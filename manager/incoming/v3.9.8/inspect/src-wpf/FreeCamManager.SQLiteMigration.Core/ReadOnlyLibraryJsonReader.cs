using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using FreeCamManager.Core.Models;
using FreeCamManager.Core.Services;

namespace FreeCamManager.SQLiteMigration.Core;

public sealed record ReadOnlyLibrarySnapshot(
    string SourceFile,
    string SourceSha256,
    DateTime SourceLastWriteUtc,
    IReadOnlyList<Artifact> Items,
    string SnapshotFingerprint,
    bool UsedBackup);

public sealed class ReadOnlyLibraryJsonReader
{
    private sealed class LibraryDocument
    {
        [JsonPropertyName("items")] public List<Artifact> Items { get; set; } = [];
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<ReadOnlyLibrarySnapshot> ReadAsync(string primaryFile, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(primaryFile)) throw new ArgumentException("Primary library path is required.", nameof(primaryFile));
        primaryFile = Path.GetFullPath(primaryFile);
        var backup = primaryFile + ".bak";
        Exception? primaryError = null;

        if (File.Exists(primaryFile))
        {
            try { return await ReadCandidateAsync(primaryFile, usedBackup: false, ct).ConfigureAwait(false); }
            catch (Exception ex) when (ex is JsonException or InvalidDataException) { primaryError = ex; }
        }
        else
        {
            primaryError = new FileNotFoundException("Production library.json does not exist.", primaryFile);
        }

        if (File.Exists(backup))
        {
            try { return await ReadCandidateAsync(backup, usedBackup: true, ct).ConfigureAwait(false); }
            catch (Exception ex) when (ex is JsonException or InvalidDataException)
            {
                throw new InvalidDataException($"Production library primary and backup are both invalid. Primary: {primaryError?.Message}; Backup: {ex.Message}", ex);
            }
        }

        throw new InvalidDataException($"Production library is unavailable or invalid and no valid backup exists. {primaryError?.Message}", primaryError);
    }

    private static async Task<ReadOnlyLibrarySnapshot> ReadCandidateAsync(string file, bool usedBackup, CancellationToken ct)
    {
        await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var doc = await JsonSerializer.DeserializeAsync<LibraryDocument>(stream, JsonOptions, ct).ConfigureAwait(false)
            ?? throw new InvalidDataException("library JSON is empty");
        doc.Items ??= [];

        var normalized = LibraryService.CreateInMemory(doc.Items).Snapshot();
        var info = new FileInfo(file);
        var sha = await ComputeFileSha256Async(file, ct).ConfigureAwait(false);
        return new ReadOnlyLibrarySnapshot(
            Path.GetFullPath(file),
            sha,
            info.LastWriteTimeUtc,
            normalized,
            ArtifactSnapshotFingerprint.Compute(normalized),
            usedBackup);
    }

    private static async Task<string> ComputeFileSha256Async(string file, CancellationToken ct)
    {
        await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
