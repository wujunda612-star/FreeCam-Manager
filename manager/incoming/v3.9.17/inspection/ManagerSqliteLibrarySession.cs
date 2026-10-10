using System.Text;
using System.Text.Json;
using FreeCamManager.Core.Models;

namespace FreeCamManager.SQLiteMigration.Core;

public sealed class ManagerSqliteLibrarySession
{
    private static readonly JsonSerializerOptions ExportJsonOptions = new() { WriteIndented = true };
    private readonly MigrationPaths _paths;
    private readonly MigrationSafetyBoundary _safety;
    private readonly SqliteMigrationStore _store;
    private readonly SemaphoreSlim _saveGate = new(1, 1);

    private ManagerSqliteLibrarySession(MigrationPaths paths, MigrationSafetyBoundary safety, SqliteMigrationStore store, IReadOnlyList<Artifact> items, string recoverySource)
    {
        _paths = paths;
        _safety = safety;
        _store = store;
        Items = items.Select(x => x.Clone()).ToList();
        RecoverySource = recoverySource;
    }

    public IReadOnlyList<Artifact> Items { get; }
    public string RecoverySource { get; }
    public string DatabaseFile => _paths.DatabaseFile;
    public string ExportFile => Path.Combine(_paths.ExportsDirectory, "library-latest.json");

    public static async Task<ManagerSqliteLibrarySession> OpenAsync(MigrationPaths paths, string productionLibraryFile, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var safety = new MigrationSafetyBoundary(paths);
        safety.EnsureExperimentWriteAllowed(paths.ExperimentDataDirectory);
        Directory.CreateDirectory(paths.ExperimentDataDirectory);
        Directory.CreateDirectory(paths.BackupsDirectory);
        Directory.CreateDirectory(paths.ExportsDirectory);
        Directory.CreateDirectory(paths.CorruptDirectory);
        var store = new SqliteMigrationStore(paths.DatabaseFile, safety);
        var mainWasCorrupt = false;

        if (File.Exists(paths.DatabaseFile))
        {
            var integrity = await store.CheckIntegrityAsync(false, ct).ConfigureAwait(false);
            if (integrity.IsOk)
            {
                var loaded = await store.LoadSnapshotAsync(ct).ConfigureAwait(false);
                return new ManagerSqliteLibrarySession(paths, safety, store, loaded, "MainDatabase");
            }

            mainWasCorrupt = true;
            var backup = await TryRestoreNewestBackupAsync(paths, safety, ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(backup))
            {
                var loaded = await store.LoadSnapshotAsync(ct).ConfigureAwait(false);
                return new ManagerSqliteLibrarySession(paths, safety, store, loaded, "SqliteBackup");
            }

            QuarantineCorruptMain(paths, safety);
        }

        var export = Path.Combine(paths.ExportsDirectory, "library-latest.json");
        if (File.Exists(export))
        {
            try
            {
                var source = await new ReadOnlyLibraryJsonReader().ReadAsync(export, ct).ConfigureAwait(false);
                await new LibraryMigrationCoordinator(paths, safety).MigrateAsync(source, MigrationFault.None, ct).ConfigureAwait(false);
                var loaded = await store.LoadSnapshotAsync(ct).ConfigureAwait(false);
                return new ManagerSqliteLibrarySession(paths, safety, store, loaded, "JsonExport");
            }
            catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException or IOException or UnauthorizedAccessException)
            {
                // Fall through to the production JSON import. Production files remain read-only.
            }
        }

