using System.Text.RegularExpressions;
using FreeCamManager.Core.Models;

namespace FreeCamManager.Core.Services;

public static partial class StableVersionResolver
{
    public static string Resolve(Artifact artifact)
    {
        foreach (var candidate in new[] { artifact.Version, artifact.BuildName, artifact.Stage })
        {
            var normalized = Normalize(candidate);
            if (!string.IsNullOrWhiteSpace(normalized)) return normalized;
        }

        var name = artifact.Name ?? "";
        var match = StableNameRegex().Match(name);
        return match.Success ? match.Groups[1].Value : "";
    }

    private static string Normalize(string value)
    {
        var trimmed = (value ?? "").Trim();
        if (ReleaseVersionRegex().IsMatch(trimmed)) return trimmed;
        var match = StableNameRegex().Match(trimmed);
        return match.Success ? match.Groups[1].Value : "";
    }

    /// <summary>
    /// Treat an existing incorrectly categorized FreeCam Stable as recoverable.
    /// Do not depend exclusively on the legacy Category field in SQLite.
    /// WW Fix packages and Result ZIPs must never be interpreted as FreeCam Stable.
    /// </summary>
    public static bool IsStableMaterial(Artifact item)
    {
        if (string.Equals(item.ArtifactType, "Result", StringComparison.OrdinalIgnoreCase))
            return false;
        var name = item.Name ?? "";
        var stem = Path.GetFileNameWithoutExtension(name);
        if (stem.EndsWith("_Result", StringComparison.OrdinalIgnoreCase) ||
            System.Text.RegularExpressions.Regex.IsMatch(stem, @"_Result\([0-9]+\)$", RegexOptions.IgnoreCase))
            return false;
        if (item.Category is "Stable" or "StableCandidate") return true;
        if (string.IsNullOrWhiteSpace(Resolve(item))) return false;
        if (!name.StartsWith("FreeCam_R", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(item.Project, "FreeCam", StringComparison.OrdinalIgnoreCase))
            return false;
        if (StableNameRegex().IsMatch(name)) return true;
        return string.Equals(item.Stage, "Stable", StringComparison.OrdinalIgnoreCase)
            && (string.Equals(item.BuildType, "Release", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.BuildType, "Stable", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.ReleaseState, "Stable", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Filename roles override stale SQLite metadata when unambiguous.</summary>
    public static string MaterialKind(Artifact item)
    {
        if (string.Equals(item.ArtifactType, "Result", StringComparison.OrdinalIgnoreCase))
            return "Result";
        var n = Path.GetFileNameWithoutExtension(item.Name ?? "").ToLowerInvariant();
        if (n.EndsWith("_source") || n.EndsWith("_fullsource") || n.EndsWith("_sourcefull"))
            return "Source";
        if (n.EndsWith("_repo") || (item.Name ?? "").EndsWith(".bundle", StringComparison.OrdinalIgnoreCase))
            return "Repo";
        if (n.EndsWith("_sha256")) return "SHA256";
        if (n.EndsWith("_release_note")) return "ReleaseNote";
        if (StableNameRegex().IsMatch(item.Name ?? "") &&
            !n.EndsWith("_source") && !n.EndsWith("_fullsource") && !n.EndsWith("_sourcefull"))
            return string.IsNullOrWhiteSpace(item.ArtifactType) ||
                   string.Equals(item.ArtifactType, "Runtime", StringComparison.OrdinalIgnoreCase)
                ? "Runtime" : item.ArtifactType;
        return item.ArtifactType ?? "";
    }

    [GeneratedRegex(@"^R[0-9]+(?:\.[0-9]+){0,2}$", RegexOptions.IgnoreCase)]
    private static partial Regex ReleaseVersionRegex();

    [GeneratedRegex(@"^FreeCam_(R[0-9]+(?:\.[0-9]+){0,2})(?:_W[0-9]+)?(?:_(?:Source|FullSource|SourceFull|Runtime|Stable|Repo|SHA256|RELEASE_NOTE))?(?:\.(?:zip|bundle|txt|md))?$", RegexOptions.IgnoreCase)]
    private static partial Regex StableNameRegex();
}
