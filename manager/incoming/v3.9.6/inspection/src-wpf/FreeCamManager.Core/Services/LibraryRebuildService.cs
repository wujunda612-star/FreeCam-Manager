using FreeCamManager.Core.Models;

namespace FreeCamManager.Core.Services;

public sealed record LibraryRebuildResult(int Added, int TestingLinked, int ResultsPaired, int Failed)
{
    public bool Changed => Added > 0 || TestingLinked > 0 || ResultsPaired > 0;
}

public sealed class LibraryRebuildService(
    LibraryService library,
    ManifestService manifest,
    HashService hash,
    IAppLogger? log = null)
{
    private static readonly string[] ManagedRoots =
        new[] { "10_Stable", "20_Feature", "30_Experiment", "40_Result", "50_Manager", "60_索引库", "80_Archive", "90_Unknown" };

    public async Task<LibraryRebuildResult> ReconcileAsync(string root, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return new LibraryRebuildResult(0, 0, 0, 0);

        var added = 0;
        var linked = 0;
        var failed = 0;

        foreach (var managedRoot in ManagedRoots)
        {
            var directory = Path.Combine(root, managedRoot);
            if (!Directory.Exists(directory)) continue;

            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).ToList(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed++;
                log?.Event("LIBRARY_REBUILD_ENUM_ERROR", ("directory", directory), ("error", ex.Message));
                continue;
            }

            foreach (var path in files)
            {
                ct.ThrowIfCancellationRequested();
                if (!ShouldIndex(path, managedRoot)) continue;
                if (library.ByPath(path) is not null) continue;

                try
                {
                    Artifact artifact;
                    try { artifact = await manifest.InspectAsync(path, ct).ConfigureAwait(false); }
                    catch
                    {
                        artifact = manifest.InspectFilename(Path.GetFileName(path));
                        artifact.Path = path;
                        artifact.Name = Path.GetFileName(path);
                    }

                    var info = new FileInfo(path);
                    artifact.Path = path;
                    artifact.Name = Path.GetFileName(path);
                    artifact.RelativePath = PathRebaseService.TryMakeRelative(root, path);
                    artifact.Category = CategoryFromPath(managedRoot, artifact.RelativePath);
                    artifact.Size = info.Exists ? info.Length : 0;
                    artifact.ImportedAt = info.Exists
                        ? new DateTimeOffset(info.LastWriteTimeUtc).ToString("O")
                        : DateTimeOffset.Now.ToString("O");

                    try { artifact.Sha256 = await hash.FileSha256Async(path, ct).ConfigureAwait(false); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        log?.Event("LIBRARY_REBUILD_HASH_SKIPPED", ("path", path), ("error", ex.Message));
                    }

                    ApplyCategoryDefaults(artifact);
                    library.Upsert(artifact);
                    added++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failed++;
                    log?.Event("LIBRARY_REBUILD_FILE_ERROR", ("path", path), ("error", ex.Message));
                }
            }
        }

        foreach (var build in library.Snapshot().Where(x => x.Category is "Feature" or "Experiment"))
        {
            ct.ThrowIfCancellationRequested();
            if (!string.IsNullOrWhiteSpace(build.TestingPath) && Directory.Exists(build.TestingPath)) continue;
            var testing = Path.Combine(root, "01_Testing", Path.GetFileNameWithoutExtension(build.Path));
            if (!Directory.Exists(testing)) continue;
            if (library.SetTestingPath(build.Path, testing, root)) linked++;
        }

        var beforePairs = library.Snapshot()
            .Where(x => x.ArtifactType != "Result")
            .ToDictionary(x => x.Path, x => x.ResultPath, StringComparer.OrdinalIgnoreCase);
        library.PairResults();
        var paired = library.Snapshot()
            .Where(x => x.ArtifactType != "Result")
            .Count(x => beforePairs.TryGetValue(x.Path, out var before)
                && !string.Equals(before, x.ResultPath, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(x.ResultPath));

        var result = new LibraryRebuildResult(added, linked, paired, failed);
        if (result.Changed) await library.SaveAsync(ct).ConfigureAwait(false);
        log?.Event("LIBRARY_REBUILD",
            ("root", root), ("added", added), ("testing_linked", linked), ("results_paired", paired), ("failed", failed));
        return result;
    }

    private static bool ShouldIndex(string path, string managedRoot)
    {
        var extension = Path.GetExtension(path);
        if (extension.Equals(".zip", StringComparison.OrdinalIgnoreCase)) return true;
        if (extension.Equals(".bundle", StringComparison.OrdinalIgnoreCase)) return true;
        return managedRoot == "10_Stable"
            && extension.Equals(".txt", StringComparison.OrdinalIgnoreCase)
            && Path.GetFileName(path).Contains("SHA256", StringComparison.OrdinalIgnoreCase);
    }

    private static string CategoryFromPath(string managedRoot, string relativePath)
    {
        return managedRoot switch
        {
            "10_Stable" => "Stable",
            "20_Feature" => "Feature",
            "30_Experiment" => "Experiment",
            "40_Result" => "Result",
            "50_Manager" => "Manager",
            "60_索引库" => "IndexLibrary",
            "80_Archive" when relativePath.Contains(
                Path.Combine("80_Archive", "Duplicates"), StringComparison.OrdinalIgnoreCase) => "Duplicate",
            "80_Archive" => "Archive",
            "90_Unknown" when relativePath.Contains(
                Path.Combine("90_Unknown", "Stable_Candidate"), StringComparison.OrdinalIgnoreCase) => "StableCandidate",
            _ => "Unknown"
        };
    }

    private static void ApplyCategoryDefaults(Artifact artifact)
    {
        switch (artifact.Category)
        {
            case "Stable":
                artifact.BuildType = "Stable";
                artifact.ReleaseState = "Stable";
                artifact.Status = "已冻结";
                artifact.Protected = true;
                if (artifact.Name.Contains("SHA256", StringComparison.OrdinalIgnoreCase))
                    artifact.ArtifactType = "SHA256";
                break;
            case "Feature":
            case "Experiment":
                if (string.IsNullOrWhiteSpace(artifact.TestStatus)) artifact.TestStatus = "待测试";
                break;
            case "Result":
                artifact.ArtifactType = "Result";
                break;
            case "StableCandidate":
                artifact.Status = "待确认 Stable（稳定版）";
                break;
            case "Duplicate":
                artifact.Status = "重复文件";
                break;
        }
    }
}
