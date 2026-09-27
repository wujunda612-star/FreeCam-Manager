using System.IO.Compression;
using System.Text.Json;
using FreeCamManager.Core.Models;

namespace FreeCamManager.Core.Services;

/// <summary>Strict, ordered user-configured launch and drag selection.
/// No implicit fallback from a missing Result ZIP to a .log.</summary>
public static class FileRuleSelector
{
    public static string FindLaunch(string testingPath, IReadOnlyList<FilePatternRule>? configured,
        string? manualOverride = null)
    {
        if (string.IsNullOrWhiteSpace(testingPath) || !Directory.Exists(testingPath)) return "";
        var manual = ResolveWithin(testingPath, manualOverride);
        if (!string.IsNullOrWhiteSpace(manual) && File.Exists(manual)
            && IsLaunchExtension(manual)) return manual;

        var candidates = EnumerateSafe(testingPath)
            .Where(IsLaunchExtension)
            .Where(path => !IsExcludedEvidence(path, testingPath))
            .OrderBy(path => RelativeDepth(path, testingPath))
            .ThenBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToList();
        return FirstByRules(configured ?? RuleDefaults.Launch(), candidates);
    }

    public static string FindDrag(Artifact build, string testingPath, string resultRoot,
        IReadOnlyList<FilePatternRule>? configured, string? manualOverride = null)
    {
        // A manually selected file overrides the global rules for this version,
        // but cannot silently select reference evidence or a foreign BuildId.
        if (!string.IsNullOrWhiteSpace(manualOverride) && File.Exists(manualOverride)
            && SafeEvidenceExtension(manualOverride)
            && !HasForeignBuildId(manualOverride, build.BuildId)
            && ((IsWithin(manualOverride, testingPath) && !IsExcludedEvidence(manualOverride, testingPath))
                || IsWithin(manualOverride, resultRoot)))
            return manualOverride;

        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(testingPath) && Directory.Exists(testingPath))
            candidates.AddRange(EnumerateSafe(testingPath)
                .Where(SafeEvidenceExtension)
                .Where(path => !IsExcludedEvidence(path, testingPath))
                .Where(path => !HasForeignBuildId(path, build.BuildId)));

        // Only explicitly paired evidence and legacy name-verified evidence may
        // come from the shared 40_Result tree. Never blindly pick the newest ZIP.
        if (!string.IsNullOrWhiteSpace(build.ResultPath) && File.Exists(build.ResultPath)
            && SafeEvidenceExtension(build.ResultPath)
            && !HasForeignBuildId(build.ResultPath, build.BuildId)
            && !IsExcludedEvidence(build.ResultPath, testingPath))
            candidates.Add(build.ResultPath);

        var legacy = new TestResultService(new ManifestService())
            .ResolvePreferredDragPath(build, testingPath, resultRoot);
        if (!string.IsNullOrWhiteSpace(legacy) && File.Exists(legacy)
            && SafeEvidenceExtension(legacy)
            && !HasForeignBuildId(legacy, build.BuildId)
            && !IsExcludedEvidence(legacy, testingPath))
            candidates.Add(legacy);

        // File-rule order is absolute. Within one rule, prefer active Results/
        // over other testing files, followed by paired archived evidence.
        var ordered = candidates.Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => EvidenceRank(path, testingPath))
            .ThenByDescending(path => File.GetLastWriteTimeUtc(path))
            .ToList();
        return FirstByRules(configured ?? RuleDefaults.Drag(), ordered);
    }

    public static string FirstByRules(IReadOnlyList<FilePatternRule> rules, IEnumerable<string> candidates)
    {
        var files = candidates.ToArray();
        foreach (var rule in rules)
        {
            if (!rule.Enabled || string.IsNullOrWhiteSpace(rule.Pattern)) continue;
            var match = files.FirstOrDefault(path => RuleMatcher.Glob(rule.Pattern, Path.GetFileName(path)));
            if (!string.IsNullOrEmpty(match)) return match;
        }
        return "";
    }

    private static bool SafeEvidenceExtension(string path)
        => new[] { ".zip", ".log", ".txt", ".json" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    private static bool IsLaunchExtension(string path)
        => new[] { ".cmd", ".bat", ".ps1", ".exe" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<string> EnumerateSafe(string root)
    {
        try { return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToArray(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }

    private static string ResolveWithin(string root, string? relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)) return "";
        try
        {
            var combined = Path.GetFullPath(Path.Combine(root, relative));
            return IsWithin(combined, root) ? combined : "";
        }
        catch { return ""; }
    }

    private static bool IsWithin(string file, string root)
    {
        if (string.IsNullOrWhiteSpace(file) || string.IsNullOrWhiteSpace(root)) return false;
        try
        {
            var dir = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            return Path.GetFullPath(file).StartsWith(dir, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static bool IsExcludedEvidence(string file, string testingPath)
    {
        if (!IsWithin(file, testingPath)) return false;
        var relative = Path.GetRelativePath(testingPath, file);
        var segments = relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < segments.Length - 1; i++)
        {
            var segment = segments[i].Replace('-', '_').Replace(' ', '_');
            if (segment.StartsWith("prior", StringComparison.OrdinalIgnoreCase)
                || segment.Contains("reference", StringComparison.OrdinalIgnoreCase)
                || segment.Contains("evidence", StringComparison.OrdinalIgnoreCase)
                || segment.Equals("Validation", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static int RelativeDepth(string path, string root)
        => Path.GetRelativePath(root, path).Count(x => x is '\\' or '/');

    private static int EvidenceRank(string path, string testingPath)
    {
        if (!IsWithin(path, testingPath)) return 4;
        var rel = Path.GetRelativePath(testingPath, path);
        var parent = Path.GetDirectoryName(rel) ?? "";
        if (parent.Equals("Results", StringComparison.OrdinalIgnoreCase)) return 0;
        if (RelativeDepth(path, testingPath) == 0) return 1;
        if (parent.Equals("Logs", StringComparison.OrdinalIgnoreCase)) return 2;
        return 3;
    }

    private static bool HasForeignBuildId(string path, string buildId)
    {
        if (string.IsNullOrWhiteSpace(buildId) ||
            !Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            using var archive = ZipFile.OpenRead(path);
            var entry = archive.Entries.FirstOrDefault(e =>
                Path.GetFileName(e.FullName).Equals("RESULT_MANIFEST.json", StringComparison.OrdinalIgnoreCase));
            if (entry is null || entry.Length > 1024 * 1024) return false;
            using var stream = entry.Open();
            using var doc = JsonDocument.Parse(stream);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;
            foreach (var field in doc.RootElement.EnumerateObject())
                if (field.Name.Equals("ForBuildId", StringComparison.OrdinalIgnoreCase)
                    && field.Value.ValueKind == JsonValueKind.String)
                    return field.Value.GetString() is { Length: > 0 } other
                        && !string.Equals(other, buildId, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException) { }
        return false;
    }
}
