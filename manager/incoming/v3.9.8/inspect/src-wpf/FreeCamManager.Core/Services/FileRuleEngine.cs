using System.IO.Compression;
using System.IO.Enumeration;
using System.Text.Json;
using FreeCamManager.Core.Models;

namespace FreeCamManager.Core.Services;

public static class FileRuleEngine
{
    private static readonly string[] LaunchExtensions = [".cmd", ".bat", ".ps1", ".exe"];

    public static bool Matches(string pattern, string? value)
        => !string.IsNullOrWhiteSpace(pattern)
           && FileSystemName.MatchesSimpleExpression(pattern.Trim(), value ?? "", ignoreCase: true);

    public static bool MatchesClassification(FileRule rule, Artifact build)
    {
        if (!rule.Enabled || string.IsNullOrWhiteSpace(rule.Pattern)) return false;
        var value = rule.MatchBy switch
        {
            "构建类型" => build.BuildType,
            "分支" => build.Branch,
            "产物类型" => build.ArtifactType,
            "发布状态" => build.ReleaseState,
            _ => build.Name
        };
        return Matches(rule.Pattern, value);
    }

    public static string FormatDirectory(string template, Artifact build)
    {
        var value = (template ?? "").Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value) || value.StartsWith('/'))
            throw new InvalidDataException("归档目录必须填写 FreeCam 根目录下的相对目录");
        value = value.Replace("{Feature}", Safe(build.Feature, "Unknown"), StringComparison.OrdinalIgnoreCase)
                     .Replace("{Stage}", Safe(build.Stage, "Unstaged"), StringComparison.OrdinalIgnoreCase)
                     .Replace("{Version}", Safe(StableVersionResolver.Resolve(build), "Unknown"), StringComparison.OrdinalIgnoreCase);
        var segments = value.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(s => s is "." or ".." || s.Contains(':') || s.Contains('{') || s.Contains('}') ||
            s.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            throw new InvalidDataException("归档目录含非法路径或未知占位符，只允许 {Feature}、{Stage}、{Version}");
        return Path.Combine(segments);
    }

    public static void Validate(IReadOnlyList<FileRule> rules, string kind)
    {
        if (rules.Count > 100) throw new InvalidDataException(kind + "最多支持 100 条规则");
        foreach (var rule in rules)
        {
            if (!rule.Enabled) continue;
            if (string.IsNullOrWhiteSpace(rule.Pattern) || rule.Pattern.IndexOfAny(['/', '\\']) >= 0)
                throw new InvalidDataException(kind + "：匹配条件不能为空，也不能包含目录分隔符");
            if (kind == "分类")
            {
                if (string.IsNullOrWhiteSpace(rule.Category))
                    throw new InvalidDataException("分类规则缺少分类名称");
                if (!new[] { "文件名", "构建类型", "分支", "产物类型", "发布状态" }.Contains(rule.MatchBy))
                    throw new InvalidDataException("无效的分类匹配字段：" + rule.MatchBy);
                _ = FormatDirectory(rule.Directory, new Artifact { Feature = "Feature", Stage = "Test1", Version = "1" });
            }
        }
    }

    public static bool IsCustomCategory(string category)
        => category.StartsWith("Custom:", StringComparison.OrdinalIgnoreCase);

    public static string PersistedCategory(string category)
    {
        if (category is "Manager" or "IndexLibrary" or "Feature" or "Experiment" or "Stable" or "StableCandidate"
            or "Result" or "Duplicate" or "Archive" or "Unknown") return category;
        return "Custom:" + category.Trim();
    }

    public static bool IsInside(string path, string directory)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(directory)) return false;
        try
        {
            var rel = Path.GetRelativePath(directory, path);
            return !Path.IsPathRooted(rel) && rel != ".."
                && !rel.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && rel != ".";
        }
        catch { return false; }
    }

    public static string FindLaunch(string testingPath, IReadOnlyList<FileRule> rules, string? manualRelativePath = null)
    {
        if (string.IsNullOrWhiteSpace(testingPath) || !Directory.Exists(testingPath)) return "";
        if (!string.IsNullOrWhiteSpace(manualRelativePath))
        {
            var specified = Path.GetFullPath(Path.Combine(testingPath, manualRelativePath));
            if (IsInside(specified, testingPath) && File.Exists(specified) &&
                LaunchExtensions.Contains(Path.GetExtension(specified), StringComparer.OrdinalIgnoreCase))
                return specified;
        }

        string[] candidates;
        try
        {
            candidates = Directory.EnumerateFiles(testingPath, "*", SearchOption.AllDirectories)
                .Where(p => LaunchExtensions.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase))
                .Where(p => !TestResultService.IsReferenceEvidencePath(p, testingPath))
                .OrderBy(p => Depth(testingPath, p))
                .ThenBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return ""; }

        foreach (var rule in rules.Where(r => r.Enabled && !string.IsNullOrWhiteSpace(r.Pattern)))
        {
            var chosen = candidates.FirstOrDefault(p => Matches(rule.Pattern, Path.GetFileName(p)));
            if (chosen is not null) return chosen;
        }
        return "";
    }

    // Ordered custom rules exclusively control drag. Never substitute a .log
    // merely because the library recorded one earlier.
    public static string FindDrag(Artifact build, string testingPath, string resultRoot,
        IReadOnlyList<FileRule> rules, string? manualPath = null)
    {
        if (!string.IsNullOrWhiteSpace(manualPath) && File.Exists(manualPath) &&
            (IsInside(manualPath, testingPath) || IsInside(manualPath, resultRoot)))
            return manualPath;

        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(testingPath) && Directory.Exists(testingPath))
        {
            try
            {
                candidates.AddRange(Directory.EnumerateFiles(testingPath, "*", SearchOption.AllDirectories)
                    .Where(p => !TestResultService.IsReferenceEvidencePath(p, testingPath)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        // A linked 40_Result file is safe to offer to the rule engine.
        if (File.Exists(build.ResultPath) && IsInside(build.ResultPath, resultRoot))
            candidates.Add(build.ResultPath);

        var sorted = candidates.Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(p => !HasConflictingForBuildId(p, build.BuildId))
            .OrderBy(p => DragDirectoryRank(p, testingPath))
            .ThenBy(p => ResultNameRank(p, build.Name))
            .ThenByDescending(File.GetLastWriteTimeUtc)
            .ToArray();
        foreach (var rule in rules.Where(r => r.Enabled && !string.IsNullOrWhiteSpace(r.Pattern)))
        {
            var chosen = sorted.FirstOrDefault(p => Matches(rule.Pattern, Path.GetFileName(p)));
            if (chosen is not null) return chosen;
        }
        return "";
    }

    private static int DragDirectoryRank(string path, string testingPath)
    {
        if (!IsInside(path, testingPath)) return 4;
        var rel = Path.GetRelativePath(testingPath, path).Replace('\\', '/');
        if (rel.StartsWith("Results/", StringComparison.OrdinalIgnoreCase) && rel.Count(c => c == '/') == 1) return 0;
        if (!rel.Contains('/')) return 1;
        if (rel.StartsWith("Logs/", StringComparison.OrdinalIgnoreCase)) return 2;
        return 3;
    }

    private static int ResultNameRank(string path, string buildName)
    {
        var expected = Path.GetFileNameWithoutExtension(buildName).Replace("_Result", "", StringComparison.OrdinalIgnoreCase);
        var actual = Path.GetFileNameWithoutExtension(path).Replace("_Result", "", StringComparison.OrdinalIgnoreCase);
        if (string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)) return 0;
        static string NoPhase(string s) => string.Join("_", s.Split('_').Where(x =>
            !(x.StartsWith("Phase", StringComparison.OrdinalIgnoreCase) && x.Length > 5 && x[5..].All(char.IsDigit))));
        return string.Equals(NoPhase(actual), NoPhase(expected), StringComparison.OrdinalIgnoreCase) ? 1 : 2;
    }

    private static bool HasConflictingForBuildId(string path, string buildId)
    {
        if (string.IsNullOrWhiteSpace(buildId) || !path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            using var zip = ZipFile.OpenRead(path);
            var entry = zip.Entries.FirstOrDefault(x => Path.GetFileName(x.FullName).Equals("RESULT_MANIFEST.json", StringComparison.OrdinalIgnoreCase));
            if (entry is null || entry.Length > 1024 * 1024) return false;
            using var stream = entry.Open();
            using var document = JsonDocument.Parse(stream);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!property.Name.Equals("ForBuildId", StringComparison.OrdinalIgnoreCase)) continue;
                var actual = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : "";
                return !string.IsNullOrWhiteSpace(actual)
                    && !string.Equals(actual, buildId, StringComparison.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException) { }
        return false;
    }

    private static int Depth(string root, string path)
        => Path.GetRelativePath(root, path).Count(c => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar);

    private static string Safe(string value, string fallback)
    {
        var s = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s.Replace('/', '_').Replace('\\', '_').Replace(':', '_');
    }
}
