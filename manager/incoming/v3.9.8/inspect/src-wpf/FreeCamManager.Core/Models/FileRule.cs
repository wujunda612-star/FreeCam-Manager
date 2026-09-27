using System.Text.Json.Serialization;

namespace FreeCamManager.Core.Models;

// Ordered, user-editable rule. For classification MatchBy selects the
// manifest/filename field; launch and drag always match the filename.
public sealed class FileRule
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
    [JsonPropertyName("pattern")] public string Pattern { get; set; } = "";
    [JsonPropertyName("match_by")] public string MatchBy { get; set; } = "文件名";
    [JsonPropertyName("category")] public string Category { get; set; } = "";
    [JsonPropertyName("directory")] public string Directory { get; set; } = "";
    [JsonPropertyName("builtin")] public bool Builtin { get; set; }

    public FileRule Clone() => new()
    {
        Enabled = Enabled, Pattern = Pattern, MatchBy = MatchBy, Category = Category,
        Directory = Directory, Builtin = Builtin
    };
}

public static class RuleDefaults
{
    // Exact Start files precede wildcard Start files, preserving old behavior.
    public static List<FileRule> Launch() =>
    [
        new() { Pattern = "Start.cmd", Builtin = true },
        new() { Pattern = "Start.bat", Builtin = true },
        new() { Pattern = "Start.ps1", Builtin = true },
        new() { Pattern = "Start.exe", Builtin = true },
        new() { Pattern = "Start*.cmd", Builtin = true },
        new() { Pattern = "Start*.bat", Builtin = true },
        new() { Pattern = "Start*.ps1", Builtin = true },
        new() { Pattern = "Start*.exe", Builtin = true }
    ];

    // Deliberately no .log fallback; the user may add one explicitly.
    public static List<FileRule> Drag() =>
    [
        new() { Pattern = "*_Result.zip", Builtin = true },
        new() { Pattern = "*_Result (*.zip", Builtin = true },
        new() { Pattern = "*Result*.zip", Builtin = true }
    ];

    // V3.9.7 built-in classification converted, in original precedence order.
    // Result ZIP imports and Stable protection are independent system behavior.
    public static List<FileRule> Classification() =>
    [
        new() { MatchBy = "文件名", Pattern = "FreeCam_Manager_*", Category = "Manager", Directory = "50_Manager", Builtin = true },
        new() { MatchBy = "文件名", Pattern = "WW底层索引_*", Category = "IndexLibrary", Directory = "60_索引库", Builtin = true },
        new() { MatchBy = "文件名", Pattern = "WW底层索引库_*", Category = "IndexLibrary", Directory = "60_索引库", Builtin = true },
        new() { MatchBy = "分支", Pattern = "feature/*", Category = "Feature", Directory = "20_Feature/{Feature}/{Stage}", Builtin = true },
        new() { MatchBy = "构建类型", Pattern = "Feature", Category = "Feature", Directory = "20_Feature/{Feature}/{Stage}", Builtin = true },
        new() { MatchBy = "分支", Pattern = "experiment/*", Category = "Experiment", Directory = "30_Experiment/{Feature}/{Stage}", Builtin = true },
        new() { MatchBy = "构建类型", Pattern = "Experiment", Category = "Experiment", Directory = "30_Experiment/{Feature}/{Stage}", Builtin = true },
        new() { MatchBy = "构建类型", Pattern = "Probe", Category = "Experiment", Directory = "30_Experiment/{Feature}/{Stage}", Builtin = true },
        new() { MatchBy = "构建类型", Pattern = "Test", Category = "Experiment", Directory = "30_Experiment/{Feature}/{Stage}", Builtin = true },
        new() { MatchBy = "构建类型", Pattern = "Regression", Category = "Experiment", Directory = "30_Experiment/{Feature}/{Stage}", Builtin = true }
    ];
}
