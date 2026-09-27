using System.Text.Json;
using System.Text.Json.Serialization;
using FreeCamManager.Core.Models;

namespace FreeCamManager.Core.Services;

public sealed class LibraryService
{
    private sealed class LibraryDocument
    {
        [JsonPropertyName("items")] public List<Artifact> Items { get; set; } = [];
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly object _gate = new();
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly string _file;
    private readonly List<Artifact> _items;
    private readonly Func<IReadOnlyList<Artifact>, CancellationToken, Task>? _snapshotSaver;

    private LibraryService(string file, List<Artifact> items, Func<IReadOnlyList<Artifact>, CancellationToken, Task>? snapshotSaver = null)
    {
        _file = file;
        _items = items;
        _snapshotSaver = snapshotSaver;
    }

    public static LibraryService CreateInMemory(IEnumerable<Artifact>? items = null)
        => new("", items?.Select(x => x.Clone()).ToList() ?? []);

    public static LibraryService CreateEmpty(string file)
        => new(file, []);

    public static LibraryService CreatePersistent(
        IEnumerable<Artifact> items,
        Func<IReadOnlyList<Artifact>, CancellationToken, Task> saveSnapshot)
    {
        ArgumentNullException.ThrowIfNull(saveSnapshot);
        var normalized = CreateInMemory(items).Snapshot();
        return new LibraryService("", normalized.Select(x => x.Clone()).ToList(), saveSnapshot);
    }

