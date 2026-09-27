namespace FreeCamManager.SQLiteMigration.Core;

public sealed class LibraryMigrationCoordinator
{
    private readonly MigrationPaths _paths;
    private readonly MigrationSafetyBoundary _safety;

    public LibraryMigrationCoordinator(MigrationPaths paths, MigrationSafetyBoundary safety)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _safety = safety ?? throw new ArgumentNullException(nameof(safety));
    }

    public async Task<MigrationReport> MigrateAsync(ReadOnlyLibrarySnapshot source, MigrationFault fault = MigrationFault.None, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        _safety.EnsureExperimentWriteAllowed(_paths.ExperimentDataDirectory);
        Directory.CreateDirectory(_paths.ExperimentDataDirectory);
        Directory.CreateDirectory(_paths.BackupsDirectory);

        var staging = Path.Combine(_paths.ExperimentDataDirectory, $"library.staging-{Guid.NewGuid():N}.db");
        _safety.EnsureExperimentWriteAllowed(staging);
        DeleteDatabaseFamily(staging);
        var previousBackup = "";

        try
        {
            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["migration_version"] = "Test1",
                ["source_file"] = source.SourceFile,
                ["source_sha256"] = source.SourceSha256,
                ["source_last_write_utc"] = source.SourceLastWriteUtc.ToString("O"),
                ["source_item_count"] = source.Items.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["snapshot_fingerprint"] = source.SnapshotFingerprint,
                ["migrated_at_utc"] = DateTimeOffset.UtcNow.ToString("O")
            };

            var stagedStore = new SqliteMigrationStore(staging, _safety);
            await stagedStore.SaveSnapshotAsync(source.Items, metadata, ct).ConfigureAwait(false);
            var stagedIntegrity = await stagedStore.CheckIntegrityAsync(full: false, ct).ConfigureAwait(false);
            if (!stagedIntegrity.IsOk) throw new InvalidDataException($"Staged SQLite quick_check failed: {stagedIntegrity.Message}");
            var stagedItems = await stagedStore.LoadSnapshotAsync(ct).ConfigureAwait(false);
            var stagedFingerprint = ArtifactSnapshotFingerprint.Compute(stagedItems);
            if (!string.Equals(source.SnapshotFingerprint, stagedFingerprint, StringComparison.Ordinal))
                throw new InvalidDataException($"Staged SQLite snapshot mismatch. source={source.SnapshotFingerprint}, sqlite={stagedFingerprint}");

            if (fault == MigrationFault.BeforePromotion)
                throw new IOException("Injected migration failure before database promotion.");

            if (File.Exists(_paths.DatabaseFile))
                previousBackup = await BackupExistingDatabaseAsync(ct).ConfigureAwait(false);

            DeleteDatabaseSidecars(_paths.DatabaseFile);
            _safety.EnsureExperimentWriteAllowed(_paths.DatabaseFile);
            File.Move(staging, _paths.DatabaseFile, true);

            var promotedStore = new SqliteMigrationStore(_paths.DatabaseFile, _safety);
            var promotedIntegrity = await promotedStore.CheckIntegrityAsync(full: false, ct).ConfigureAwait(false);
            if (!promotedIntegrity.IsOk) throw new InvalidDataException($"Promoted SQLite quick_check failed: {promotedIntegrity.Message}");
            var promotedItems = await promotedStore.LoadSnapshotAsync(ct).ConfigureAwait(false);
            var promotedFingerprint = ArtifactSnapshotFingerprint.Compute(promotedItems);
            if (!string.Equals(source.SnapshotFingerprint, promotedFingerprint, StringComparison.Ordinal))
                throw new InvalidDataException($"Promoted SQLite snapshot mismatch. source={source.SnapshotFingerprint}, sqlite={promotedFingerprint}");

            return new MigrationReport(
                true,
                true,
                source.UsedBackup,
                source.SourceFile,
                source.SourceSha256,
                source.Items.Count,
                promotedFingerprint,
                _paths.DatabaseFile,
                previousBackup,
                "Read-only JSON -> isolated SQLite migration verified.");
        }
        finally
        {
            DeleteDatabaseFamily(staging);
        }
    }

    private async Task<string> BackupExistingDatabaseAsync(CancellationToken ct)
    {
        _safety.EnsureExperimentWriteAllowed(_paths.BackupsDirectory);
        Directory.CreateDirectory(_paths.BackupsDirectory);
        var destination = Path.Combine(_paths.BackupsDirectory, $"library-before-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss.fff}-{Guid.NewGuid():N}.db");
        await SqliteMigrationStore.CreateOnlineBackupAsync(_paths.DatabaseFile, destination, _safety, ct).ConfigureAwait(false);
        var checkStore = new SqliteMigrationStore(destination, _safety);
        var check = await checkStore.CheckIntegrityAsync(false, ct).ConfigureAwait(false);
        if (!check.IsOk)
        {
            try { File.Delete(destination); } catch { }
            throw new InvalidDataException($"Previous experiment database backup failed quick_check: {check.Message}");
        }
        return destination;
    }

    private void DeleteDatabaseFamily(string baseFile)
    {
        foreach (var file in new[] { baseFile, baseFile + "-wal", baseFile + "-shm", baseFile + "-journal" })
        {
            if (!File.Exists(file)) continue;
            _safety.EnsureExperimentWriteAllowed(file);
            File.Delete(file);
        }
    }

    private void DeleteDatabaseSidecars(string baseFile)
    {
        foreach (var file in new[] { baseFile + "-wal", baseFile + "-shm", baseFile + "-journal" })
        {
            if (!File.Exists(file)) continue;
            _safety.EnsureExperimentWriteAllowed(file);
            File.Delete(file);
        }
    }
}
