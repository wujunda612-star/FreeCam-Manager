using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FreeCamManager.Core.Models;

namespace FreeCamManager.SQLiteMigration.Core;

public sealed record RollbackResult(string LegacyLibraryFile, string BackupFile, string SqliteArchiveDirectory, int ItemCount, string SnapshotFingerprint);

public sealed class LegacyJsonRollbackService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task<RollbackResult> RestoreLegacyJsonAsync(ProductionStoragePaths paths, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var safety = new ProductionDataSafetyBoundary(paths, allowLegacyJsonWrites: true);
        var session = await ProductionManagerSqliteLibrarySession.OpenAsync(paths, ct).ConfigureAwait(false);
        var items = await session.ReloadAsync(ct).ConfigureAwait(false);
        var fingerprint = ArtifactSnapshotFingerprint.Compute(items);

        safety.EnsureWriteAllowed(paths.RollbackBackupDirectory);
        Directory.CreateDirectory(paths.RollbackBackupDirectory);
        var backupFile = "";
        if (File.Exists(paths.LegacyLibraryFile))
        {
            backupFile = Path.Combine(paths.RollbackBackupDirectory, $"library-before-rollback-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss.fff}-{Guid.NewGuid():N}.json");
            safety.EnsureWriteAllowed(backupFile);
            File.Copy(paths.LegacyLibraryFile, backupFile, false);
        }

        var tmp = paths.LegacyLibraryFile + ".rollback.tmp";
        safety.EnsureWriteAllowed(tmp);
        safety.EnsureWriteAllowed(paths.LegacyLibraryFile);
        try
        {
            await using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, true))
            {
                await JsonSerializer.SerializeAsync(stream, new { items }, JsonOptions, ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            var parsed = await new ReadOnlyLibraryJsonReader().ReadAsync(tmp, ct).ConfigureAwait(false);
            if (!string.Equals(fingerprint, parsed.SnapshotFingerprint, StringComparison.Ordinal))
                throw new InvalidDataException("Rollback JSON fingerprint mismatch.");
            File.Move(tmp, paths.LegacyLibraryFile, true);
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }

        var sqliteArchiveDirectory = ArchiveActiveSqliteAfterRollback(paths, safety);
        return new(paths.LegacyLibraryFile, backupFile, sqliteArchiveDirectory, items.Count, fingerprint);
    }

    private static string ArchiveActiveSqliteAfterRollback(ProductionStoragePaths paths, ProductionDataSafetyBoundary safety)
    {
        var archiveDirectory = Path.Combine(paths.RollbackBackupDirectory, $"sqlite-after-rollback-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss.fff}-{Guid.NewGuid():N}");
        safety.EnsureWriteAllowed(archiveDirectory);
        Directory.CreateDirectory(archiveDirectory);

        foreach (var source in new[]
        {
            paths.DatabaseFile,
            paths.DatabaseFile + "-wal",
            paths.DatabaseFile + "-shm",
            paths.DatabaseFile + "-journal",
            paths.RecoveryExportFile
        })
        {
            if (!File.Exists(source)) continue;
            safety.EnsureWriteAllowed(source);
            var target = Path.Combine(archiveDirectory, Path.GetFileName(source));
            safety.EnsureWriteAllowed(target);
            File.Move(source, target, true);
        }

        if (Directory.Exists(paths.SqliteBackupsDirectory))
        {
            safety.EnsureWriteAllowed(paths.SqliteBackupsDirectory);
            var backupArchive = Path.Combine(archiveDirectory, "SQLiteBackups");
            safety.EnsureWriteAllowed(backupArchive);
            Directory.Move(paths.SqliteBackupsDirectory, backupArchive);
        }

        return archiveDirectory;
    }
}
