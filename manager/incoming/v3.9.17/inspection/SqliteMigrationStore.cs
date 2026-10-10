using System.Text.Json;
using FreeCamManager.Core.Models;
using FreeCamManager.Core.Services;
using Microsoft.Data.Sqlite;

namespace FreeCamManager.SQLiteMigration.Core;

public sealed class SqliteMigrationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private readonly string _databaseFile;
    private readonly ISqliteWriteBoundary _safety;

    public SqliteMigrationStore(string databaseFile, ISqliteWriteBoundary safety)
    {
        _databaseFile = Path.GetFullPath(databaseFile ?? throw new ArgumentNullException(nameof(databaseFile)));
        _safety = safety ?? throw new ArgumentNullException(nameof(safety));
    }

    public string DatabaseFile => _databaseFile;

    public async Task SaveSnapshotAsync(IReadOnlyCollection<Artifact> items, IReadOnlyDictionary<string, string>? metadata = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        _safety.EnsureWriteAllowed(_databaseFile);
        var parent = Path.GetDirectoryName(_databaseFile) ?? throw new InvalidOperationException("Database parent directory missing.");
        _safety.EnsureWriteAllowed(parent);
        Directory.CreateDirectory(parent);

        await using var connection = await OpenConnectionAsync(SqliteOpenMode.ReadWriteCreate, configureWal: true, ct).ConfigureAwait(false);
        await EnsureSchemaAsync(connection, ct).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction(deferred: false);
        try
        {
            using (var clear = connection.CreateCommand())
            {
                clear.Transaction = transaction;
                clear.CommandText = "DELETE FROM artifacts; DELETE FROM meta;";
                await clear.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            foreach (var item in items.OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase))
            {
                using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = "INSERT INTO artifacts(path, payload_json) VALUES ($path, $payload);";
                insert.Parameters.AddWithValue("$path", item.Path ?? "");
                insert.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(item, JsonOptions));
                await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            if (metadata is not null)
            {
                foreach (var pair in metadata)
                {
                    using var meta = connection.CreateCommand();
                    meta.Transaction = transaction;
                    meta.CommandText = "INSERT INTO meta(key, value) VALUES ($key, $value);";
                    meta.Parameters.AddWithValue("$key", pair.Key);
                    meta.Parameters.AddWithValue("$value", pair.Value ?? "");
                    await meta.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }
            }

            transaction.Commit();
        }
        catch
        {
            try { transaction.Rollback(); } catch { }
            throw;
        }

        await CheckpointAsync(connection, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Atomically applies only the changed SQLite rows. The legacy full-snapshot
    /// writer remains intact for migration/recovery flows.
    /// </summary>
    public async Task ApplyDeltaAsync(
        IReadOnlyDictionary<string, string> upserts,
        IReadOnlyCollection<string> removedPaths,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken ct = default)
    {
        _safety.EnsureWriteAllowed(_databaseFile);
        var parent = Path.GetDirectoryName(_databaseFile) ?? throw new InvalidOperationException("Database parent directory missing.");
        _safety.EnsureWriteAllowed(parent);
        Directory.CreateDirectory(parent);
        await using var connection = await OpenConnectionAsync(SqliteOpenMode.ReadWriteCreate, configureWal: true, ct).ConfigureAwait(false);
        await EnsureSchemaAsync(connection, ct).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction(deferred: false);
        try
        {
            using (var write = connection.CreateCommand())
            {
                write.Transaction = transaction;
                write.CommandText = @"INSERT INTO artifacts(path, payload_json) VALUES ($path, $payload)
                    ON CONFLICT(path) DO UPDATE SET payload_json = excluded.payload_json;";
                var p = write.Parameters.Add("$path", SqliteType.Text);
                var v = write.Parameters.Add("$payload", SqliteType.Text);
                foreach (var pair in upserts)
                {
                    ct.ThrowIfCancellationRequested();
                    p.Value = pair.Key;
                    v.Value = pair.Value;
                    await write.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }
            }

            using (var remove = connection.CreateCommand())
            {
                remove.Transaction = transaction;
                remove.CommandText = "DELETE FROM artifacts WHERE path = $path COLLATE NOCASE;";
                var p = remove.Parameters.Add("$path", SqliteType.Text);
                foreach (var path in removedPaths)
                {
                    ct.ThrowIfCancellationRequested();
                    p.Value = path;
                    await remove.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }
            }

            using (var meta = connection.CreateCommand())
            {
                meta.Transaction = transaction;
                meta.CommandText = @"INSERT INTO meta(key, value) VALUES ($key, $value)
                    ON CONFLICT(key) DO UPDATE SET value = excluded.value;";
                var key = meta.Parameters.Add("$key", SqliteType.Text);
                var value = meta.Parameters.Add("$value", SqliteType.Text);
                foreach (var pair in metadata)
                {
                    key.Value = pair.Key;
                    value.Value = pair.Value ?? "";
                    await meta.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }
            }
            transaction.Commit();
        }
        catch
        {
            try { transaction.Rollback(); } catch { }
            throw;
        }
        await CheckpointAsync(connection, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Cheap post-commit verification for a delta. Checks every changed/deleted
    /// row and the cardinality; full quick_check and rolling backups remain
    /// the session's responsibility.
    /// </summary>
    public async Task VerifyDeltaAsync(
        IReadOnlyDictionary<string, string> upserts,
        IReadOnlyCollection<string> removedPaths,
        int expectedCount,
        CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(SqliteOpenMode.ReadOnly, configureWal: false, ct).ConfigureAwait(false);
        using (var count = connection.CreateCommand())
        {
            count.CommandText = "SELECT COUNT(*) FROM artifacts;";
            var actual = Convert.ToInt32(await count.ExecuteScalarAsync(ct).ConfigureAwait(false));
            if (actual != expectedCount)
                throw new InvalidDataException($"SQLite delta row-count mismatch: expected={expectedCount}, actual={actual}");
        }
        using var query = connection.CreateCommand();
        query.CommandText = "SELECT payload_json FROM artifacts WHERE path = $path COLLATE NOCASE;";
        var parameter = query.Parameters.Add("$path", SqliteType.Text);
        foreach (var pair in upserts)
        {
            parameter.Value = pair.Key;
            var actual = await query.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
            if (!string.Equals(actual, pair.Value, StringComparison.Ordinal))
                throw new InvalidDataException($"SQLite delta row verification failed: {pair.Key}");
        }
        foreach (var path in removedPaths)
        {
            parameter.Value = path;
            if (await query.ExecuteScalarAsync(ct).ConfigureAwait(false) is not null)
                throw new InvalidDataException($"SQLite delta deleted row still present: {path}");
        }
    }

    public async Task<IReadOnlyList<Artifact>> LoadSnapshotAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_databaseFile)) throw new FileNotFoundException("SQLite migration database does not exist.", _databaseFile);
        await using var connection = await OpenConnectionAsync(SqliteOpenMode.ReadOnly, configureWal: false, ct).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload_json FROM artifacts ORDER BY path COLLATE NOCASE;";
        var normalized = LibraryService.CreateInMemory();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var artifact = JsonSerializer.Deserialize<Artifact>(reader.GetString(0), JsonOptions)
                ?? throw new InvalidDataException("SQLite artifact payload is empty.");
            normalized.Upsert(artifact);
        }
        return normalized.Snapshot();
    }

    public async Task<IReadOnlyDictionary<string, string>> LoadMetadataAsync(CancellationToken ct = default)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(_databaseFile)) return result;
        await using var connection = await OpenConnectionAsync(SqliteOpenMode.ReadOnly, configureWal: false, ct).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT key, value FROM meta ORDER BY key;";
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false)) result[reader.GetString(0)] = reader.GetString(1);
        return result;
    }

    public async Task<SqliteIntegrityResult> CheckIntegrityAsync(bool full = false, CancellationToken ct = default)
    {
        var check = full ? "integrity_check" : "quick_check";
        try
        {
            if (!File.Exists(_databaseFile)) return SqliteIntegrityResult.Failed(check, "database file does not exist");
            await using var connection = await OpenConnectionAsync(SqliteOpenMode.ReadOnly, configureWal: false, ct).ConfigureAwait(false);
            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA {check};";
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            var messages = new List<string>();
            while (await reader.ReadAsync(ct).ConfigureAwait(false)) messages.Add(reader.IsDBNull(0) ? "" : reader.GetString(0));
            return messages.Count == 1 && string.Equals(messages[0], "ok", StringComparison.OrdinalIgnoreCase)
                ? SqliteIntegrityResult.Ok(check)
                : SqliteIntegrityResult.Failed(check, messages.Count == 0 ? "no result" : string.Join(" | ", messages));
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return SqliteIntegrityResult.Failed(check, ex.Message);
        }
    }

    public static async Task CreateOnlineBackupAsync(string sourceFile, string destinationFile, ISqliteWriteBoundary safety, CancellationToken ct = default)
    {
        safety.EnsureWriteAllowed(sourceFile);
        safety.EnsureWriteAllowed(destinationFile);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
        if (File.Exists(destinationFile)) File.Delete(destinationFile);

        var sourceBuilder = new SqliteConnectionStringBuilder { DataSource = sourceFile, Mode = SqliteOpenMode.ReadOnly, Cache = SqliteCacheMode.Private, Pooling = false };
        var destinationBuilder = new SqliteConnectionStringBuilder { DataSource = destinationFile, Mode = SqliteOpenMode.ReadWriteCreate, Cache = SqliteCacheMode.Private, Pooling = false };
        await using var source = new SqliteConnection(sourceBuilder.ToString());
        await using var destination = new SqliteConnection(destinationBuilder.ToString());
        await source.OpenAsync(ct).ConfigureAwait(false);
        await destination.OpenAsync(ct).ConfigureAwait(false);
        source.BackupDatabase(destination);
    }

    private async Task<SqliteConnection> OpenConnectionAsync(SqliteOpenMode mode, bool configureWal, CancellationToken ct)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _databaseFile,
            Mode = mode,
            Cache = SqliteCacheMode.Private,
            Pooling = false
        };
        var connection = new SqliteConnection(builder.ToString());
        try
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);
            if (configureWal)
            {
                using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
                await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task EnsureSchemaAsync(SqliteConnection connection, CancellationToken ct)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS artifacts(
                path TEXT PRIMARY KEY COLLATE NOCASE,
                payload_json TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS meta(
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task CheckpointAsync(SqliteConnection connection, CancellationToken ct)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
