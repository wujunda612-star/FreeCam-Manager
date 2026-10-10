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
        return ReleaseVersionRegex().IsMatch(trimmed) ? trimmed : "";
    }

    [GeneratedRegex(@"^R[0-9]+(?:\.[0-9]+){0,2}$", RegexOptions.IgnoreCase)]
    private static partial Regex ReleaseVersionRegex();

    [GeneratedRegex(@"^FreeCam_(R[0-9]+(?:\.[0-9]+){0,2})(?:_(?:Source|Repo|SHA256|RELEASE_NOTE))?(?:\.(?:zip|bundle|txt|md))?$", RegexOptions.IgnoreCase)]
    private static partial Regex StableNameRegex();
}
