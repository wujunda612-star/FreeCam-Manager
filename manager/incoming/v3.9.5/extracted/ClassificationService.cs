using FreeCamManager.Core.Models;

namespace FreeCamManager.Core.Services;

public enum DevelopmentFilter { All, Feature, Experiment, Test }

public sealed record ClassificationDecision(string Category, string RelativeDirectory, bool NeedsStableConfirmation = false);

public sealed class ClassificationService
{
    public ClassificationDecision Plan(Artifact a)
    {
        var name = (a.Name ?? Path.GetFileName(a.Path ?? "") ?? "").Trim();
        if (name.StartsWith("FreeCam_Manager_", StringComparison.OrdinalIgnoreCase))
            return new("Manager", "50_Manager");
        if (name.StartsWith("WW底层索引_", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("WW底层索引库_", StringComparison.OrdinalIgnoreCase))
            return new("IndexLibrary", "60_索引库");
        if (Eq(a.ArtifactType, "Result"))
            return new("Result", Path.Combine("40_Result", Safe(a.Feature, "Unknown"), Safe(a.Stage, "Unstaged")));
        if (IsStableLike(a))
        {
            var version = Safe(StableVersionResolver.Resolve(a), "Unknown");
            return new("StableCandidate", Path.Combine("90_Unknown", "Stable_Candidate", version), true);
        }
        var branch = (a.Branch ?? "").Trim();
        if (branch.StartsWith("feature/", StringComparison.OrdinalIgnoreCase) || Eq(a.BuildType, "Feature"))
            return new("Feature", Path.Combine("20_Feature", Safe(a.Feature, "Unknown"), Safe(a.Stage, "Unstaged")));
        if (branch.StartsWith("experiment/", StringComparison.OrdinalIgnoreCase) || IsExperimentType(a.BuildType))
            return new("Experiment", Path.Combine("30_Experiment", Safe(a.Feature, "Unknown"), Safe(a.Stage, "Unstaged")));
        return new("Unknown", "90_Unknown");
    }

    public string CategoryLabel(Artifact a)
    {
        var category = (a.Category ?? "").Trim().ToLowerInvariant();
        var buildType = (a.BuildType ?? "").Trim().ToLowerInvariant();
        return category switch
        {
            "feature" => IsTest(a) ? "正式功能 · 测试" : "正式功能",
            "experiment" => buildType switch
            {
                "probe" => "实验 · 探针",
                "test" => "测试",
                "regression" => "回归测试",
                _ => IsTest(a) ? "测试" : "实验"
            },
            "stable" => "稳定版",
            "stablecandidate" => "待确认稳定版",
            "manager" => "管理器",
            "indexlibrary" => "索引库",
            "result" => "测试结果",
            "duplicate" => "重复文件",
            "archive" => "历史归档",
            "unknown" => "未识别",
            _ => buildType switch
            {
                "feature" => "正式功能",
                "experiment" => "实验",
                "probe" => "实验 · 探针",
                "test" => "测试",
                "regression" => "回归测试",
                "stable" => "稳定版",
                _ => "未识别"
            }
        };
    }

    public bool MatchesDevelopmentFilter(Artifact a, DevelopmentFilter filter)
    {
        if (filter == DevelopmentFilter.All) return a.Category is "Feature" or "Experiment";
        var isTest = IsTest(a);
        return filter switch
        {
            DevelopmentFilter.Feature => a.Category == "Feature" && !isTest,
            DevelopmentFilter.Experiment => a.Category == "Experiment" && !isTest,
            DevelopmentFilter.Test => a.Category is "Feature" or "Experiment" && isTest,
            _ => false
        };
    }

    public bool IsManagerArtifact(Artifact a) => (a.Name ?? "").Trim().StartsWith("FreeCam_Manager_", StringComparison.OrdinalIgnoreCase);

    private static bool IsStableLike(Artifact a) => Eq(a.BuildType, "StableCandidate") || Eq(a.BuildType, "Stable") || Eq(a.ReleaseState, "Stable");
    private static bool IsExperimentType(string value) => new[] { "experiment", "probe", "test", "regression" }.Contains((value ?? "").Trim().ToLowerInvariant());
    private static bool IsTest(Artifact a) => Eq(a.BuildType, "Test") || (a.Stage ?? "").Trim().StartsWith("Test", StringComparison.OrdinalIgnoreCase);
    private static bool Eq(string a, string b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string Safe(string value, string fallback)
    {
        var s = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        s = s.Replace('/', '_').Replace('\\', '_').Replace(':', '_');
        return s;
    }
}
