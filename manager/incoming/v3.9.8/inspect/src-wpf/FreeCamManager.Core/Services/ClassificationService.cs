using FreeCamManager.Core.Models;

namespace FreeCamManager.Core.Services;

public enum DevelopmentFilter { All, Feature, Experiment, Test }

public sealed record ClassificationDecision(string Category, string RelativeDirectory, bool NeedsStableConfirmation = false);

public sealed class ClassificationService
{
    private readonly Func<IReadOnlyList<FileRule>> _rules;

    public ClassificationService(Func<IReadOnlyList<FileRule>>? rules = null)
    {
        _rules = rules ?? (() => RuleDefaults.Classification());
    }

    public IReadOnlyList<FileRule> ConfiguredRules => _rules();

    public ClassificationDecision Plan(Artifact a)
    {
        ClassificationDecision Result() =>
            new("Result", Path.Combine("40_Result", Safe(a.Feature, "Unknown"), Safe(a.Stage, "Unstaged")));
        ClassificationDecision StableCandidate()
        {
            var version = Safe(StableVersionResolver.Resolve(a), "Unknown");
            return new("StableCandidate", Path.Combine("90_Unknown", "Stable_Candidate", version), true);
        }

        // Keep the original priority: Manager and index package name rules are
        // evaluated before Stable; Result routing and Stable freeze still
        // override ordinary feature/experiment/custom file rules.
        foreach (var rule in ConfiguredRules)
        {
            if (!FileRuleEngine.MatchesClassification(rule, a)) continue;
            var category = FileRuleEngine.PersistedCategory(rule.Category);
            if (category is not "Manager" and not "IndexLibrary")
            {
                if (Eq(a.ArtifactType, "Result")) return Result();
                if (IsStableLike(a)) return StableCandidate();
            }
            return new(category, FileRuleEngine.FormatDirectory(rule.Directory, a));
        }
        if (Eq(a.ArtifactType, "Result")) return Result();
        if (IsStableLike(a)) return StableCandidate();
        return new("Unknown", "90_Unknown");
    }

    public IEnumerable<string> AdditionalManagedRoots()
    {
        foreach (var rule in ConfiguredRules.Where(r => r.Enabled))
        {
            var category = FileRuleEngine.PersistedCategory(rule.Category);
            if (!FileRuleEngine.IsCustomCategory(category)) continue;
            var first = rule.Directory.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(first) && !first.Contains('{') && first != "." && first != "..")
                yield return first;
        }
    }

    // Called only when rebuilding a missing SQLite entry. Existing entries
    // keep their original Category when rules are edited.
    public string? CustomCategoryFromRelativePath(string relative)
    {
        var normalized = relative.Replace('\\', '/');
        foreach (var rule in ConfiguredRules.Where(r => r.Enabled))
        {
            var category = FileRuleEngine.PersistedCategory(rule.Category);
            if (!FileRuleEngine.IsCustomCategory(category)) continue;
            var prefix = rule.Directory.Replace('\\', '/').Split('{')[0].TrimEnd('/');
            if (!string.IsNullOrWhiteSpace(prefix) &&
                (normalized.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase)
                 || normalized.Equals(prefix, StringComparison.OrdinalIgnoreCase)))
                return category;
        }
        return null;
    }

    public string CategoryLabel(Artifact a)
    {
        var category = (a.Category ?? "").Trim().ToLowerInvariant();
        var buildType = (a.BuildType ?? "").Trim().ToLowerInvariant();
        if (FileRuleEngine.IsCustomCategory(a.Category ?? "")) return (a.Category ?? "")["Custom:".Length..];
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