        try
        {
            var source = await new ReadOnlyLibraryJsonReader().ReadAsync(productionLibraryFile, ct).ConfigureAwait(false);
            await new LibraryMigrationCoordinator(paths, safety).MigrateAsync(source, MigrationFault.None, ct).ConfigureAwait(false);
            var loaded = await store.LoadSnapshotAsync(ct).ConfigureAwait(false);
            return new ManagerSqliteLibrarySession(paths, safety, store, loaded, source.UsedBackup ? "ProductionJsonBackup" : "ProductionJson");
        }
        catch (Exception ex) when (ex is InvalidDataException or FileNotFoundException or System.Text.Json.JsonException)
        {
            await store.SaveSnapshotAsync([], new Dictionary<string, string>
            {
                ["manager_storage"] = "sqlite-experiment-test2",
                ["created_empty_utc"] = DateTimeOffset.UtcNow.ToString("O"),
                ["corrupt_main_quarantined"] = mainWasCorrupt ? "true" : "false"
            }, ct).ConfigureAwait(false);
            return new ManagerSqliteLibrarySession(paths, safety, store, [], "NewDatabase");
        }
    }

    public async Task SaveAsync(IReadOnlyList<Artifact> items, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        await _saveGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (File.Exists(_paths.DatabaseFile))
            {
                var integrity = await _store.CheckIntegrityAsync(false, ct).ConfigureAwait(false);
                if (integrity.IsOk)
                    await CreateRollingBackupAsync(ct).ConfigureAwait(false);
            }

            await _store.SaveSnapshotAsync(items, new Dictionary<string, string>
            {
                ["manager_storage"] = "sqlite-experiment-test2",
                ["saved_at_utc"] = DateTimeOffset.UtcNow.ToString("O"),
                ["item_count"] = items.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["snapshot_fingerprint"] = ArtifactSnapshotFingerprint.Compute(items)
            }, ct).ConfigureAwait(false);

            var check = await _store.CheckIntegrityAsync(false, ct).ConfigureAwait(false);
            if (!check.IsOk) throw new InvalidDataException($"SQLite quick_check failed after save: {check.Message}");
            await WriteJsonExportAsync(items, ct).ConfigureAwait(false);
        }
        finally
        {
            _saveGate.Release();
        }
    }

    public Task<IReadOnlyList<Artifact>> ReloadAsync(CancellationToken ct = default) => _store.LoadSnapshotAsync(ct);

    private async Task CreateRollingBackupAsync(CancellationToken ct)
    {
        _safety.EnsureExperimentWriteAllowed(_paths.BackupsDirectory);
        Directory.CreateDirectory(_paths.BackupsDirectory);
        var backup = Path.Combine(_paths.BackupsDirectory, $"library-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss.fff}-{Guid.NewGuid():N}.db");
        await SqliteMigrationStore.CreateOnlineBackupAsync(_paths.DatabaseFile, backup, _safety, ct).ConfigureAwait(false);
        var backupStore = new SqliteMigrationStore(backup, _safety);
        var integrity = await backupStore.CheckIntegrityAsync(false, ct).ConfigureAwait(false);
        if (!integrity.IsOk)
        {
            try { File.Delete(backup); } catch { }
            throw new InvalidDataException($"SQLite rolling backup failed quick_check: {integrity.Message}");
        }

        foreach (var stale in Directory.EnumerateFiles(_paths.BackupsDirectory, "library-*.db")
                     .OrderByDescending(File.GetLastWriteTimeUtc)
                     .Skip(10))
        {
            _safety.EnsureExperimentWriteAllowed(stale);
            try { File.Delete(stale); } catch { }
        }
    }

    private async Task WriteJsonExportAsync(IReadOnlyList<Artifact> items, CancellationToken ct)
    {
        var export = ExportFile;
        var tmp = export + ".tmp";
        _safety.EnsureExperimentWriteAllowed(export);
        _safety.EnsureExperimentWriteAllowed(tmp);
        Directory.CreateDirectory(_paths.ExportsDirectory);
        try
        {
            await using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, true))
            {
                await JsonSerializer.SerializeAsync(stream, new { items }, ExportJsonOptions, ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            _ = await new ReadOnlyLibraryJsonReader().ReadAsync(tmp, ct).ConfigureAwait(false);
            File.Move(tmp, export, true);
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }
    }

    private static async Task<string> TryRestoreNewestBackupAsync(MigrationPaths paths, MigrationSafetyBoundary safety, CancellationToken ct)
    {
        if (!Directory.Exists(paths.BackupsDirectory)) return "";
        foreach (var backup in Directory.EnumerateFiles(paths.BackupsDirectory, "library-*.db")
                     .OrderByDescending(File.GetLastWriteTimeUtc))
        {
            var backupStore = new SqliteMigrationStore(backup, safety);
            var integrity = await backupStore.CheckIntegrityAsync(false, ct).ConfigureAwait(false);
            if (!integrity.IsOk) continue;

            var restoreTmp = paths.DatabaseFile + ".restore.tmp";
            safety.EnsureExperimentWriteAllowed(restoreTmp);
            try
            {
                if (File.Exists(restoreTmp)) File.Delete(restoreTmp);
                await SqliteMigrationStore.CreateOnlineBackupAsync(backup, restoreTmp, safety, ct).ConfigureAwait(false);
                DeleteSidecars(paths.DatabaseFile, safety);
                File.Move(restoreTmp, paths.DatabaseFile, true);
                var restored = new SqliteMigrationStore(paths.DatabaseFile, safety);
                var restoredCheck = await restored.CheckIntegrityAsync(false, ct).ConfigureAwait(false);
                if (!restoredCheck.IsOk) throw new InvalidDataException($"Restored SQLite backup failed quick_check: {restoredCheck.Message}");
                return backup;
            }
            finally
            {
                try { if (File.Exists(restoreTmp)) File.Delete(restoreTmp); } catch { }
            }
        }
        return "";
    }

    private static void QuarantineCorruptMain(MigrationPaths paths, MigrationSafetyBoundary safety)
    {
        if (!File.Exists(paths.DatabaseFile)) return;
        safety.EnsureExperimentWriteAllowed(paths.CorruptDirectory);
        Directory.CreateDirectory(paths.CorruptDirectory);
        var destination = Path.Combine(paths.CorruptDirectory, $"library.corrupt-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss.fff}-{Guid.NewGuid():N}.db");
        safety.EnsureExperimentWriteAllowed(destination);
        DeleteSidecars(paths.DatabaseFile, safety);
        File.Move(paths.DatabaseFile, destination, true);
    }

    private static void DeleteSidecars(string databaseFile, MigrationSafetyBoundary safety)
    {
        foreach (var file in new[] { databaseFile + "-wal", databaseFile + "-shm", databaseFile + "-journal" })
        {
            if (!File.Exists(file)) continue;
            safety.EnsureExperimentWriteAllowed(file);
            File.Delete(file);
        }
    }
}
