using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FreeCamManager.Core.Models;

namespace FreeCamManager.SQLiteMigration.Core;

public static class ArtifactSnapshotFingerprint
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    public static string Compute(IEnumerable<Artifact> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var ordered = items
            .Select(x => x.Clone())
            .OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Path, StringComparer.Ordinal)
            .ToArray();
        var json = JsonSerializer.Serialize(ordered, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }
}
