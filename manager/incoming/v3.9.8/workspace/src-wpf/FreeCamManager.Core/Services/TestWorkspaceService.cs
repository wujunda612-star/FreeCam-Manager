namespace FreeCamManager.Core.Services;

public sealed record TestPreparation(string TestingPath, ExtractionStatus ExtractionStatus, string LaunchPath);

public sealed class TestWorkspaceService(ExtractionService extraction)
{
    public Func<FreeCamManager.Core.Models.AppSettings>? Configuration { get; set; }

    private static readonly string[] Extensions = [".cmd", ".bat", ".ps1", ".exe"];

    public async Task<TestPreparation> PrepareAsync(string zipPath, string testingRoot, CancellationToken ct = default)
    {
        if (!File.Exists(zipPath)) throw new FileNotFoundException("原始测试 ZIP 不存在", zipPath);
        var extracted = await extraction.ExtractToTestingAsync(zipPath, testingRoot, ct);
        var settings = Configuration?.Invoke();
        var launchPath = settings is null
            ? FindLaunchEntry(extracted.Destination)
            : FileRuleSelector.FindLaunch(extracted.Destination, settings.LaunchRules,
                settings.LaunchOverrides.GetValueOrDefault(Path.GetFileName(zipPath)));
        return new TestPreparation(extracted.Destination, extracted.Status, launchPath);
    }

    public string FindLaunchEntry(string testingPath)
    {
        if (string.IsNullOrWhiteSpace(testingPath) || !Directory.Exists(testingPath)) return "";
        if (Configuration is { } configure)
            return FileRuleSelector.FindLaunch(testingPath, configure().LaunchRules);

        foreach (var exact in new[] { "Start.cmd", "Start.bat", "Start.ps1", "Start.exe" })
        {
            var candidate = Path.Combine(testingPath, exact);
            if (File.Exists(candidate)) return candidate;
        }

        try
        {
            return Directory.EnumerateFiles(testingPath, "*", SearchOption.AllDirectories)
                .Where(IsLaunchCandidate)
                .OrderBy(path => RelativeDepth(testingPath, path))
                .ThenBy(path => LaunchPriority(Path.GetExtension(path)))
                .ThenBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault() ?? "";
        }
        catch (UnauthorizedAccessException) { return ""; }
        catch (IOException) { return ""; }
    }

    public void DeleteTestingDirectory(string testingPath)
    {
        if (string.IsNullOrWhiteSpace(testingPath) || !Directory.Exists(testingPath)) return;
        Directory.Delete(testingPath, recursive: true);
    }

    private static bool IsLaunchCandidate(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        if (!name.StartsWith("Start", StringComparison.OrdinalIgnoreCase)) return false;
        return Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    }

    private static int RelativeDepth(string root, string path)
    {
        var rel = Path.GetRelativePath(root, path);
        return rel.Count(c => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar);
    }

    private static int LaunchPriority(string extension) => extension.ToLowerInvariant() switch
    {
        ".cmd" => 0,
        ".bat" => 1,
        ".ps1" => 2,
        ".exe" => 3,
        _ => 9
    };
}
