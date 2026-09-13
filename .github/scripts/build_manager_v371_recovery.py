from __future__ import annotations

import argparse
from pathlib import Path


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{label}: expected exactly one anchor, found {count}")
    return text.replace(old, new, 1)


def add_tests(source_root: Path) -> None:
    root = source_root / "src-wpf"
    program_path = root / "FreeCamManager.Tests" / "Program.cs"
    program = program_path.read_text(encoding="utf-8")

    reg_anchor = '        await Run("V3.7 Fix1 background refresh preserves manual conclusion interaction", V371BackgroundRefreshPreservesManualConclusionInteraction);\n'
    registrations = reg_anchor + (
        '        await Run("V3.7 Fix1 corrupted library restores from valid backup", V371CorruptedLibraryRestoresFromBackup);\n'
        '        await Run("V3.7 Fix1 safe save keeps last-known-good backup", V371SafeSaveKeepsBackup);\n'
        '        await Run("V3.7 Fix1 root reconciliation rebuilds managed library without moving files", V371RootReconciliationRebuildsManagedLibrary);\n'
        '        await Run("V3.7 Fix1 root reconciliation preserves existing manual metadata", V371RootReconciliationPreservesManualMetadata);\n'
        '        await Run("V3.7 Fix1 startup and manual refresh wire recovery reconciliation", V371RecoveryWiringContract);\n'
    )
    program = replace_once(program, reg_anchor, registrations, "recovery test registrations")

    methods = r'''
    private static async Task V371CorruptedLibraryRestoresFromBackup()
    {
        var dir = TempDir();
        var file = Path.Combine(dir, "library.json");
        var backup = file + ".bak";
        var artifact = new Artifact
        {
            Path = Path.Combine(dir, "FreeCam_R40.4.0_LookAt_Probe1.zip"),
            Name = "FreeCam_R40.4.0_LookAt_Probe1.zip",
            Category = "Experiment",
            Feature = "LookAt",
            Stage = "Probe1",
            ManualStatus = "通过",
            Rating = 4,
            Notes = "keep me"
        };
        var json = JsonSerializer.Serialize(new { items = new[] { artifact } }, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(backup, json, new UTF8Encoding(false));
        await File.WriteAllBytesAsync(file, Enumerable.Repeat((byte)0, 4096).ToArray());

        var recovered = await LibraryService.LoadAsync(file);
        var item = recovered.Snapshot().Single();
        Assert(item.ManualStatus == "通过" && item.Rating == 4 && item.Notes == "keep me",
            "valid .bak library must recover metadata when primary library is corrupt");
        var restoredBytes = await File.ReadAllBytesAsync(file);
        Assert(restoredBytes.Any(b => b != 0), "backup recovery must restore a valid primary library file");
    }

    private static async Task V371SafeSaveKeepsBackup()
    {
        var dir = TempDir();
        var file = Path.Combine(dir, "library.json");
        var artifactPath = Path.Combine(dir, "FreeCam_R40.4.0_Test.zip");
        var original = new Artifact { Path = artifactPath, Name = Path.GetFileName(artifactPath), ManualStatus = "通过", Rating = 2 };
        var json = JsonSerializer.Serialize(new { items = new[] { original } }, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(file, json, new UTF8Encoding(false));

        var library = await LibraryService.LoadAsync(file);
        library.SetManualStatus(artifactPath, "待复测");
        library.SetRating(artifactPath, 5);
        await library.SaveAsync();

        var backup = file + ".bak";
        Assert(File.Exists(backup), "safe library save must preserve a .bak file");
        var previous = await LibraryService.LoadAsync(backup);
        var oldItem = previous.ByPath(artifactPath);
        Assert(oldItem is not null && oldItem.ManualStatus == "通过" && oldItem.Rating == 2,
            ".bak must contain the last-known-good library before replacement");

        var current = await LibraryService.LoadAsync(file);
        var newItem = current.ByPath(artifactPath);
        Assert(newItem is not null && newItem.ManualStatus == "待复测" && newItem.Rating == 5,
            "safe save must commit the new library after backup creation");
    }

    private static async Task V371RootReconciliationRebuildsManagedLibrary()
    {
        var dir = TempDir();
        var root = Path.Combine(dir, "FreeCam");
        var buildDir = Path.Combine(root, "30_Experiment", "LookAt", "Probe1");
        var resultDir = Path.Combine(root, "40_Result", "LookAt", "Probe1");
        var testingDir = Path.Combine(root, "01_Testing", "FreeCam_R40.4.0_LookAt_Probe1");
        Directory.CreateDirectory(buildDir);
        Directory.CreateDirectory(resultDir);
        Directory.CreateDirectory(testingDir);

        var buildPath = Path.Combine(buildDir, "FreeCam_R40.4.0_LookAt_Probe1.zip");
        var resultPath = Path.Combine(resultDir, "FreeCam_R40.4.0_LookAt_Probe1_Result.zip");
        using (ZipFile.Open(buildPath, ZipArchiveMode.Create)) { }
        using (ZipFile.Open(resultPath, ZipArchiveMode.Create)) { }

        var libraryFile = Path.Combine(dir, "library.json");
        var library = await LibraryService.LoadAsync(libraryFile);
        var type = typeof(LibraryService).Assembly.GetType("FreeCamManager.Core.Services.LibraryRebuildService");
        Assert(type is not null, "LibraryRebuildService must exist");
        var instance = Activator.CreateInstance(type!, library, new ManifestService(), new HashService(), null);
        Assert(instance is not null, "LibraryRebuildService constructor failed");
        var method = type!.GetMethod("ReconcileAsync");
        Assert(method is not null, "LibraryRebuildService.ReconcileAsync is missing");
        var task = method!.Invoke(instance, new object?[] { root, CancellationToken.None }) as Task;
        Assert(task is not null, "ReconcileAsync did not return a Task");
        await task!;

        var build = library.ByPath(buildPath);
        var result = library.ByPath(resultPath);
        Assert(build is not null && build.Category == "Experiment",
            "existing 30_Experiment build must be rebuilt into the library");
        Assert(result is not null && result.Category == "Result" && result.ArtifactType == "Result",
            "existing 40_Result package must be rebuilt as Result");
        Assert(build is not null && string.Equals(build.TestingPath, testingDir, StringComparison.OrdinalIgnoreCase),
            "rebuild must reconnect matching 01_Testing workspace");
        Assert(build is not null && string.Equals(build.ResultPath, resultPath, StringComparison.OrdinalIgnoreCase),
            "rebuild must reconnect matching Result package");
        Assert(File.Exists(buildPath) && File.Exists(resultPath),
            "root reconciliation must index files in place without moving them");
        Assert(File.Exists(libraryFile), "root reconciliation must persist the rebuilt library");
    }

    private static async Task V371RootReconciliationPreservesManualMetadata()
    {
        var dir = TempDir();
        var root = Path.Combine(dir, "FreeCam");
        var buildDir = Path.Combine(root, "20_Feature", "Camera", "Test1");
        Directory.CreateDirectory(buildDir);
        var buildPath = Path.Combine(buildDir, "FreeCam_R40.4.0_Camera_Test1.zip");
        using (ZipFile.Open(buildPath, ZipArchiveMode.Create)) { }

        var libraryFile = Path.Combine(dir, "library.json");
        var seeded = new Artifact
        {
            Path = buildPath,
            RelativePath = Path.GetRelativePath(root, buildPath),
            Name = Path.GetFileName(buildPath),
            Category = "Feature",
            Feature = "Camera",
            Stage = "Test1",
            ManualStatus = "通过",
            Rating = 5,
            Notes = "人工备注",
            Protected = true
        };
        var seedJson = JsonSerializer.Serialize(new { items = new[] { seeded } }, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(libraryFile, seedJson, new UTF8Encoding(false));
        var library = await LibraryService.LoadAsync(libraryFile);

        var type = typeof(LibraryService).Assembly.GetType("FreeCamManager.Core.Services.LibraryRebuildService");
        Assert(type is not null, "LibraryRebuildService must exist");
        var instance = Activator.CreateInstance(type!, library, new ManifestService(), new HashService(), null);
        var task = type!.GetMethod("ReconcileAsync")!.Invoke(instance, new object?[] { root, CancellationToken.None }) as Task;
        Assert(task is not null, "ReconcileAsync did not return a Task");
        await task!;

        var item = library.ByPath(buildPath);
        Assert(item is not null && item.ManualStatus == "通过" && item.Rating == 5
            && item.Notes == "人工备注" && item.Protected,
            "root reconciliation must never erase existing manual conclusion/rating/notes/lock metadata");
    }

    private static Task V371RecoveryWiringContract()
    {
        var sourceRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "FreeCamManager"));
        var app = File.ReadAllText(Path.Combine(sourceRoot, "App.xaml.cs"));
        var watcher = File.ReadAllText(Path.Combine(sourceRoot, "..", "FreeCamManager.Core", "Services", "InboxWatcherService.cs"));
        var library = File.ReadAllText(Path.Combine(sourceRoot, "..", "FreeCamManager.Core", "Services", "LibraryService.cs"));

        Assert(app.Contains("System.Text.Json.JsonException", StringComparison.Ordinal)
            && app.Contains("LibraryService.CreateEmpty(AppPaths.LibraryFile)", StringComparison.Ordinal),
            "startup must survive a corrupt library and create a writable empty index");
        Assert(app.Contains("LibraryRebuildService", StringComparison.Ordinal)
            && app.Contains("ReconcileAsync(settings.RootDir", StringComparison.Ordinal),
            "startup must reconcile already-classified files from the configured FreeCam root");
        Assert(watcher.Contains("manual && _rebuild is not null", StringComparison.Ordinal)
            && watcher.Contains("_rebuild.ReconcileAsync(cfg.RootDir", StringComparison.Ordinal),
            "manual Refresh must reconcile the managed root as well as scan the inbox");
        Assert(library.Contains(".bak", StringComparison.Ordinal)
            && library.Contains("flushToDisk: true", StringComparison.Ordinal)
            && library.Contains("ValidateLibraryFileAsync", StringComparison.Ordinal),
            "library persistence must validate temp JSON, flush to disk, and keep a backup");
        return Task.CompletedTask;
    }

'''
    program = replace_once(program, "    private static string TempDir()\n", methods + "    private static string TempDir()\n", "TempDir method")
    program_path.write_text(program, encoding="utf-8")

    validator = r'''from pathlib import Path
import sys

root = Path(__file__).resolve().parent
app = root / "FreeCamManager"
core = root / "FreeCamManager.Core"
errors = []

def need(cond, msg):
    if not cond:
        errors.append(msg)

appsrc = (app / "App.xaml.cs").read_text(encoding="utf-8")
library = (core / "Services" / "LibraryService.cs").read_text(encoding="utf-8")
watcher = (core / "Services" / "InboxWatcherService.cs").read_text(encoding="utf-8")
rebuild_path = core / "Services" / "LibraryRebuildService.cs"
rebuild = rebuild_path.read_text(encoding="utf-8") if rebuild_path.exists() else ""
tests = (root / "FreeCamManager.Tests" / "Program.cs").read_text(encoding="utf-8")

need('file + ".bak"' in library or '_file + ".bak"' in library,
     "LibraryService must keep a .bak last-known-good copy")
need("flushToDisk: true" in library and "ValidateLibraryFileAsync" in library,
     "LibraryService safe-save validation/durable flush contract is missing")
need("CreateEmpty(string file)" in library,
     "LibraryService must support a writable empty library after corruption")
need(rebuild_path.exists() and "public sealed class LibraryRebuildService" in rebuild,
     "LibraryRebuildService is missing")
need('new[] { "10_Stable", "20_Feature", "30_Experiment", "40_Result", "80_Archive", "90_Unknown" }' in rebuild,
     "rebuild service must scan all managed classification roots")
need("System.Text.Json.JsonException" in appsrc and "LibraryService.CreateEmpty(AppPaths.LibraryFile)" in appsrc,
     "startup corrupt-library recovery contract is missing")
need("ReconcileAsync(settings.RootDir" in appsrc,
     "startup root reconciliation is missing")
need("manual && _rebuild is not null" in watcher and "_rebuild.ReconcileAsync(cfg.RootDir" in watcher,
     "manual refresh root reconciliation is missing")
for name in [
    "V3.7 Fix1 corrupted library restores from valid backup",
    "V3.7 Fix1 safe save keeps last-known-good backup",
    "V3.7 Fix1 root reconciliation rebuilds managed library without moving files",
    "V3.7 Fix1 root reconciliation preserves existing manual metadata",
    "V3.7 Fix1 startup and manual refresh wire recovery reconciliation",
]:
    need(name in tests, f"missing regression test registration: {name}")

if errors:
    print("FAIL: V3.7 Fix1 recovery contract")
    for e in errors:
        print(" -", e)
    sys.exit(1)
print("PASS: V3.7 Fix1 recovery contract")
'''
    (root / "validate-v371-recovery.py").write_text(validator, encoding="utf-8")