    public static async Task<LibraryService> LoadAsync(string file, CancellationToken ct = default)
    {
        if (!File.Exists(file)) return new LibraryService(file, []);
        try
        {
            return await LoadDocumentAsync(file, file, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            var backup = file + ".bak";
            if (!File.Exists(backup)) throw;

            var recovered = await LoadDocumentAsync(backup, file, ct).ConfigureAwait(false);
            var recoveryTmp = file + ".recovery.tmp";
            try
            {
                File.Copy(backup, recoveryTmp, true);
                File.Move(recoveryTmp, file, true);
            }
            finally
            {
                TryDelete(recoveryTmp);
            }
            return recovered;
        }
    }

    private static async Task<LibraryService> LoadDocumentAsync(string sourceFile, string targetFile, CancellationToken ct)
    {
        await using var stream = File.OpenRead(sourceFile);
        var doc = await JsonSerializer.DeserializeAsync<LibraryDocument>(stream, JsonOptions, ct).ConfigureAwait(false)
            ?? throw new InvalidDataException("library JSON is empty");
        doc.Items ??= [];
        foreach (var item in doc.Items) Migrate(item);
        return new LibraryService(targetFile, doc.Items);
    }

    public IReadOnlyList<Artifact> Snapshot()
    {
        lock (_gate) return _items.Select(x => x.Clone()).ToList();
    }

    public Artifact? ByPath(string path)
    {
        lock (_gate) return _items.FirstOrDefault(x => SamePath(x.Path, path))?.Clone();
    }

    public Artifact? ByHash(string sha256)
    {
        if (string.IsNullOrWhiteSpace(sha256)) return null;
        lock (_gate) return _items.FirstOrDefault(x => string.Equals(x.Sha256, sha256, StringComparison.OrdinalIgnoreCase))?.Clone();
    }

    public void Upsert(Artifact incoming)
    {
        lock (_gate)
        {
            var index = _items.FindIndex(x => SamePath(x.Path, incoming.Path));
            if (index < 0) { Migrate(incoming); _items.Add(incoming.Clone()); return; }
            var old = _items[index];
            if (string.IsNullOrEmpty(incoming.RelativePath)) incoming.RelativePath = old.RelativePath;
            if (string.IsNullOrEmpty(incoming.Version)) incoming.Version = old.Version;
            if (string.IsNullOrEmpty(incoming.BuildName)) incoming.BuildName = old.BuildName;
            if (incoming.Tags.Count == 0) incoming.Tags = [.. old.Tags];
            if (string.IsNullOrEmpty(incoming.Notes)) incoming.Notes = old.Notes;
            if (string.IsNullOrEmpty(incoming.Status)) incoming.Status = old.Status;
            if (string.IsNullOrEmpty(incoming.TestStatus)) incoming.TestStatus = old.TestStatus;
            if (string.IsNullOrEmpty(incoming.ManualStatus)) incoming.ManualStatus = old.ManualStatus;
            if (string.IsNullOrEmpty(incoming.TestingPath)) incoming.TestingPath = old.TestingPath;
            if (string.IsNullOrEmpty(incoming.TestingRelativePath)) incoming.TestingRelativePath = old.TestingRelativePath;
            if (string.IsNullOrEmpty(incoming.ResultPath)) incoming.ResultPath = old.ResultPath;
            if (string.IsNullOrEmpty(incoming.ResultRelativePath)) incoming.ResultRelativePath = old.ResultRelativePath;
            if (string.IsNullOrEmpty(incoming.LastTestedAt)) incoming.LastTestedAt = old.LastTestedAt;
            if (string.IsNullOrEmpty(incoming.TestStartedAt)) incoming.TestStartedAt = old.TestStartedAt;
            if (string.IsNullOrEmpty(incoming.AutoDeleteAt)) incoming.AutoDeleteAt = old.AutoDeleteAt;
            if (incoming.Rating == 0) incoming.Rating = old.Rating;
            incoming.Favorite = incoming.Favorite || old.Favorite;
            incoming.Protected = incoming.Protected || old.Protected;
            Migrate(incoming);
            _items[index] = incoming.Clone();
        }
    }

    public bool ReplacePath(string oldPath, Artifact incoming)
    {
        lock (_gate)
        {
            var index = _items.FindIndex(x => SamePath(x.Path, oldPath));
            if (index < 0) return false;
            var old = _items[index];
            if (string.IsNullOrEmpty(incoming.RelativePath)) incoming.RelativePath = old.RelativePath;
            if (string.IsNullOrEmpty(incoming.Version)) incoming.Version = old.Version;
            if (string.IsNullOrEmpty(incoming.BuildName)) incoming.BuildName = old.BuildName;
            if (incoming.Tags.Count == 0) incoming.Tags = [.. old.Tags];
            if (string.IsNullOrEmpty(incoming.Notes)) incoming.Notes = old.Notes;
            if (string.IsNullOrEmpty(incoming.TestStatus)) incoming.TestStatus = old.TestStatus;
            if (string.IsNullOrEmpty(incoming.ManualStatus)) incoming.ManualStatus = old.ManualStatus;
            if (string.IsNullOrEmpty(incoming.TestingPath)) incoming.TestingPath = old.TestingPath;
            if (string.IsNullOrEmpty(incoming.TestingRelativePath)) incoming.TestingRelativePath = old.TestingRelativePath;
            if (string.IsNullOrEmpty(incoming.ResultPath)) incoming.ResultPath = old.ResultPath;
            if (string.IsNullOrEmpty(incoming.ResultRelativePath)) incoming.ResultRelativePath = old.ResultRelativePath;
            if (string.IsNullOrEmpty(incoming.LastTestedAt)) incoming.LastTestedAt = old.LastTestedAt;
            if (string.IsNullOrEmpty(incoming.TestStartedAt)) incoming.TestStartedAt = old.TestStartedAt;
            if (string.IsNullOrEmpty(incoming.AutoDeleteAt)) incoming.AutoDeleteAt = old.AutoDeleteAt;
            if (incoming.Rating == 0) incoming.Rating = old.Rating;
            incoming.Favorite = incoming.Favorite || old.Favorite;
            incoming.Protected = incoming.Protected || old.Protected;
            Migrate(incoming);

            // Path rebasing can make a stale record converge on a path that is already
            // represented by another artifact. JSON tolerated that historical state,
            // but SQLite correctly enforces one row per physical path. Merge the two
            // records here, at the in-memory invariant boundary, before persistence.
            var collisionIndex = _items.FindIndex(i => !ReferenceEquals(i, old) && SamePath(i.Path, incoming.Path));
            if (collisionIndex >= 0 && collisionIndex != index)
            {
                var merged = MergePathCollision(_items[collisionIndex], incoming);
                _items[collisionIndex] = merged;
                _items.RemoveAt(index);
                return true;
            }

            _items[index] = incoming.Clone();
            return true;
        }
    }

    public bool RemoveByPath(string path)
    {
        lock (_gate)
        {
            var index = _items.FindIndex(x => SamePath(x.Path, path));
            if (index < 0) return false;
            _items.RemoveAt(index);
            return true;
        }
    }

    public bool SetManualStatus(string path, string status) => Mutate(path, a =>
    {
        a.ManualStatus = NormalizeManualStatus(status);
        if (!string.Equals(a.ManualStatus, "已废弃", StringComparison.Ordinal)) a.AutoDeleteAt = "";
    });

    public bool SetAutoDeleteAt(string path, DateTimeOffset dueAt) => Mutate(path, a => a.AutoDeleteAt = dueAt.ToString("O"));
    public bool ClearAutoDeleteAt(string path) => Mutate(path, a => a.AutoDeleteAt = "");

    public bool SetRating(string path, int rating) => Mutate(path, a =>
    {
        a.Rating = Math.Clamp(rating, 0, 5);
        a.Favorite = a.Rating > 0;
        if (a.Rating == 5) a.Protected = true;
    });

    public (bool Locked, bool Found) ToggleProtected(string path)
    {
        lock (_gate)
        {
            var item = _items.FirstOrDefault(x => SamePath(x.Path, path));
            if (item is null) return (false, false);
            item.Protected = !item.Protected;
            return (item.Protected, true);
        }
    }

    public bool SetTags(string path, IEnumerable<string> tags) => Mutate(path, a => a.Tags = UniqueTags(tags));
    public bool SetNotes(string path, string notes) => Mutate(path, a => a.Notes = notes ?? "");
    public bool SetTestingPath(string path, string testingPath, string root = "") => Mutate(path, a =>
    {
        a.TestingPath = testingPath ?? "";
        a.TestingRelativePath = !string.IsNullOrWhiteSpace(root) ? PathRebaseService.TryMakeRelative(root, a.TestingPath) : "";
        if (string.IsNullOrWhiteSpace(a.TestStatus)) a.TestStatus = "待测试";
    });

    public bool ClearTestingPath(string path) => Mutate(path, a =>
    {
        a.TestingPath = "";
        a.TestingRelativePath = "";
        if (a.TestStatus == "测试中")
        {
            a.TestStatus = "待测试";
            a.Status = "待测试";
        }
    });

    public bool SetTestEvidence(string path, string evidencePath, string root = "", string? testedAt = null) => Mutate(path, a =>
    {
        a.ResultPath = evidencePath ?? "";
        a.ResultRelativePath = !string.IsNullOrWhiteSpace(root) ? PathRebaseService.TryMakeRelative(root, a.ResultPath) : "";
        a.LastTestedAt = testedAt ?? DateTimeOffset.Now.ToString("O");
        a.TestStatus = "已测试";
        a.Status = "已测试";
    });

    public bool MarkTestStarted(string path, string? startedAt = null) => Mutate(path, a =>
    {
        a.TestStartedAt = startedAt ?? DateTimeOffset.Now.ToString("O");
        a.TestStatus = "测试中";
        a.Status = "测试中";
    });

    public bool MarkTestStatus(string path, string status) => Mutate(path, a =>
    {
        a.TestStatus = status;
        if (status is "待测试" or "测试中" or "已测试") a.Status = status;
    });

    public void PairResults()
    {
        lock (_gate)
        {
            foreach (var result in _items.Where(x => Eq(x.ArtifactType, "Result")).ToList())
            {
                foreach (var build in _items.Where(x => !Eq(x.ArtifactType, "Result")))
                {
                    var match = !string.IsNullOrWhiteSpace(result.ForBuildId) && Eq(build.BuildId, result.ForBuildId);
                    if (!match && string.IsNullOrWhiteSpace(result.ForBuildId))
                    {
                        match = Eq(build.Base, result.Base) && Eq(build.Feature, result.Feature) &&
                                Eq(build.Stage, result.Stage) && !string.IsNullOrWhiteSpace(result.Stage);
                    }
                    if (!match) continue;
                    var id = !string.IsNullOrWhiteSpace(build.BuildId) ? build.BuildId : result.ForBuildId;
                    build.TestStatus = "已测试";
                    build.Status = "已测试";
                    build.PairedBuildId = id;
                    build.ResultPath = result.Path;
                    build.ResultRelativePath = result.RelativePath;
                    if (string.IsNullOrWhiteSpace(build.LastTestedAt))
                        build.LastTestedAt = !string.IsNullOrWhiteSpace(result.ImportedAt) ? result.ImportedAt : result.BuildDate;
                    result.PairedBuildId = id;
                }
            }
        }
    }

    public async Task SaveAsync(CancellationToken ct = default)
    {
        if (_snapshotSaver is not null)
        {
            await _saveGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                List<Artifact> persistentSnapshot;
                lock (_gate) persistentSnapshot = _items.Select(x => x.Clone()).ToList();
                await _snapshotSaver(persistentSnapshot, ct).ConfigureAwait(false);
            }
            finally
            {
                _saveGate.Release();
            }
            return;
        }

        if (string.IsNullOrWhiteSpace(_file)) return;
        await _saveGate.WaitAsync(ct).ConfigureAwait(false);
        var tmp = _file + ".tmp";
        var backup = _file + ".bak";
        var backupTmp = backup + ".tmp";
        try
        {
            List<Artifact> snapshot;
            lock (_gate) snapshot = _items.Select(x => x.Clone()).ToList();
            var parent = Path.GetDirectoryName(_file);
            if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);

            await using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, true))
            {
                await JsonSerializer.SerializeAsync(stream, new LibraryDocument { Items = snapshot }, JsonOptions, ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            await ValidateLibraryFileAsync(tmp, ct).ConfigureAwait(false);

            if (File.Exists(_file) && await IsValidLibraryFileAsync(_file, ct).ConfigureAwait(false))
            {
                File.Copy(_file, backupTmp, true);
                await ValidateLibraryFileAsync(backupTmp, ct).ConfigureAwait(false);
                File.Move(backupTmp, backup, true);
            }

            File.Move(tmp, _file, true);
        }
        finally
        {
            TryDelete(tmp);
            TryDelete(backupTmp);
            _saveGate.Release();
        }
    }

