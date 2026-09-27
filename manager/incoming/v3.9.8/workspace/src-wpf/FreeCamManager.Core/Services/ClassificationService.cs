using FreeCamManager.Core.Models;

namespace FreeCamManager.Core.Services;

public enum DevelopmentFilter { All, Feature, Experiment, Test }

public sealed record ClassificationDecision(string Category, string RelativeDirectory, bool NeedsStableConfirmation = false);

public sealed class ClassificationService
{
    private readonly Func<IReadOnlyList<ClassificationRule>> _rules;
    public ClassificationService(Func<IReadOnlyList<ClassificationRule>>? rules = null)
        => _rules = rules ?? (() => RuleDefaults.Classification());

    public ClassificationDecision Plan(Artifact a)
    {
        var name = (a.Name ?? Path.GetFileName(a.Path ?? "") ?? "").Trim();
        // Result packages remain a system pairing concern, not a user category
        // rule. Preserve legacy 40_Result behavior for external Result imports.
        if (Eq(a.ArtifactType, "Result"))
            return new("Result", Path.Combine("40_Result", Safe(a.Feature, "Unknown"), Safe(a.Stage, "Unstaged")));

        // User filename globs cannot defeat the existing Stable freeze gate.
        var stable = IsStableLike(a);
        foreach (var rule in _rules())
        {
            if (!rule.Enabled || string.IsNullOrWhiteSpace(rule.Category)
                || !Matches(rule, a, name)) continue;
            if (stable && !Eq(rule.Category, "StableCandidate")) continue;
            try
            {
                var dir = RuleMatcher.ResolveFolder(rule.Folder,
                    Path.Combine(Path.GetTempPath(), "FreeCam_Classifier_Rules"),
                    a.Feature, a.Stage, StableVersionResolver.Resolve(a));
                return new(rule.Category.Trim(), dir, Eq(rule.Category, "StableCandidate"));
            }
            catch (InvalidOperationException) { continue; }
        }

        if (stable)
        {
            var version = Safe(StableVersionResolver.Resolve(a), "Unknown");
            return new("StableCandidate", Path.Combine("90_Unknown", "Stable_Candidate", version), true);
        }
        return new("Unknown", "90_Unknown");
    }

    public static bool Matches(ClassificationRule rule, Artifact a, string? filename = null)
    {
        var name = filename ?? (a.Name ?? Path.GetFileName(a.Path ?? ""));
        var value = rule.Field switch
        {
            "FileName" => name,
            "ArtifactType" => a.ArtifactType,
            "BuildType" => a.BuildType,
            "Branch" => a.Branch,
            "ReleaseState" => a.ReleaseState,
            "StableLike" => IsStableLike(a) ? "true" : "",
            "FeatureLike" => (a.Branch ?? "").StartsWith("feature/", StringComparison.OrdinalIgnoreCase)
                || Eq(a.BuildType, "Feature") ? "true" : "",
            "ExperimentLike" => (a.Branch ?? "").StartsWith("experiment/", StringComparison.OrdinalIgnoreCase)
                || IsExperimentType(a.BuildType) ? "true" : "",
            _ => ""
        };
        return RuleMatcher.Glob(rule.Pattern, value);
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
            _ when !string.IsNullOrWhiteSpace(a.Category) && !RuleMatcher.IsBuiltInCategory(a.Category) => a.Category,
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
