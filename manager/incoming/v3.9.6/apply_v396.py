from pathlib import Path
import json
import sys

root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(".")

def load(path):
    target = root / path
    return target, target.read_text(encoding="utf-8-sig")

def save(target, value):
    target.write_text(value, encoding="utf-8")

def replace_once(source, before, after, label):
    count = source.count(before)
    if count != 1:
        raise SystemExit(f"{label}: expected exactly 1 match, found {count}")
    return source.replace(before, after, 1)

app_path, app = load("src-wpf/FreeCamManager/App.xaml.cs")
app = replace_once(
    app,
    "    private Task? _watcherTask;\n",
    "    private Task? _watcherTask;\n    private Task? _startupMaintenanceTask;\n",
    "startup maintenance task field"
)

# Preserve all existing path repair/rebuild/stable repair/discard cleanup logic,
# but execute these I/O-intensive stages on a worker after ContentRendered.
begin = '            stage = "重定位版本库文件路径";'
end = '            var dialogs = new UserDialogService();'
if app.count(begin) != 1 or app.count(end) != 1:
    raise SystemExit("cannot locate exact original startup maintenance block")
start = app.index(begin)
stop = app.index(end, start)
old_stages = app[start:stop]
old_stages = replace_once(
    old_stages,
    "new PathRebaseService().RepairLibraryPathsAsync(_library, settings.RootDir)",
    "new PathRebaseService().RepairLibraryPathsAsync(_library!, settings.RootDir, ct)",
    "cancellable path repair"
)
old_stages = replace_once(
    old_stages,
    "libraryRebuild.ReconcileAsync(settings.RootDir)",
    "libraryRebuild.ReconcileAsync(settings.RootDir, ct)",
    "cancellable root reconciliation"
)
old_stages = replace_once(
    old_stages,
    "organizer.RepairStableCandidateDirectoriesAsync()",
    "organizer.RepairStableCandidateDirectoriesAsync(ct)",
    "cancellable stable repair"
)
old_stages = replace_once(
    old_stages,
    "discardCleanup.CleanupDueAsync(DateTimeOffset.Now)",
    "discardCleanup.CleanupDueAsync(DateTimeOffset.Now, ct)",
    "cancellable cleanup"
)
app = app[:start] + '            startupTiming.Mark("MAINTENANCE_DEFERRED");\n\n' + app[stop:]

app = replace_once(
    app,
    "            startupTiming.Mark(\"WATCHER_READY\");\n\n            stage = \"创建主界面\";",
    "            startupTiming.Mark(\"WATCHER_READY\");\n            var maintenanceStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);\n\n            stage = \"创建主界面\";",
    "maintenance gate"
)
app = replace_once(
    app,
    "filenameAliases, () => watcher.ScanNowAsync(), cfg => ApplyRuntimeSettings(cfg), localTermsState,",
    """filenameAliases, async () =>
                {
                    await maintenanceStarted.Task.WaitAsync(_shutdown.Token);
                    if (_startupMaintenanceTask is not null)
                        await _startupMaintenanceTask.ConfigureAwait(false);
                    return await watcher.ScanNowAsync(_shutdown.Token).ConfigureAwait(false);
                }, cfg => ApplyRuntimeSettings(cfg), localTermsState,""",
    "manual scan waits for startup maintenance"
)
app = replace_once(
    app,
    """            window.ContentRendered += (_, _) =>
            {
                startupTiming.Mark("FIRST_RENDER");
                startupTiming.Flush("FIRST_RENDER");
            };""",
    """            var maintenanceLaunched = false;
            window.ContentRendered += (_, _) =>
            {
                if (maintenanceLaunched) return;
                maintenanceLaunched = true;
                startupTiming.Mark("FIRST_RENDER");
                startupTiming.Flush("FIRST_RENDER");

                // Browsing and navigation are available as soon as WPF renders;
                // the previous blocking root scan now runs off the UI thread.
                _mainViewModel.StatusText = "主界面已显示，版本库正在后台校验…";
                _startupMaintenanceTask = Task.Run(() => RunStartupMaintenanceAsync(
                    settings, organizer, libraryRebuild, discardCleanup, startupTiming, _shutdown.Token));
                var maintenance = _startupMaintenanceTask!;
                maintenanceStarted.TrySetResult(true);

                // Reconciliation, automatic inbox imports, and manual scans must
                // not mutate the same library simultaneously.
                _watcherTask = Task.Run(async () =>
                {
                    try
                    {
                        await maintenance.ConfigureAwait(false);
                        if (!_shutdown.IsCancellationRequested)
                            await watcher.StartAsync(_shutdown.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
                    catch (Exception ex) { _log?.Event("WATCHER_FATAL", ("error", ex.ToString())); }
                });
            };""",
    "post-render maintenance launch"
)
app = replace_once(
    app,
    """            stage = "启动收件箱监控";
            _watcherTask = Task.Run(async () =>
            {
                try { await watcher.StartAsync(_shutdown.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
                catch (Exception ex) { _log?.Event("WATCHER_FATAL", ("error", ex.ToString())); }
            });

            _termsUpdateTask""",
    """            stage = "启动后台更新检查";
            // Inbox watcher starts when the post-render reconciliation completes.
            _termsUpdateTask""",
    "remove pre-maintenance watcher"
)
app = replace_once(
    app,
    """        _shutdown.Cancel();
        try { _watcherTask?.GetAwaiter().GetResult(); }""",
    """        _shutdown.Cancel();
        try { _startupMaintenanceTask?.GetAwaiter().GetResult(); } catch (OperationCanceledException) { } catch (Exception ex) { _log?.Event("STARTUP_MAINTENANCE_STOP_ERROR", ("error", ex.Message)); }
        try { _watcherTask?.GetAwaiter().GetResult(); }""",
    "wait for maintenance on exit"
)