    private static async Task ValidateLibraryFileAsync(string file, CancellationToken ct)
    {
        await using var stream = File.OpenRead(file);
        var doc = await JsonSerializer.DeserializeAsync<LibraryDocument>(stream, JsonOptions, ct).ConfigureAwait(false);
        if (doc is null || doc.Items is null) throw new InvalidDataException("library JSON validation failed");
    }

    private static async Task<bool> IsValidLibraryFileAsync(string file, CancellationToken ct)
    {
        try
        {
            await ValidateLibraryFileAsync(file, ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { }
    }

    public static string DisplayStatus(Artifact a)
    {
        var system = a.TestStatus;
        var manual = a.ManualStatus;
        if (string.IsNullOrWhiteSpace(system) && a.Status is "待测试" or "已测试") system = a.Status;
        if (string.IsNullOrWhiteSpace(manual) && a.Status is "通过" or "失败" or "待复测" or "已废弃") manual = a.Status;
        if (!string.IsNullOrWhiteSpace(system) && !string.IsNullOrWhiteSpace(manual)) return system + " · " + manual;
        return !string.IsNullOrWhiteSpace(manual) ? manual : !string.IsNullOrWhiteSpace(system) ? system : a.Status;
    }

    private bool Mutate(string path, Action<Artifact> mutation)
    {
        lock (_gate)
        {
            var item = _items.FirstOrDefault(x => SamePath(x.Path, path));
            if (item is null) return false;
            mutation(item);
            return true;
        }
    }

    private static void Migrate(Artifact a)
    {
        if (a.Rating == 0 && a.Favorite) a.Rating = 5;
        a.Rating = Math.Clamp(a.Rating, 0, 5);
        a.Tags ??= [];
        if (string.IsNullOrWhiteSpace(a.TestStatus) && a.Status is "待测试" or "已测试") a.TestStatus = a.Status;
        if (string.IsNullOrWhiteSpace(a.ManualStatus) && a.Status is "通过" or "失败" or "待复测" or "已废弃") a.ManualStatus = a.Status;
        if (a.Category is "Stable" or "StableCandidate" && string.IsNullOrWhiteSpace(a.Version))
            a.Version = StableVersionResolver.Resolve(a);
    }

    private static Artifact MergePathCollision(Artifact authoritative, Artifact rebased)
    {
        // The record that already owns the target path is authoritative for identity
        // and conflicting scalar fields. User metadata is merged conservatively so a
        // stale-path repair cannot silently erase ratings, locks, tags or notes.
        var merged = authoritative.Clone();
        merged.Path = rebased.Path;
        merged.RelativePath = FirstNonEmpty(authoritative.RelativePath, rebased.RelativePath);
        merged.Name = FirstNonEmpty(authoritative.Name, rebased.Name);
        merged.SchemaVersion = Math.Max(authoritative.SchemaVersion, rebased.SchemaVersion);
        merged.Project = FirstNonEmpty(authoritative.Project, rebased.Project);
        merged.Version = FirstNonEmpty(authoritative.Version, rebased.Version);
        merged.BuildName = FirstNonEmpty(authoritative.BuildName, rebased.BuildName);
        merged.Base = FirstNonEmpty(authoritative.Base, rebased.Base);
        merged.Branch = FirstNonEmpty(authoritative.Branch, rebased.Branch);
        merged.Feature = FirstNonEmpty(authoritative.Feature, rebased.Feature);
        merged.BuildType = FirstNonEmpty(authoritative.BuildType, rebased.BuildType);
        merged.ArtifactType = FirstNonEmpty(authoritative.ArtifactType, rebased.ArtifactType);
        merged.Stage = FirstNonEmpty(authoritative.Stage, rebased.Stage);
        merged.BuildId = FirstNonEmpty(authoritative.BuildId, rebased.BuildId);
        merged.ParentBuildId = FirstNonEmpty(authoritative.ParentBuildId, rebased.ParentBuildId);
        merged.ForBuildId = FirstNonEmpty(authoritative.ForBuildId, rebased.ForBuildId);
        merged.Commit = FirstNonEmpty(authoritative.Commit, rebased.Commit);
        merged.SourceMode = FirstNonEmpty(authoritative.SourceMode, rebased.SourceMode);
        merged.SourceState = FirstNonEmpty(authoritative.SourceState, rebased.SourceState);
        merged.ReleaseState = FirstNonEmpty(authoritative.ReleaseState, rebased.ReleaseState);
        merged.BuildDate = FirstNonEmpty(authoritative.BuildDate, rebased.BuildDate);
        merged.ManifestFound = authoritative.ManifestFound || rebased.ManifestFound;
        merged.ManifestName = FirstNonEmpty(authoritative.ManifestName, rebased.ManifestName);
        merged.Sha256 = FirstNonEmpty(authoritative.Sha256, rebased.Sha256);
        merged.Size = authoritative.Size > 0 ? authoritative.Size : rebased.Size;
        merged.Category = FirstNonEmpty(authoritative.Category, rebased.Category);

        merged.ManualStatus = FirstNonEmpty(NormalizeManualStatus(authoritative.ManualStatus), NormalizeManualStatus(rebased.ManualStatus));
        merged.TestStatus = StrongerTestStatus(authoritative.TestStatus, rebased.TestStatus);
        merged.Status = !string.IsNullOrWhiteSpace(merged.TestStatus)
            ? merged.TestStatus
            : !string.IsNullOrWhiteSpace(merged.ManualStatus)
                ? merged.ManualStatus
                : FirstNonEmpty(authoritative.Status, rebased.Status);

        merged.TestingPath = PreferDirectory(authoritative.TestingPath, rebased.TestingPath);
        merged.TestingRelativePath = merged.TestingPath == authoritative.TestingPath
            ? FirstNonEmpty(authoritative.TestingRelativePath, rebased.TestingRelativePath)
            : FirstNonEmpty(rebased.TestingRelativePath, authoritative.TestingRelativePath);
        merged.ResultPath = PreferFile(authoritative.ResultPath, rebased.ResultPath);
        merged.ResultRelativePath = merged.ResultPath == authoritative.ResultPath
            ? FirstNonEmpty(authoritative.ResultRelativePath, rebased.ResultRelativePath)
            : FirstNonEmpty(rebased.ResultRelativePath, authoritative.ResultRelativePath);
        merged.LastTestedAt = LaterTimestamp(authoritative.LastTestedAt, rebased.LastTestedAt);
        merged.TestStartedAt = LaterTimestamp(authoritative.TestStartedAt, rebased.TestStartedAt);

        merged.Tags = UniqueTags((authoritative.Tags ?? []).Concat(rebased.Tags ?? []));
        merged.Notes = MergeNotes(authoritative.Notes, rebased.Notes);
        merged.Rating = Math.Max(authoritative.Rating, rebased.Rating);
        merged.Favorite = authoritative.Favorite || rebased.Favorite || merged.Rating > 0;
        merged.Protected = authoritative.Protected || rebased.Protected;
        merged.PairedBuildId = FirstNonEmpty(authoritative.PairedBuildId, rebased.PairedBuildId);
        merged.ImportedAt = FirstNonEmpty(authoritative.ImportedAt, rebased.ImportedAt);
        merged.DuplicateOf = FirstNonEmpty(authoritative.DuplicateOf, rebased.DuplicateOf);
        merged.AutoDeleteAt = string.Equals(merged.ManualStatus, "已废弃", StringComparison.Ordinal)
            ? FirstNonEmpty(authoritative.AutoDeleteAt, rebased.AutoDeleteAt)
            : "";

        Migrate(merged);
        return merged;
    }

    private static string StrongerTestStatus(string a, string b)
    {
        static int Rank(string value) => value switch
        {
            "已测试" => 3,
            "测试中" => 2,
            "待测试" => 1,
            _ => 0
        };
        return Rank(a) >= Rank(b) ? a ?? "" : b ?? "";
    }

    private static string PreferDirectory(string primary, string secondary)
    {
        if (!string.IsNullOrWhiteSpace(primary) && Directory.Exists(primary)) return primary;
        if (!string.IsNullOrWhiteSpace(secondary) && Directory.Exists(secondary)) return secondary;
        return FirstNonEmpty(primary, secondary);
    }

    private static string PreferFile(string primary, string secondary)
    {
        if (!string.IsNullOrWhiteSpace(primary) && File.Exists(primary)) return primary;
        if (!string.IsNullOrWhiteSpace(secondary) && File.Exists(secondary)) return secondary;
        return FirstNonEmpty(primary, secondary);
    }

    private static string LaterTimestamp(string a, string b)
    {
        var hasA = DateTimeOffset.TryParse(a, out var parsedA);
        var hasB = DateTimeOffset.TryParse(b, out var parsedB);
        if (hasA && hasB) return parsedA >= parsedB ? a : b;
        return FirstNonEmpty(a, b);
    }

    private static string MergeNotes(string primary, string secondary)
    {
        primary ??= "";
        secondary ??= "";
        if (string.IsNullOrWhiteSpace(primary)) return secondary;
        if (string.IsNullOrWhiteSpace(secondary)) return primary;
        if (string.Equals(primary.Trim(), secondary.Trim(), StringComparison.Ordinal)) return primary;
        return primary.TrimEnd() + Environment.NewLine + secondary.TrimStart();
    }

    private static string FirstNonEmpty(string primary, string secondary)
        => !string.IsNullOrWhiteSpace(primary) ? primary : secondary ?? "";

    private static string NormalizeManualStatus(string value)
    {
        value = value?.Trim() ?? "";
        return value == "未标记" ? "" : value;
    }

    private static List<string> UniqueTags(IEnumerable<string> tags)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var raw in tags ?? [])
        {
            var tag = raw?.Trim() ?? "";
            if (tag.Length > 0 && seen.Add(tag)) result.Add(tag);
        }
        return result;
    }

    private static bool SamePath(string a, string b)
    {
        try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
        catch { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
    }

    private static bool Eq(string a, string b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
}
