using FreeCamManager.Core.Models;

namespace FreeCamManager.Core.Services;

public sealed record TestPreparation(string TestingPath, ExtractionStatus ExtractionStatus, string LaunchPath);

public sealed class TestWorkspaceService(ExtractionService extraction, Func<IReadOnlyList<FileRule>>? rules = null)
{
    private readonly Func<IReadOnlyList<FileRule>> _rules = rules ?? (() => RuleDefaults.Launch());

    public async Task<TestPreparation> PrepareAsync(string zipPath, string testingRoot,
        CancellationToken ct = default, string? manualRelativePath = null)
    {
        if (!File.Exists(zipPath)) throw new FileNotFoundException("原始测试 ZIP 不存在", zipPath);
        var extracted = await extraction.ExtractToTestingAsync(zipPath, testingRoot, ct);
        var launchPath = FindLaunchEntry(extracted.Destination, manualRelativePath);
        return new TestPreparation(extracted.Destination, extracted.Status, launchPath);
    }

    public string FindLaunchEntry(string testingPath, string? manualRelativePath = null)
        => FileRuleEngine.FindLaunch(testingPath, _rules(), manualRelativePath);

    public void DeleteTestingDirectory(string testingPath)
    {
        if (string.IsNullOrWhiteSpace(testingPath) || !Directory.Exists(testingPath)) return;
        Directory.Delete(testingPath, recursive: true);
    }
}
