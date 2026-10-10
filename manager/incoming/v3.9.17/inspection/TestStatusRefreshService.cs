namespace FreeCamManager.Core.Services;

public sealed class TestStatusRefreshService
{
    private readonly LibraryService _library;
    private readonly TestResultService _resultService;
    private readonly Func<string> _root;

    public TestStatusRefreshService(LibraryService library, TestResultService resultService, string root = "")
        : this(library, resultService, () => root)
    {
    }

    public TestStatusRefreshService(LibraryService library, TestResultService resultService, Func<string> root)
    {
        _library = library;
        _resultService = resultService;
        _root = root;
    }

    public Task<bool> RefreshAsync(CancellationToken ct = default)
        => RefreshCoreAsync(activeOnly: false, ct);

    // Background polling only needs builds that are actually in "测试中".
    // This restores automatic completion detection without bringing back the old
    // high-frequency full-library evidence scan.
    public Task<bool> RefreshActiveAsync(CancellationToken ct = default)
        => RefreshCoreAsync(activeOnly: true, ct);

    private async Task<bool> RefreshCoreAsync(bool activeOnly, CancellationToken ct)
    {
        var changed = false;
        var items = _library.TestingSnapshot(activeOnly);
        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();
            if (!Directory.Exists(item.TestingPath))
            {
                if (!activeOnly && _library.ClearTestingPath(item.Path)) changed = true;
                continue;
            }

            // A raw log means the test is complete, but it is not necessarily the
            // final deliverable. Full/manual refresh keeps upgrading until a preferred
            // Result ZIP appears. Active polling only cares about ending "测试中".
            if (!activeOnly && item.TestStatus == "已测试" &&
                TestResultService.IsPreferredResultZip(item.ResultPath) &&
                !TestResultService.IsReferenceEvidencePath(item.ResultPath, item.TestingPath))
                continue;

            var evidence = await _resultService.FindEvidenceAsync(item.TestingPath, item, ct);
            if (evidence is null) continue;
            if (_library.SetTestEvidence(item.Path, evidence.Path, _root(), evidence.LastWriteTimeUtc.ToString("O")))
                changed = true;
        }

        if (changed) await _library.SaveAsync(ct);
        return changed;
    }
}