def apply_production(source_root: Path) -> None:
    root = source_root / "src-wpf"

    library_path = root / "FreeCamManager.Core" / "Services" / "LibraryService.cs"
    library = library_path.read_text(encoding="utf-8")

    create_anchor = '''    public static LibraryService CreateInMemory(IEnumerable<Artifact>? items = null)
        => new("", items?.Select(x => x.Clone()).ToList() ?? []);

'''
    library = replace_once(
        library,
        create_anchor,
        create_anchor + '''    public static LibraryService CreateEmpty(string file)
        => new(file, []);

''',
        "writable empty library factory")

    old_load = '''    public static async Task<LibraryService> LoadAsync(string file, CancellationToken ct = default)
    {
        if (!File.Exists(file)) return new LibraryService(file, []);
        await using var stream = File.OpenRead(file);
        var doc = await JsonSerializer.DeserializeAsync<LibraryDocument>(stream, JsonOptions, ct) ?? new LibraryDocument();
        foreach (var item in doc.Items) Migrate(item);
        return new LibraryService(file, doc.Items ?? []);
    }
'''
    new_load = '''    public static async Task<LibraryService> LoadAsync(string file, CancellationToken ct = default)
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
'''
    library = replace_once(library, old_load, new_load, "backup-aware library load")

    old_save = '''    public async Task SaveAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_file)) return;
        await _saveGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            List<Artifact> snapshot;
            lock (_gate) snapshot = _items.Select(x => x.Clone()).ToList();
            var parent = Path.GetDirectoryName(_file);
            if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
            var tmp = _file + ".tmp";
            await using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, true))
            {
                await JsonSerializer.SerializeAsync(stream, new LibraryDocument { Items = snapshot }, JsonOptions, ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
            }
            File.Move(tmp, _file, true);
        }
        finally
        {
            _saveGate.Release();
        }
    }
'''
    new_save = '''    public async Task SaveAsync(CancellationToken ct = default)
    {
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
'''
    library = replace_once(library, old_save, new_save, "safe library save")
    library_path.write_text(library, encoding="utf-8")

    rebuild_path = root / "FreeCamManager.Core" / "Services" / "LibraryRebuildService.cs"
    rebuild_path.write_text(r'''using FreeCamManager.Core.Models;

namespace FreeCamManager.Core.Services;

public sealed record LibraryRebuildResult(int Added, int TestingLinked, int ResultsPaired, int Failed)
{
    public bool Changed => Added > 0 || TestingLinked > 0 || ResultsPaired > 0;
}

public sealed class LibraryRebuildService(
    LibraryService library,
    ManifestService manifest,
    HashService hash,
    IAppLogger? log = null)
{
    private static readonly string[] ManagedRoots =
        new[] { "10_Stable", "20_Feature", "30_Experiment", "40_Result", "80_Archive", "90_Unknown" };

    public async Task<LibraryRebuildResult> ReconcileAsync(string root, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return new LibraryRebuildResult(0, 0, 0, 0);

        var added = 0;
        var linked = 0;
        var failed = 0;

        foreach (var managedRoot in ManagedRoots)
        {
            var directory = Path.Combine(root, managedRoot);
            if (!Directory.Exists(directory)) continue;

            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).ToList(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed++;
                log?.Event("LIBRARY_REBUILD_ENUM_ERROR", ("directory", directory), ("error", ex.Message));
                continue;
            }

            foreach (var path in files)
            {
                ct.ThrowIfCancellationRequested();
                if (!ShouldIndex(path, managedRoot)) continue;
                if (library.ByPath(path) is not null) continue;

                try
                {
                    Artifact artifact;
                    try { artifact = await manifest.InspectAsync(path, ct).ConfigureAwait(false); }
                    catch
                    {
                        artifact = manifest.InspectFilename(Path.GetFileName(path));
                        artifact.Path = path;
                        artifact.Name = Path.GetFileName(path);
                    }

                    var info = new FileInfo(path);
                    artifact.Path = path;
                    artifact.Name = Path.GetFileName(path);
                    artifact.RelativePath = PathRebaseService.TryMakeRelative(root, path);
                    artifact.Category = CategoryFromPath(managedRoot, artifact.RelativePath);
                    artifact.Size = info.Exists ? info.Length : 0;
                    artifact.ImportedAt = info.Exists
                        ? new DateTimeOffset(info.LastWriteTimeUtc).ToString("O")
                        : DateTimeOffset.Now.ToString("O");

                    try { artifact.Sha256 = await hash.FileSha256Async(path, ct).ConfigureAwait(false); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        log?.Event("LIBRARY_REBUILD_HASH_SKIPPED", ("path", path), ("error", ex.Message));
                    }

                    ApplyCategoryDefaults(artifact);
                    library.Upsert(artifact);
                    added++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failed++;
                    log?.Event("LIBRARY_REBUILD_FILE_ERROR", ("path", path), ("error", ex.Message));
                }
            }
        }

        foreach (var build in library.Snapshot().Where(x => x.Category is "Feature" or "Experiment"))
        {
            ct.ThrowIfCancellationRequested();
            if (!string.IsNullOrWhiteSpace(build.TestingPath) && Directory.Exists(build.TestingPath)) continue;
            var testing = Path.Combine(root, "01_Testing", Path.GetFileNameWithoutExtension(build.Path));
            if (!Directory.Exists(testing)) continue;
            if (library.SetTestingPath(build.Path, testing, root)) linked++;
        }

        var beforePairs = library.Snapshot()
            .Where(x => x.ArtifactType != "Result")
            .ToDictionary(x => x.Path, x => x.ResultPath, StringComparer.OrdinalIgnoreCase);
        library.PairResults();
        var paired = library.Snapshot()
            .Where(x => x.ArtifactType != "Result")
            .Count(x => beforePairs.TryGetValue(x.Path, out var before)
                && !string.Equals(before, x.ResultPath, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(x.ResultPath));

        var result = new LibraryRebuildResult(added, linked, paired, failed);
        if (result.Changed) await library.SaveAsync(ct).ConfigureAwait(false);
        log?.Event("LIBRARY_REBUILD",
            ("root", root), ("added", added), ("testing_linked", linked), ("results_paired", paired), ("failed", failed));
        return result;
    }

    private static bool ShouldIndex(string path, string managedRoot)
    {
        var extension = Path.GetExtension(path);
        if (extension.Equals(".zip", StringComparison.OrdinalIgnoreCase)) return true;
        if (extension.Equals(".bundle", StringComparison.OrdinalIgnoreCase)) return true;
        return managedRoot == "10_Stable"
            && extension.Equals(".txt", StringComparison.OrdinalIgnoreCase)
            && Path.GetFileName(path).Contains("SHA256", StringComparison.OrdinalIgnoreCase);
    }

    private static string CategoryFromPath(string managedRoot, string relativePath)
    {
        return managedRoot switch
        {
            "10_Stable" => "Stable",
            "20_Feature" => "Feature",
            "30_Experiment" => "Experiment",
            "40_Result" => "Result",
            "80_Archive" when relativePath.Contains(
                Path.Combine("80_Archive", "Duplicates"), StringComparison.OrdinalIgnoreCase) => "Duplicate",
            "80_Archive" => "Archive",
            "90_Unknown" when relativePath.Contains(
                Path.Combine("90_Unknown", "Stable_Candidate"), StringComparison.OrdinalIgnoreCase) => "StableCandidate",
            _ => "Unknown"
        };
    }

    private static void ApplyCategoryDefaults(Artifact artifact)
    {
        switch (artifact.Category)
        {
            case "Stable":
                artifact.BuildType = "Stable";
                artifact.ReleaseState = "Stable";
                artifact.Status = "已冻结";
                artifact.Protected = true;
                if (artifact.Name.Contains("SHA256", StringComparison.OrdinalIgnoreCase))
                    artifact.ArtifactType = "SHA256";
                break;
            case "Feature":
            case "Experiment":
                if (string.IsNullOrWhiteSpace(artifact.TestStatus)) artifact.TestStatus = "待测试";
                break;
            case "Result":
                artifact.ArtifactType = "Result";
                break;
            case "StableCandidate":
                artifact.Status = "待确认 Stable（稳定版）";
                break;
            case "Duplicate":
                artifact.Status = "重复文件";
                break;
        }
    }
}
''', encoding="utf-8")

    app_path = root / "FreeCamManager" / "App.xaml.cs"
    app = app_path.read_text(encoding="utf-8")
    old_library_load = '''            stage = $"读取版本库：{AppPaths.LibraryFile}";
            try
            {
                _library = await LibraryService.LoadAsync(AppPaths.LibraryFile);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                _library = LibraryService.CreateInMemory();
                startupWarnings.Add($"无法读取版本库，已进入只读空库模式：{ex.Message}");
                _log?.Event("LIBRARY_FALLBACK_MEMORY", ("file", AppPaths.LibraryFile), ("error", ex.Message));
            }

'''
    new_library_load = '''            stage = $"读取版本库：{AppPaths.LibraryFile}";
            try
            {
                _library = await LibraryService.LoadAsync(AppPaths.LibraryFile);
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidDataException)
            {
                var quarantined = "";
                try
                {
                    if (File.Exists(AppPaths.LibraryFile))
                    {
                        quarantined = AppPaths.LibraryFile + ".corrupt-" + DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
                        File.Move(AppPaths.LibraryFile, quarantined, true);
                    }
                }
                catch (Exception moveEx) when (moveEx is IOException or UnauthorizedAccessException)
                {
                    _log?.Event("LIBRARY_CORRUPT_QUARANTINE_FAILED", ("file", AppPaths.LibraryFile), ("error", moveEx.Message));
                }

                _library = LibraryService.CreateEmpty(AppPaths.LibraryFile);
                startupWarnings.Add(string.IsNullOrWhiteSpace(quarantined)
                    ? $"版本库损坏，将从 FreeCam 根目录重建索引：{ex.Message}"
                    : $"版本库损坏，已保留故障副本并将从 FreeCam 根目录重建索引：{quarantined}");
                _log?.Event("LIBRARY_CORRUPT_RECOVERY", ("file", AppPaths.LibraryFile), ("quarantined", quarantined), ("error", ex.Message));
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                _library = LibraryService.CreateInMemory();
                startupWarnings.Add($"无法读取版本库，已进入只读空库模式：{ex.Message}");
                _log?.Event("LIBRARY_FALLBACK_MEMORY", ("file", AppPaths.LibraryFile), ("error", ex.Message));
            }

'''
    app = replace_once(app, old_library_load, new_library_load, "corrupt library startup recovery")

    service_anchor = '''            var testRefresh = new TestStatusRefreshService(_library, result, () => settings.RootDir);
            var organizer = new OrganizerService(settings.RootDir, settings.StableBackupDir, _library, manifest, classification, hash);
            var discardCleanup = new DiscardCleanupService(_library, organizer, result, () => settings.RootDir, () => SettingsService.ResultRoot(settings));
'''
    service_new = '''            var testRefresh = new TestStatusRefreshService(_library, result, () => settings.RootDir);
            var organizer = new OrganizerService(settings.RootDir, settings.StableBackupDir, _library, manifest, classification, hash);
            var libraryRebuild = new LibraryRebuildService(_library, manifest, hash, _log);
            var discardCleanup = new DiscardCleanupService(_library, organizer, result, () => settings.RootDir, () => SettingsService.ResultRoot(settings));
'''
    app = replace_once(app, service_anchor, service_new, "library rebuild service creation")

    stage_anchor = '''            stage = "修复稳定版候选目录";
'''
    stage_new = '''            stage = "校验并重建版本库索引";
            try
            {
                var rebuilt = await libraryRebuild.ReconcileAsync(settings.RootDir);
                if (rebuilt.Added > 0 || rebuilt.TestingLinked > 0 || rebuilt.ResultsPaired > 0)
                    startupWarnings.Add($"已从现有 FreeCam 目录恢复索引：新增 {rebuilt.Added} 项，测试目录关联 {rebuilt.TestingLinked} 项，Result 关联 {rebuilt.ResultsPaired} 项。");
                if (rebuilt.Failed > 0)
                    startupWarnings.Add($"版本库重建有 {rebuilt.Failed} 个文件暂时无法识别，可稍后点击刷新重试。");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                startupWarnings.Add($"现有 FreeCam 目录索引重建暂时无法完成：{ex.Message}");
                _log?.Event("LIBRARY_REBUILD_STARTUP_ERROR", ("error", ex.Message));
            }

            stage = "修复稳定版候选目录";
'''
    app = replace_once(app, stage_anchor, stage_new, "startup library reconciliation")

    watcher_old = '''            var watcher = new InboxWatcherService(() => settings, _library, organizer, manifest, classification, extraction, testRefresh, _log, discardCleanup);
'''
    watcher_new = '''            var watcher = new InboxWatcherService(() => settings, _library, organizer, manifest, classification, extraction, testRefresh, _log, discardCleanup, libraryRebuild);
'''
    app = replace_once(app, watcher_old, watcher_new, "watcher recovery wiring")
    app_path.write_text(app, encoding="utf-8")

    watcher_path = root / "FreeCamManager.Core" / "Services" / "InboxWatcherService.cs"
    watcher = watcher_path.read_text(encoding="utf-8")
    watcher = replace_once(
        watcher,
        '''    private readonly IAppLogger? _log;
    private readonly DiscardCleanupService? _discardCleanup;
''',
        '''    private readonly IAppLogger? _log;
    private readonly DiscardCleanupService? _discardCleanup;
    private readonly LibraryRebuildService? _rebuild;
''',
        "watcher rebuild field")
    watcher = replace_once(
        watcher,
        '''        TestStatusRefreshService testRefresh,
        IAppLogger? log = null,
        DiscardCleanupService? discardCleanup = null)
''',
        '''        TestStatusRefreshService testRefresh,
        IAppLogger? log = null,
        DiscardCleanupService? discardCleanup = null,
        LibraryRebuildService? rebuild = null)
''',
        "watcher rebuild constructor")
    watcher = replace_once(
        watcher,
        '''        _testRefresh = testRefresh;
        _log = log;
        _discardCleanup = discardCleanup;
    }
''',
        '''        _testRefresh = testRefresh;
        _log = log;
        _discardCleanup = discardCleanup;
        _rebuild = rebuild;
    }
''',
        "watcher rebuild assignment")
    watcher = replace_once(
        watcher,
        '''            var cfg = _settings();
            var changed = false;
''',
        '''            var cfg = _settings();
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
''',
        "manual root reconciliation")
    watcher_path.write_text(watcher, encoding="utf-8")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("command", choices=["add-tests", "apply-production"])
    parser.add_argument("--source-root", required=True, type=Path)
    args = parser.parse_args()
    if args.command == "add-tests":
        add_tests(args.source_root)
    else:
        apply_production(args.source_root)


if __name__ == "__main__":
    main()