method_prefix = """    private async Task RunStartupMaintenanceAsync(
        AppSettings settings, OrganizerService organizer, LibraryRebuildService libraryRebuild,
        DiscardCleanupService discardCleanup, StartupTimingRecorder startupTiming, CancellationToken ct)
    {
        var startupWarnings = new List<string>();
        var stage = "后台版本库校验";
        var clock = Stopwatch.StartNew();
        _log?.Event("STARTUP_MAINTENANCE_BEGIN", ("root", settings.RootDir));
        try
        {
            ct.ThrowIfCancellationRequested();
"""
method_suffix = """        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _log?.Event("STARTUP_MAINTENANCE_CANCELLED");
        }
        catch (Exception ex)
        {
            startupWarnings.Add($"后台版本库校验中断（{stage}）：{ex.Message}，可以稍后手动刷新重试。");
            _log?.Event("STARTUP_MAINTENANCE_ERROR", ("stage", stage), ("error", ex.ToString()));
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                startupTiming.Mark("BACKGROUND_MAINTENANCE_DONE", ("elapsed_ms", clock.ElapsedMilliseconds));
                startupTiming.Flush("BACKGROUND_MAINTENANCE_DONE");
                _log?.Event("STARTUP_MAINTENANCE_DONE",
                    ("elapsed_ms", clock.ElapsedMilliseconds), ("warning_count", startupWarnings.Count));
                Dispatch(() =>
                {
                    if (_shutdown.IsCancellationRequested || _mainViewModel is null) return;
                    _mainViewModel.StatusText = startupWarnings.Count > 0
                        ? string.Join(" · ", startupWarnings)
                        : "版本库后台校验完成，收件箱监控已启动";
                    _ = _mainViewModel.RefreshAllAsync();
                });
            }
        }
    }

"""
app = replace_once(
    app,
    "    protected override void OnExit(ExitEventArgs e)",
    method_prefix + old_stages + method_suffix + "    protected override void OnExit(ExitEventArgs e)",
    "insert background startup maintenance"
)
save(app_path, app)

# Background timings may overlap the normal startup logger's FIRST_RENDER/APP_READY
# marks. Keep delta measurements and file flush serialized.
timing_path, timing = load("src-wpf/FreeCamManager/Services/StartupTimingRecorder.cs")
timing = replace_once(
    timing,
    "    private readonly object _sync = new();",
    "    private readonly object _sync = new();\n    private readonly object _flushSync = new();",
    "timer flush gate"
)
mark_begin = "    public void Mark(string name, params (string Key, object? Value)[] fields)"
mark_end = "    public void Flush(string reason)"
m1 = timing.index(mark_begin)
m2 = timing.index(mark_end, m1)
mark = """    public void Mark(string name, params (string Key, object? Value)[] fields)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        var detail = fields.Length == 0
            ? ""
            : string.Join(";", fields.Select(x => $"{x.Key}={Sanitize(x.Value)}"));
        lock (_sync)
        {
            var elapsedMs = _clock.Elapsed.TotalMilliseconds;
            var deltaMs = elapsedMs - _lastElapsedMs;
            _lastElapsedMs = elapsedMs;
            _entries.Add(new Entry(name.Trim(), elapsedMs, deltaMs, DateTimeOffset.Now, detail));
        }
    }

"""
timing = timing[:m1] + mark + timing[m2:]
timing = replace_once(
    timing,
    """        try
        {
            List<Entry> pending;
            lock (_sync)
""",
    """        try
        {
            lock (_flushSync)
            {
            List<Entry> pending;
            lock (_sync)
""",
    "serialize timing flush"
)
timing = replace_once(
    timing,
    """            File.AppendAllText(FilePath, sb.ToString(), new UTF8Encoding(false));
        }
        catch""",
    """            File.AppendAllText(FilePath, sb.ToString(), new UTF8Encoding(false));
            }
        }
        catch""",
    "finish serialized timing flush"
)
save(timing_path, timing)

