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

    public async Task<bool> RefreshAsync(CancellationToken ct = default)
    {
        var changed = false;
        foreach (var item in _library.Snapshot())
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(item.TestingPath)) continue;
            if (!Directory.Exists(item.TestingPath))
            {
                if (_library.ClearTestingPath(item.Path)) changed = true;
                continue;
            }
            // A raw log means the test is complete, but it is not necessarily the
            // final deliverable. Keep rescanning until a preferred Result ZIP appears.
            if (item.TestStatus == "已测试" && TestResultService.IsPreferredResultZip(item.ResultPath) &&
                !TestResultService.IsReferenceEvidencePath(item.ResultPath, item.TestingPath)) continue;
            var evidence = await _resultService.FindEvidenceAsync(item.TestingPath, item, ct);
            if (evidence is null) continue;
            if (_library.SetTestEvidence(item.Path, evidence.Path, _root(), evidence.LastWriteTimeUtc.ToString("O"))) changed = true;
        }
        if (changed) await _library.SaveAsync(ct);
        return changed;
    }
}
