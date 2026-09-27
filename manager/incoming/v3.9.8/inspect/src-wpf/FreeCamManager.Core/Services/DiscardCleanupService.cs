using FreeCamManager.Core.Models;

namespace FreeCamManager.Core.Services;

public sealed record DiscardCleanupResult(int Deleted, int SkippedProtected, int Failed);

public sealed class DiscardCleanupService
{
    private readonly LibraryService _library;
    private readonly OrganizerService _organizer;
    private readonly TestResultService _results;
    private readonly Func<string> _root;
    private readonly Func<string> _resultRoot;

    public DiscardCleanupService(
        LibraryService library,
        OrganizerService organizer,
        TestResultService results,
        Func<string> root,
        Func<string> resultRoot)
    {
        _library = library;
        _organizer = organizer;
        _results = results;
        _root = root;
        _resultRoot = resultRoot;
    }

    public async Task<DiscardCleanupResult> CleanupDueAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        var deleted = 0;
        var skippedProtected = 0;
        var failed = 0;

        foreach (var item in _library.Snapshot())
        {
            ct.ThrowIfCancellationRequested();
            if (!string.Equals(item.ManualStatus, "已废弃", StringComparison.Ordinal)) continue;
            if (!DateTimeOffset.TryParse(item.AutoDeleteAt, out var dueAt) || dueAt > now) continue;
            if (item.Protected)
            {
                skippedProtected++;
                continue;
            }

            try
            {
                var testingPath = ResolveTestingPath(item);
                if (!string.IsNullOrWhiteSpace(testingPath) && Directory.Exists(testingPath))
                {
                    var preserved = await _results.PreserveEvidenceAsync(testingPath, item, _resultRoot(), ct).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(preserved))
                        _library.SetTestEvidence(item.Path, preserved, _root(), item.LastTestedAt);
                    Directory.Delete(testingPath, true);
                    _library.ClearTestingPath(item.Path);
                }

                await _organizer.DeleteAsync(item.Path, ct).ConfigureAwait(false);
                deleted++;
            }
            catch (FileNotFoundException)
            {
                _library.RemoveByPath(item.Path);
                await _library.SaveAsync(ct).ConfigureAwait(false);
                deleted++;
            }
            catch
            {
                failed++;
            }
        }

        return new DiscardCleanupResult(deleted, skippedProtected, failed);
    }

    private string ResolveTestingPath(Artifact item)
    {
        if (!string.IsNullOrWhiteSpace(item.TestingPath) && Directory.Exists(item.TestingPath))
            return item.TestingPath;
        if (!string.IsNullOrWhiteSpace(item.TestingRelativePath))
        {
            var rebased = Path.Combine(_root(), item.TestingRelativePath);
            if (Directory.Exists(rebased)) return rebased;
        }
        var derived = Path.Combine(_root(), "01_Testing", Path.GetFileNameWithoutExtension(item.Path));
        return Directory.Exists(derived) ? derived : "";
    }
}
