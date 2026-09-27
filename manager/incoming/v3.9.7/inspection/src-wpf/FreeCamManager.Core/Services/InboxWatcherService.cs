using System.IO.Compression;
using FreeCamManager.Core.Models;

namespace FreeCamManager.Core.Services;

public sealed class InboxWatcherService
{
    private sealed record FileStamp(long Size, long WriteTicks, int Count);

    private readonly Func<AppSettings> _settings;
    private readonly LibraryService _library;
    private readonly OrganizerService _organizer;
    private readonly ManifestService _manifest;
    private readonly ClassificationService _classification;
    private readonly ExtractionService _extraction;
    private readonly TestStatusRefreshService _testRefresh;
    private readonly IAppLogger? _log;
    private readonly DiscardCleanupService? _discardCleanup;
    private readonly LibraryRebuildService? _rebuild;
    private readonly Dictionary<string, FileStamp> _seen = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FileStamp> _unrecognized = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private readonly SemaphoreSlim _scanGate = new(1, 1);

    public event EventHandler? LibraryChanged;
    public event EventHandler<string>? StatusChanged;

    public InboxWatcherService(
        Func<AppSettings> settings,
        LibraryService library,
        OrganizerService organizer,
        ManifestService manifest,
        ClassificationService classification,
        ExtractionService extraction,
        TestStatusRefreshService testRefresh,
        IAppLogger? log = null,
        DiscardCleanupService? discardCleanup = null,
        LibraryRebuildService? rebuild = null)
    {
        _settings = settings;
        _library = library;
        _organizer = organizer;
        _manifest = manifest;
        _classification = classification;
        _extraction = extraction;
        _testRefresh = testRefresh;
        _log = log;
        _discardCleanup = discardCleanup;
        _rebuild = rebuild;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var seconds = Math.Max(1, _settings().ScanSeconds);
            await Task.Delay(TimeSpan.FromSeconds(seconds), ct).ConfigureAwait(false);
            await ScanOnceAsync(ct).ConfigureAwait(false);
        }
    }

    public Task<bool> ScanOnceAsync(CancellationToken ct = default) => ScanCoreAsync(manual: false, ct);

    /// <summary>
    /// User-triggered scan. Unlike the conservative background scan, this performs
    /// a short in-call stability probe so a completed download can be processed on
    /// the first click instead of waiting for the next 3-second polling cycle.
    /// </summary>
    public Task<bool> ScanNowAsync(CancellationToken ct = default) => ScanCoreAsync(manual: true, ct);

    private async Task<bool> ScanCoreAsync(bool manual, CancellationToken ct)
    {
        await _scanGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var cfg = _settings();
            var changed = false;

            if (manual && _rebuild is not null)
            {
                try
                {
                    var rebuilt = await _rebuild.ReconcileAsync(cfg.RootDir, ct).ConfigureAwait(false);
                    if (rebuilt.Changed)
                    {
                        changed = true;
                        StatusChanged?.Invoke(this,
                            $"已重新扫描现有目录：新增 {rebuilt.Added}，测试关联 {rebuilt.TestingLinked}，Result 关联 {rebuilt.ResultsPaired}");
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _log?.Event("LIBRARY_REBUILD_MANUAL_ERROR", ("root", cfg.RootDir), ("error", ex.Message));
                    StatusChanged?.Invoke(this, "现有目录重建扫描失败: " + ex.Message);
                }
            }
            if (_discardCleanup is not null)
            {
                try
                {
                    var cleanup = await _discardCleanup.CleanupDueAsync(DateTimeOffset.Now, ct).ConfigureAwait(false);
                    if (cleanup.Deleted > 0)
                    {
                        changed = true;
                        StatusChanged?.Invoke(this, $"已自动删除 {cleanup.Deleted} 个到期的已废弃版本");
                    }
                    else if (cleanup.Failed > 0)
                    {
                        StatusChanged?.Invoke(this, $"有 {cleanup.Failed} 个到期的已废弃版本自动删除失败");
                    }
                    if (cleanup.Deleted > 0 || cleanup.Failed > 0 || cleanup.SkippedProtected > 0)
                        _log?.Event("DISCARD_CLEANUP", ("deleted", cleanup.Deleted), ("skipped_locked", cleanup.SkippedProtected), ("failed", cleanup.Failed));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _log?.Event("DISCARD_CLEANUP_ERROR", ("error", ex.Message));
                }
            }

            var inbox = cfg.InboxDir;
            if (string.IsNullOrWhiteSpace(inbox) || !Directory.Exists(inbox))
            {
                if (changed) LibraryChanged?.Invoke(this, EventArgs.Empty);
                return changed;
            }
            _log?.Event(manual ? "SCAN_MANUAL_BEGIN" : "SCAN_BEGIN", ("inbox", inbox));
            var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var processed = 0;
            var failed = 0;

            List<string> inboxFiles;
            try
            {
                inboxFiles = Directory.EnumerateFiles(inbox, "*", SearchOption.TopDirectoryOnly).ToList();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                _log?.Event("SCAN_ACCESS_ERROR", ("inbox", inbox), ("error", ex.Message));
                StatusChanged?.Invoke(this, "收件箱暂时无法访问: " + inbox);
                return false;
            }

            foreach (var path in inboxFiles)
            {
                ct.ThrowIfCancellationRequested();
                var name = Path.GetFileName(path);
                if (IsTemporaryDownload(name)) continue;
                present.Add(path);

                if (!manual && IsRememberedUnrecognized(path)) continue;
                if (!await IsReadyForProcessingAsync(path, manual, ct).ConfigureAwait(false)) continue;

                try
                {
                    var info = new FileInfo(path);
                    _log?.Event("SCAN_PROCESS_FILE", ("path", path), ("bytes", info.Exists ? info.Length : 0), ("mode", manual ? "manual" : "auto"));
                    string testingPath = "";
                    Artifact inspect;
                    if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        try { inspect = await _manifest.InspectAsync(path, ct).ConfigureAwait(false); }
                        catch { inspect = _manifest.InspectFilename(name); inspect.Path = path; }
                    }
                    else
                    {
                        inspect = _manifest.InspectFilename(name);
                        inspect.Path = path;
                    }

                    var decision = _classification.Plan(inspect);
                    if (decision.Category == "Unknown")
                    {
                        RememberUnrecognized(path);
                        _log?.Event("SCAN_UNRECOGNIZED_KEEP_INBOX", ("path", path), ("mode", manual ? "manual" : "auto"));
                        StatusChanged?.Invoke(this, "未识别，已保留在收件箱: " + name);
                        continue;
                    }

                    if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                        && decision.Category is not "Manager" and not "IndexLibrary"
                        && _extraction.ShouldExtract(inspect))
                    {
                        var result = await _extraction.ExtractToTestingAsync(path, SettingsService.TestingRoot(cfg), ct).ConfigureAwait(false);
                        testingPath = result.Destination;
                        _log?.Event("TEST_FOLDER_READY", ("path", path), ("folder", testingPath), ("status", result.Status));
                    }

                    _organizer.Root = cfg.RootDir;
                    _organizer.StableBackup = cfg.StableBackupDir;
                    var processedArtifact = await _organizer.ProcessAsync(path, ct).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(testingPath))
                    {
                        _library.SetTestingPath(processedArtifact.Path, testingPath, cfg.RootDir);
                        await _library.SaveAsync(ct).ConfigureAwait(false);
                    }
                    lock (_gate)
                    {
                        _seen.Remove(path);
                        _unrecognized.Remove(path);
                    }
                    processed++;
                    changed = true;
                    StatusChanged?.Invoke(this, (manual ? "已立即处理: " : "已自动解压/归档: ") + name);
                }
                catch (Exception ex)
                {
                    failed++;
                    _log?.Event("SCAN_PROCESS_ERROR", ("path", path), ("error", ex.Message));
                    StatusChanged?.Invoke(this, "自动处理失败: " + name + " · " + ex.Message);
                }
            }

            lock (_gate)
            {
                foreach (var path in _seen.Keys.Where(x => !present.Contains(x)).ToList()) _seen.Remove(path);
                foreach (var path in _unrecognized.Keys.Where(x => !present.Contains(x)).ToList()) _unrecognized.Remove(path);
            }

            if (await _testRefresh.RefreshAsync(ct).ConfigureAwait(false)) changed = true;
            if (changed) LibraryChanged?.Invoke(this, EventArgs.Empty);
            _log?.Event(manual ? "SCAN_MANUAL_END" : "SCAN_END", ("inbox", inbox), ("processed", processed), ("failed", failed), ("changed", changed));
            return changed;
        }
        finally
        {
            _scanGate.Release();
        }
    }

    private bool IsRememberedUnrecognized(string path)
    {
        FileInfo info;
        try { info = new FileInfo(path); }
        catch { return false; }
        if (!info.Exists) return false;

        lock (_gate)
        {
            if (_unrecognized.TryGetValue(path, out var stamp)
                && stamp.Size == info.Length
                && stamp.WriteTicks == info.LastWriteTimeUtc.Ticks)
                return true;

            _unrecognized.Remove(path);
            return false;
        }
    }

    private void RememberUnrecognized(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return;
            lock (_gate)
                _unrecognized[path] = new FileStamp(info.Length, info.LastWriteTimeUtc.Ticks, 1);
        }
        catch { }
    }

    private async Task<bool> IsReadyForProcessingAsync(string path, bool manual, CancellationToken ct)
    {
        FileInfo info;
        try { info = new FileInfo(path); }
        catch { return false; }
        if (!info.Exists) return false;

        if (!manual)
        {
            if (DateTime.UtcNow - info.LastWriteTimeUtc < TimeSpan.FromSeconds(4)) return false;
            FileStamp stamp;
            lock (_gate)
            {
                if (_seen.TryGetValue(path, out var previous) && previous.Size == info.Length && previous.WriteTicks == info.LastWriteTimeUtc.Ticks)
                    stamp = previous with { Count = previous.Count + 1 };
                else
                    stamp = new FileStamp(info.Length, info.LastWriteTimeUtc.Ticks, 1);
                _seen[path] = stamp;
            }
            return stamp.Count >= 2;
        }

        // Manual refresh should feel immediate while still refusing a file that is
        // actively being written. One short probe replaces the next polling cycle.
        var firstSize = info.Length;
        var firstWrite = info.LastWriteTimeUtc.Ticks;
        await Task.Delay(350, ct).ConfigureAwait(false);
        info.Refresh();
        if (!info.Exists || info.Length != firstSize || info.LastWriteTimeUtc.Ticks != firstWrite) return false;

        if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var archive = ZipFile.OpenRead(path);
                _ = archive.Entries.Count; // Force central-directory validation.
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                _log?.Event("SCAN_MANUAL_NOT_READY", ("path", path), ("error", ex.Message));
                return false;
            }
        }

        lock (_gate) _seen[path] = new FileStamp(info.Length, info.LastWriteTimeUtc.Ticks, 2);
        return true;
    }

    private static bool IsTemporaryDownload(string name)
    {
        var lower = name.ToLowerInvariant();
        return lower.EndsWith(".crdownload") || lower.EndsWith(".part") || lower.EndsWith(".tmp") || lower.EndsWith(".download");
    }
}