p, proj = load("src-wpf/FreeCamManager/FreeCamManager.csproj")
proj = replace_once(proj, "<Version>3.9.5</Version>", "<Version>3.9.6</Version>", "version bump")
save(p, proj)
manifest_path = root / "BUILD_MANIFEST.json"
manifest = json.loads(manifest_path.read_text(encoding="utf-8-sig"))
manifest.update({
    "Version": "V3.9.6",
    "BuildName": "FreeCam_Manager_V3.9.6",
    "Base": "FreeCam_Manager_V3.9.5",
    "Branch": "feature/manager-v3.9.6-fast-startup",
    "Feature": "Nonblocking Startup Library Reconciliation",
    "Stage": "Release",
    "BuildId": "MANAGER-V396-FASTSTART-20260923"
})
manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

# Permanently use numeric three-part versions in every future UI/log display.
p, version = load("src-wpf/FreeCamManager.Core/Services/AppVersionService.cs")
version = replace_once(
    version,
    '''        var baseText = $"V{normalized.Major}.{normalized.Minor}";
        return normalized.Build > 0 ? $"{baseText} Fix{normalized.Build}" : baseText;''',
    '''        return $"V{normalized.Major}.{normalized.Minor}.{normalized.Build}";''',
    "three-part display version"
)
save(p, version)

# Include the startup ordering and version-format regression in the distributed test suite.
p, tests = load("src-wpf/FreeCamManager.Tests/Program.cs")
tests = replace_once(
    tests,
    '        await Run("V3.7 Fix1 startup and manual refresh wire recovery reconciliation", V371RecoveryWiringContract);',
    '        await Run("V3.7 Fix1 startup and manual refresh wire recovery reconciliation", V371RecoveryWiringContract);\n'
    '        await Run("V3.9.6 first frame precedes background reconciliation", V396FirstFrameStartupContract);\n'
    '        await Run("V3.9.6 version strings use three numeric components", V396ThreePartVersionDisplay);',
    "register new startup/version regression tests"
)
tests = replace_once(
    tests,
    '    private static string TempDir()',
    '''    private static Task V396FirstFrameStartupContract()
    {
        var sourceRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "FreeCamManager"));
        var source = File.ReadAllText(Path.Combine(sourceRoot, "App.xaml.cs"));
        var startup = source.Split("protected override async void OnStartup", 2, StringSplitOptions.None)[1]
            .Split("private async Task RunStartupMaintenanceAsync", 2, StringSplitOptions.None)[0];
        var render = startup.IndexOf("window.ContentRendered +=", StringComparison.Ordinal);
        var launch = startup.IndexOf("_startupMaintenanceTask = Task.Run(", StringComparison.Ordinal);
        var show = startup.IndexOf("window.Show();", StringComparison.Ordinal);
        Assert(render >= 0 && launch > render && show > launch,
            "startup maintenance must launch from the first ContentRendered callback");
        Assert(!startup[..render].Contains("await libraryRebuild.ReconcileAsync(", StringComparison.Ordinal),
            "startup must show the window before blocking root reconciliation");
        Assert(startup.Contains("await maintenanceStarted.Task.WaitAsync(_shutdown.Token)", StringComparison.Ordinal)
            && startup.Contains("await maintenance.ConfigureAwait(false)", StringComparison.Ordinal),
            "manual scans and the inbox watcher must wait until startup maintenance is finished");
        Assert(source.Contains("RunStartupMaintenanceAsync(", StringComparison.Ordinal)
            && source.Contains("libraryRebuild.ReconcileAsync(settings.RootDir, ct)", StringComparison.Ordinal)
            && source.Contains("startupTiming.Flush(\"BACKGROUND_MAINTENANCE_DONE\")", StringComparison.Ordinal),
            "background repair must still reconcile the library and record completion");
        return Task.CompletedTask;
    }

    private static Task V396ThreePartVersionDisplay()
    {
        Assert(AppVersionService.FormatDisplay(new Version(3, 9, 6)) == "V3.9.6",
            "Manager must display V3.9.6, not V3.9 Fix6");
        Assert(AppVersionService.FormatDisplay(new Version(3, 9, 5)) == "V3.9.5",
            "Manager must display V3.9.5, not V3.9 Fix5");
        Assert(AppVersionService.FormatDisplay(new Version(3, 10, 0)) == "V3.10.0",
            "three-part display must retain zero patch versions");
        return Task.CompletedTask;
    }

    private static string TempDir()''',
    "embed startup and version tests"
)
save(p, tests)

print("Applied FreeCam Manager V3.9.6 post-render startup patch")
