using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FreeCamManager.Core.Models;

namespace FreeCamManager.SQLiteMigration.Core;

public sealed class ProductionManagerSqliteLibrarySession
{
    private static readonly JsonSerializerOptions ExportJsonOptions = new() { WriteIndented = true };
    private readonly ProductionStoragePaths _paths;
    private readonly ProductionDataSafetyBoundary _safety;
    private readonly SqliteMigrationStore _store;
    private readonly SemaphoreSlim _saveGate = new(1, 1);

    private ProductionManagerSqliteLibrarySession(
        ProductionStoragePaths paths,
        ProductionDataSafetyBoundary safety,
        SqliteMigrationStore store,
        IReadOnlyList<Artifact> items,
        string recoverySource,
        string migrationBackupDirectory)
    {
        _paths = paths;
        _safety = safety;
        _store = store;
        Items = items.Select(x => x.Clone()).ToList();
        RecoverySource = recoverySource;
        MigrationBackupDirectory = migrationBackupDirectory;
    }

    public IReadOnlyList<Artifact> Items { get; }
    public string RecoverySource { get; }
    public string MigrationBackupDirectory { get; }
    public string DatabaseFile => _paths.DatabaseFile;
    public string RecoveryExportFile => _paths.RecoveryExportFile;

    public static async Task<ProductionManagerSqliteLibrarySession> OpenAsync(ProductionStoragePaths paths, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var safety = new ProductionDataSafetyBoundary(paths);
        safety.EnsureWriteAllowed(paths.DataDirectory);
        Directory.CreateDirectory(paths.DataDirectory);
        var store = new SqliteMigrationStore(paths.DatabaseFile, safety);

        if (File.Exists(paths.DatabaseFile))
        {
            var integrity = await store.CheckIntegrityAsync(false, ct).ConfigureAwait(false);
            if (integrity.IsOk)
            {
                var loaded = await store.LoadSnapshotAsync(ct).ConfigureAwait(false);
                return new(paths, safety, store, loaded, "MainDatabase", "");
            }

            var restoredBackup = await TryRestoreNewestBackupAsync(paths, safety, ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(restoredBackup))
            {
                var loaded = await store.LoadSnapshotAsync(ct).ConfigureAwait(false);
                return new(paths, safety, store, loaded, "SqliteBackup", "");
            }

            QuarantineCorruptMain(paths, safety);
        }

        if (File.Exists(paths.RecoveryExportFile))
        {
            try
            {
                var source = await new ReadOnlyLibraryJsonReader().ReadAsync(paths.RecoveryExportFile, ct).ConfigureAwait(false);
                await SaveNewDatabaseVerifiedAsync(paths, safety, store, source.Items, "JsonRecovery", source.SnapshotFingerprint, ct).ConfigureAwait(false);
                var loaded = await store.LoadSnapshotAsync(ct).ConfigureAwait(false);
                return new(paths, safety, store, loaded, "JsonRecovery", "");
            }
            catch (Exception ex) when (ex is InvalidDataException or JsonException or IOException or UnauthorizedAccessException)
            {
                // Continue to the immutable legacy JSON source.
            }
        }

        try
        {
            var source = await new ReadOnlyLibraryJsonReader().ReadAsync(paths.LegacyLibraryFile, ct).ConfigureAwait(false);
            var migrationBackup = await CreatePreMigrationBackupAsync(paths, safety, ct).ConfigureAwait(false);
            await SaveNewDatabaseVerifiedAsync(paths, safety, store, source.Items,
                source.UsedBackup ? "LegacyJsonBackup" : "LegacyJson", source.SnapshotFingerprint, ct).ConfigureAwait(false);
            await WriteRecoveryExportAsync(paths, safety, source.Items, ct).ConfigureAwait(false);
            var loaded = await store.LoadSnapshotAsync(ct).ConfigureAwait(false);
            return new(paths, safety, store, loaded, source.UsedBackup ? "LegacyJsonBackup" : "LegacyJson", migrationBackup);
        }
        catch (Exception ex) when (ex is InvalidDataException or FileNotFoundException or JsonException)
        {
            await SaveNewDatabaseVerifiedAsync(paths, safety, store, [], "NewDatabase", ArtifactSnapshotFingerprint.Compute([]), ct).ConfigureAwait(false);
            await WriteRecoveryExportAsync(paths, safety, [], ct).ConfigureAwait(false);
            return new(paths, safety, store, [], "NewDatabase", "");
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
                if (!integrity.IsOk) throw new InvalidDataException($"SQLite quick_check failed before save: {integrity.Message}");
                await CreateRollingBackupAsync(ct).ConfigureAwait(false);
            }

            var fingerprint = ArtifactSnapshotFingerprint.Compute(items);
            await _store.SaveSnapshotAsync(items, new Dictionary<string, string>
            {
                ["manager_storage"] = "sqlite-v38-rc1",
                ["saved_at_utc"] = DateTimeOffset.UtcNow.ToString("O"),
                ["item_count"] = items.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["snapshot_fingerprint"] = fingerprint
            }, ct).ConfigureAwait(false);

            var check = await _store.CheckIntegrityAsync(false, ct).ConfigureAwait(false);
            if (!check.IsOk) throw new InvalidDataException($"SQLite quick_check failed after save: {check.Message}");
            var loaded = await _store.LoadSnapshotAsync(ct).ConfigureAwait(false);
            var loadedFingerprint = ArtifactSnapshotFingerprint.Compute(loaded);
            if (!string.Equals(fingerprint, loadedFingerprint, StringComparison.Ordinal))
                throw new InvalidDataException($"SQLite snapshot verification failed after save. expected={fingerprint}, actual={loadedFingerprint}");

            await WriteRecoveryExportAsync(_paths, _safety, items, ct).ConfigureAwait(false);
        }
        finally
        {
            _saveGate.Release();
        }
    }

    public Task<IReadOnlyList<Artifact>> ReloadAsync(CancellationToken ct = default) => _store.LoadSnapshotAsync(ct);
    public Task<SqliteIntegrityResult> CheckIntegrityAsync(bool full = false, CancellationToken ct = default) => _store.CheckIntegrityAsync(full, ct);

    private static async Task SaveNewDatabaseVerifiedAsync(
        ProductionStoragePaths paths,
        ProductionDataSafetyBoundary safety,
        SqliteMigrationStore store,
        IReadOnlyList<Artifact> items,
        string source,
        string expectedFingerprint,
        CancellationToken ct)
    {
        safety.EnsureWriteAllowed(paths.DatabaseFile);
        await store.SaveSnapshotAsync(items, new Dictionary<string, string>
        {
            ["manager_storage"] = "sqlite-v38-rc1",
            ["created_at_utc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["recovery_source"] = source,
            ["snapshot_fingerprint"] = expectedFingerprint
        }, ct).ConfigureAwait(false);
        var check = await store.CheckIntegrityAsync(false, ct).ConfigureAwait(false);
        if (!check.IsOk) throw new InvalidDataException($"SQLite quick_check failed after creation: {check.Message}");
        var loaded = await store.LoadSnapshotAsync(ct).ConfigureAwait(false);
        var actual = ArtifactSnapshotFingerprint.Compute(loaded);
        if (!string.Equals(expectedFingerprint, actual, StringComparison.Ordinal))
            throw new InvalidDataException($"SQLite migration snapshot mismatch. expected={expectedFingerprint}, actual={actual}");
    }

    private async Task CreateRollingBackupAsync(CancellationToken ct)
    {
        _safety.EnsureWriteAllowed(_paths.SqliteBackupsDirectory);
        Directory.CreateDirectory(_paths.SqliteBackupsDirectory);
        var destination = Path.Combine(_paths.SqliteBackupsDirectory, $"library-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss.fff}-{Guid.NewGuid():N}.db");
        await SqliteMigrationStore.CreateOnlineBackupAsync(_paths.DatabaseFile, destination, _safety, ct).ConfigureAwait(false);
        var backupStore = new SqliteMigrationStore(destination, _safety);
        var check = await backupStore.CheckIntegrityAsync(false, ct).ConfigureAwait(false);
        if (!check.IsOk)
        {
            TryDelete(destination);
            throw new InvalidDataException($"SQLite rolling backup failed quick_check: {check.Message}");
        }

        foreach (var stale in Directory.EnumerateFiles(_paths.SqliteBackupsDirectory, "library-*.db")
                     .OrderByDescending(File.GetLastWriteTimeUtc)
                     .Skip(10))
        {
            _safety.EnsureWriteAllowed(stale);
            TryDelete(stale);
        }
    }

    private static async Task<string> CreatePreMigrationBackupAsync(ProductionStoragePaths paths, ProductionDataSafetyBoundary safety, CancellationToken ct)
    {
        safety.EnsureWriteAllowed(paths.MigrationBackupDirectory);
        Directory.CreateDirectory(paths.MigrationBackupDirectory);
        var destination = Path.Combine(paths.MigrationBackupDirectory, $"pre-v38-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss.fff}-{Guid.NewGuid():N}");
        safety.EnsureWriteAllowed(destination);
        Directory.CreateDirectory(destination);

        var manifest = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["created_at_utc"] = DateTimeOffset.UtcNow.ToString("O")
        };
        foreach (var source in new[] { paths.LegacyLibraryFile, paths.LegacyBackupFile })
        {
            if (!File.Exists(source)) continue;
            var target = Path.Combine(destination, Path.GetFileName(source));
            safety.EnsureWriteAllowed(target);
            File.Copy(source, target, false);
            var sourceHash = await Sha256Async(source, ct).ConfigureAwait(false);
            var targetHash = await Sha256Async(target, ct).ConfigureAwait(false);
            if (!string.Equals(sourceHash, targetHash, StringComparison.Ordinal))
                throw new InvalidDataException($"Pre-migration backup hash mismatch: {source}");
            manifest[Path.GetFileName(source)] = new { sha256 = sourceHash, size = new FileInfo(source).Length };
        }

        var manifestFile = Path.Combine(destination, "manifest.json");
        safety.EnsureWriteAllowed(manifestFile);
        await File.WriteAllTextAsync(manifestFile, JsonSerializer.Serialize(manifest, ExportJsonOptions), new UTF8Encoding(false), ct).ConfigureAwait(false);
        return destination;
    }

    internal static async Task WriteRecoveryExportAsync(ProductionStoragePaths paths, ProductionDataSafetyBoundary safety, IReadOnlyList<Artifact> items, CancellationToken ct)
    {
        safety.EnsureWriteAllowed(paths.RecoveryDirectory);
        Directory.CreateDirectory(paths.RecoveryDirectory);
        var tmp = paths.RecoveryExportFile + ".tmp";
        safety.EnsureWriteAllowed(tmp);
        safety.EnsureWriteAllowed(paths.RecoveryExportFile);
        try
        {
            await using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, true))
            {
                await JsonSerializer.SerializeAsync(stream, new { items }, ExportJsonOptions, ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            var parsed = await new ReadOnlyLibraryJsonReader().ReadAsync(tmp, ct).ConfigureAwait(false);
            var expected = ArtifactSnapshotFingerprint.Compute(items);
            if (!string.Equals(expected, parsed.SnapshotFingerprint, StringComparison.Ordinal))
                throw new InvalidDataException("Recovery JSON fingerprint mismatch.");
            File.Move(tmp, paths.RecoveryExportFile, true);
        }
        finally
        {
            TryDelete(tmp);
        }
    }

    private static async Task<string> TryRestoreNewestBackupAsync(ProductionStoragePaths paths, ProductionDataSafetyBoundary safety, CancellationToken ct)
    {
        if (!Directory.Exists(paths.SqliteBackupsDirectory)) return "";
        foreach (var backup in Directory.EnumerateFiles(paths.SqliteBackupsDirectory, "library-*.db").OrderByDescending(File.GetLastWriteTimeUtc))
        {
            var candidateStore = new SqliteMigrationStore(backup, safety);
            var check = await candidateStore.CheckIntegrityAsync(false, ct).ConfigureAwait(false);
            if (!check.IsOk) continue;

            var tmp = paths.DatabaseFile + ".restore.tmp";
            safety.EnsureWriteAllowed(tmp);
            try
            {
                TryDelete(tmp);
                await SqliteMigrationStore.CreateOnlineBackupAsync(backup, tmp, safety, ct).ConfigureAwait(false);
                if (File.Exists(paths.DatabaseFile)) QuarantineCorruptMain(paths, safety);
                File.Move(tmp, paths.DatabaseFile, true);
                var restored = new SqliteMigrationStore(paths.DatabaseFile, safety);
                var restoredCheck = await restored.CheckIntegrityAsync(false, ct).ConfigureAwait(false);
                if (!restoredCheck.IsOk) throw new InvalidDataException($"Restored SQLite backup failed quick_check: {restoredCheck.Message}");
                return backup;
            }
            finally
            {
                TryDelete(tmp);
            }
        }
        return "";
    }

    private static void QuarantineCorruptMain(ProductionStoragePaths paths, ProductionDataSafetyBoundary safety)
    {
        if (!File.Exists(paths.DatabaseFile)) return;
        safety.EnsureWriteAllowed(paths.CorruptDirectory);
        Directory.CreateDirectory(paths.CorruptDirectory);
        var destination = Path.Combine(paths.CorruptDirectory, $"library.corrupt-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss.fff}-{Guid.NewGuid():N}.db");
        safety.EnsureWriteAllowed(destination);
        DeleteSidecars(paths.DatabaseFile, safety);
        File.Move(paths.DatabaseFile, destination, true);
    }

    private static void DeleteSidecars(string databaseFile, ProductionDataSafetyBoundary safety)
    {
        foreach (var file in new[] { databaseFile + "-wal", databaseFile + "-shm", databaseFile + "-journal" })
        {
            if (!File.Exists(file)) continue;
            safety.EnsureWriteAllowed(file);
            File.Delete(file);
        }
    }

    private static async Task<string> Sha256Async(string file, CancellationToken ct)
    {
        await using var stream = File.OpenRead(file);
        var hash = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void TryDelete(string file)
    {
        try { if (File.Exists(file)) File.Delete(file); } catch { }
    }
}
