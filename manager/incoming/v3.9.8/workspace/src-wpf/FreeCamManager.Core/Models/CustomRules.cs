using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace FreeCamManager.Core.Models;

/// <summary>Filename glob; order in the settings list is its priority.</summary>
public sealed class FilePatternRule
{
    [JsonPropertyName("pattern")] public string Pattern { get; set; } = "";
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
    public FilePatternRule Clone() => new() { Pattern = Pattern, Enabled = Enabled };
}

/// <summary>A user-editable version of one legacy classifier rule.</summary>
public sealed class ClassificationRule
{
    [JsonPropertyName("field")] public string Field { get; set; } = "FileName";
    [JsonPropertyName("pattern")] public string Pattern { get; set; } = "*";
    [JsonPropertyName("category")] public string Category { get; set; } = "";
    [JsonPropertyName("folder")] public string Folder { get; set; } = "";
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
    public ClassificationRule Clone() => new() { Field = Field, Pattern = Pattern, Category = Category, Folder = Folder, Enabled = Enabled };
}

public static class RuleDefaults
{
    public static List<FilePatternRule> Launch() => new[] {
        "Start.cmd", "Start.bat", "Start.ps1", "Start.exe",
        "Start*.cmd", "Start*.bat", "Start*.ps1", "Start*.exe"
    }.Select(x => new FilePatternRule { Pattern = x }).ToList();

    public static List<FilePatternRule> Drag() => new[] {
        "*_Result.zip", "*Result*.zip"
    }.Select(x => new FilePatternRule { Pattern = x }).ToList();

    // This is the original ordering. The Result package handling is a separate
    // system mechanism (pairing/drag), not an editable user classification.
    public static List<ClassificationRule> Classification() => new()
    {
        new() { Field="FileName", Pattern="FreeCam_Manager_*", Category="Manager", Folder="50_Manager" },
        new() { Field="FileName", Pattern="WW底层索引_*", Category="IndexLibrary", Folder="60_索引库" },
        new() { Field="FileName", Pattern="WW底层索引库_*", Category="IndexLibrary", Folder="60_索引库" },
        new() { Field="StableLike", Pattern="*", Category="StableCandidate", Folder="90_Unknown/Stable_Candidate/{Version}" },
        new() { Field="FeatureLike", Pattern="*", Category="Feature", Folder="20_Feature/{Feature}/{Stage}" },
        new() { Field="ExperimentLike", Pattern="*", Category="Experiment", Folder="30_Experiment/{Feature}/{Stage}" }
    };
}

public static class RuleMatcher
{
    private static readonly HashSet<string> BuiltIn = new(StringComparer.OrdinalIgnoreCase)
    {
        "Stable", "StableCandidate", "Feature", "Experiment", "Result",
        "Manager", "IndexLibrary", "Duplicate", "Archive", "Unknown"
    };
    public static bool IsBuiltInCategory(string? category)
        => string.IsNullOrWhiteSpace(category) || BuiltIn.Contains(category);

    public static bool Glob(string? pattern, string? filename)
    {
        if (string.IsNullOrWhiteSpace(pattern) || string.IsNullOrEmpty(filename)
            || pattern.Length > 256) return false;
        var regex = "^" + Regex.Escape(pattern.Trim()).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
        try { return Regex.IsMatch(filename, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(80)); }
        catch (RegexMatchTimeoutException) { return false; }
        catch (ArgumentException) { return false; }
    }

    public static string? ValidateFolder(string template)
    {
        if (string.IsNullOrWhiteSpace(template)) return "目标目录不能为空";
        if (Path.IsPathRooted(template)) return "目标目录必须在 FreeCam 根目录内";
        var normalized = template.Replace('\\', '/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(s => s is "." or ".." || s.Contains(':')
            || s.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            return "目标目录存在非法字符或越界路径";
        return null;
    }

    public static string ResolveFolder(string template, string root, string feature, string stage, string version)
    {
        var expanded = template.Replace("{Feature}", Safe(feature, "Unknown"), StringComparison.OrdinalIgnoreCase)
            .Replace("{Stage}", Safe(stage, "Unstaged"), StringComparison.OrdinalIgnoreCase)
            .Replace("{Version}", Safe(version, "Unknown"), StringComparison.OrdinalIgnoreCase);
        if (ValidateFolder(expanded) is { } message) throw new InvalidOperationException(message);
        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var target = Path.GetFullPath(Path.Combine(rootFull, expanded.Replace('/', Path.DirectorySeparatorChar)));
        if (!target.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("分类目录不能超出 FreeCam 根目录");
        return Path.GetRelativePath(rootFull, target);
    }

    private static string Safe(string? raw, string fallback)
    {
        var value = string.IsNullOrWhiteSpace(raw) ? fallback : raw.Trim();
        foreach (var ch in Path.GetInvalidFileNameChars()) value = value.Replace(ch, '_');
        return value.Replace('/', '_').Replace('\\', '_').Replace(':', '_');
    }
}
