using FreeCamManager.Core.Models;

namespace FreeCamManager.Core.Services;

public sealed record TestEvidence(string Path, string Kind, DateTime LastWriteTimeUtc);

public sealed class TestResultService(ManifestService manifestService)
{
    public async Task<(bool Found, string ResultPath)> HasMatchingResultAsync(string testingPath, Artifact build, CancellationToken ct = default)
    {
        var evidence = await FindEvidenceAsync(testingPath, build, ct);
        return evidence is null ? (false, "") : (true, evidence.Path);
    }

    public string ResolvePreferredDragPath(Artifact build, string testingPath, string resultRoot)
    {
        var expectedName = ExpectedResultFileName(build);

        // Current test workspace always wins. In particular, never let a stale
        // Validation/Prior_* or *_Evidence_* package stored in ResultPath beat
        // the output that the build just produced under its own Results/Logs.
        var currentResult = FindCurrentWorkspaceResultZip(testingPath, expectedName);
        if (!string.IsNullOrWhiteSpace(currentResult)) return currentResult;

        var stored = build.ResultPath;
        if (IsPreferredResultZip(stored) && IsInsideTestingWorkspace(stored!, testingPath) && !IsExcludedEvidencePath(stored!, testingPath))
            return stored;

        // A completed matching Result ZIP is the canonical deliverable. It must
        // upgrade/beat a raw log that may have been discovered a moment earlier.
        var expected = FindExpectedResultZip(resultRoot, expectedName);
        if (!string.IsNullOrWhiteSpace(expected)) return expected;

        var currentRaw = FindCurrentWorkspaceRawEvidence(testingPath, build);
        if (!string.IsNullOrWhiteSpace(currentRaw)) return currentRaw;

        if (IsPreferredResultZip(stored) && !IsExcludedEvidencePath(stored!, testingPath)) return stored;

        var resultRootRaw = FindMatchingRawEvidence(resultRoot, build);
        if (!string.IsNullOrWhiteSpace(resultRootRaw)) return resultRootRaw;

        if (IsSafeRawEvidence(stored) && !IsExcludedEvidencePath(stored!, testingPath)) return stored;
        return "";
    }

    public async Task<TestEvidence?> FindEvidenceAsync(string testingPath, Artifact build, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(testingPath) || !Directory.Exists(testingPath)) return null;

        var resultsDir = Path.Combine(testingPath, "Results");
        if (Directory.Exists(resultsDir))
        {
            foreach (var path in Directory.EnumerateFiles(resultsDir, "*.zip", SearchOption.TopDirectoryOnly)
                         .Where(path => IsFresh(path, build))
                         .OrderByDescending(File.GetLastWriteTimeUtc))
            {
                ct.ThrowIfCancellationRequested();
                if (await IsMatchingResultZipAsync(path, build, ct))
                    return new TestEvidence(path, "ResultZip", File.GetLastWriteTimeUtc(path));
            }

        }

        foreach (var path in EnumerateEligibleFiles(testingPath, "*.zip")
                     .Where(path => Path.GetFileName(path).Contains("result", StringComparison.OrdinalIgnoreCase))
                     .Where(path => IsFresh(path, build))
                     .OrderBy(path => WorkspaceEvidenceRank(path, testingPath))
                     .ThenByDescending(File.GetLastWriteTimeUtc))
        {
            ct.ThrowIfCancellationRequested();
            if (await IsMatchingResultZipAsync(path, build, ct))
                return new TestEvidence(path, "ResultZip", File.GetLastWriteTimeUtc(path));
        }

        if (Directory.Exists(resultsDir))
        {
            var resultFile = Directory.EnumerateFiles(resultsDir, "*", SearchOption.TopDirectoryOnly)
                .Where(IsRawEvidence)
                .Where(path => IsFresh(path, build))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(resultFile))
                return new TestEvidence(resultFile, "Log", File.GetLastWriteTimeUtc(resultFile));
        }

        var log = EnumerateEligibleFiles(testingPath, "*.log")
            .Where(path => IsFresh(path, build))
            .OrderBy(path => WorkspaceEvidenceRank(path, testingPath))
            .ThenByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
        return string.IsNullOrWhiteSpace(log) ? null : new TestEvidence(log, "Log", File.GetLastWriteTimeUtc(log));
    }

    public async Task<string> PreserveEvidenceAsync(string testingPath, Artifact build, string resultRoot, CancellationToken ct = default)
    {
        var evidence = await FindEvidenceAsync(testingPath, build, ct);
        if (evidence is null || !File.Exists(evidence.Path)) return "";

        var evidenceFull = Path.GetFullPath(evidence.Path);
        var testingFull = Path.GetFullPath(testingPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!evidenceFull.StartsWith(testingFull, StringComparison.OrdinalIgnoreCase)) return evidence.Path;

        var feature = Safe(build.Feature, "Unknown");
        var stage = Safe(build.Stage, "Unstaged");
        var directory = Path.Combine(resultRoot, feature, stage);
        Directory.CreateDirectory(directory);
        var destination = UniquePath(Path.Combine(directory, Path.GetFileName(evidence.Path)));
        await using var input = new FileStream(evidence.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 128 * 1024, true);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, true);
        await input.CopyToAsync(output, 128 * 1024, ct);
        return destination;
    }

    public static string ExpectedResultFileName(Artifact build)
    {
        var name = !string.IsNullOrWhiteSpace(build.Name) ? build.Name : Path.GetFileName(build.Path);
        if (string.IsNullOrWhiteSpace(name)) return "";
        var stem = Path.GetFileNameWithoutExtension(name);
        if (stem.EndsWith("_Result", StringComparison.OrdinalIgnoreCase)) return stem + ".zip";
        return stem + "_Result.zip";
    }

    public static bool IsPreferredResultZip(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
        if (!Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase)) return false;
        var stem = Path.GetFileNameWithoutExtension(path);
        return stem.EndsWith("_Result", StringComparison.OrdinalIgnoreCase) ||
               stem.Contains("_Result (", StringComparison.OrdinalIgnoreCase);
    }


    private static string? FindCurrentWorkspaceResultZip(string testingPath, string expectedName)
    {
        if (string.IsNullOrWhiteSpace(testingPath) || string.IsNullOrWhiteSpace(expectedName) || !Directory.Exists(testingPath)) return null;

        var resultsDir = Path.Combine(testingPath, "Results");
        var preferredRoots = new[] { resultsDir, testingPath };
        foreach (var root in preferredRoots.Where(Directory.Exists))
        {
            var exact = Directory.EnumerateFiles(root, expectedName, SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(exact)) return exact;

            var expectedStem = Path.GetFileNameWithoutExtension(expectedName);
            var numbered = Directory.EnumerateFiles(root, expectedStem + "*.zip", SearchOption.TopDirectoryOnly)
                .Where(path => Path.GetFileNameWithoutExtension(path).StartsWith(expectedStem + " (", StringComparison.OrdinalIgnoreCase))
                .Where(IsPreferredResultZip)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(numbered)) return numbered;
        }

        var recursiveExact = EnumerateEligibleFiles(testingPath, expectedName)
            .OrderBy(path => WorkspaceEvidenceRank(path, testingPath))
            .ThenByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(recursiveExact)) return recursiveExact;

        var stem = Path.GetFileNameWithoutExtension(expectedName);
        return EnumerateEligibleFiles(testingPath, stem + "*.zip")
            .Where(path => Path.GetFileNameWithoutExtension(path).StartsWith(stem + " (", StringComparison.OrdinalIgnoreCase))
            .Where(IsPreferredResultZip)
            .OrderBy(path => WorkspaceEvidenceRank(path, testingPath))
            .ThenByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static string? FindCurrentWorkspaceRawEvidence(string testingPath, Artifact build)
    {
        if (string.IsNullOrWhiteSpace(testingPath) || !Directory.Exists(testingPath)) return null;
        var stem = Path.GetFileNameWithoutExtension(!string.IsNullOrWhiteSpace(build.Name) ? build.Name : build.Path);

        var all = EnumerateEligibleFiles(testingPath, "*")
            .Where(IsRawEvidence)
            .OrderBy(path => WorkspaceEvidenceRank(path, testingPath))
            .ThenByDescending(File.GetLastWriteTimeUtc)
            .ToList();

        if (!string.IsNullOrWhiteSpace(stem))
        {
            var matching = all.FirstOrDefault(path => Path.GetFileNameWithoutExtension(path).Contains(stem, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(matching)) return matching;
        }
        return all.FirstOrDefault();
    }

    private static IEnumerable<string> EnumerateEligibleFiles(string testingPath, string pattern)
    {
        if (string.IsNullOrWhiteSpace(testingPath) || !Directory.Exists(testingPath)) return [];
        try
        {
            return Directory.EnumerateFiles(testingPath, pattern, SearchOption.AllDirectories)
                .Where(path => !IsExcludedEvidencePath(path, testingPath))
                .ToArray();
        }
        catch (UnauthorizedAccessException) { return []; }
        catch (IOException) { return []; }
    }

    private static int WorkspaceEvidenceRank(string path, string testingPath)
    {
        try
        {
            var relative = Path.GetRelativePath(testingPath, path);
            var segments = relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length <= 1) return 0;
            if (segments.Length == 2 && segments[0].Equals("Results", StringComparison.OrdinalIgnoreCase)) return 0;
            if (segments[0].Equals("Logs", StringComparison.OrdinalIgnoreCase)) return 1;
            if (segments[0].Equals("Results", StringComparison.OrdinalIgnoreCase)) return 2;
            return 3;
        }
        catch { return 4; }
    }

    private static bool IsInsideTestingWorkspace(string path, string testingPath)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(testingPath)) return false;
        try
        {
            var relative = Path.GetRelativePath(testingPath, path);
            return !relative.Equals("..", StringComparison.Ordinal) &&
                   !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
                   !Path.IsPathRooted(relative);
        }
        catch { return false; }
    }

    public static bool IsReferenceEvidencePath(string? path, string testingPath)
        => !string.IsNullOrWhiteSpace(path) && IsExcludedEvidencePath(path, testingPath);

    private static bool IsExcludedEvidencePath(string path, string testingPath)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(testingPath)) return false;
        try
        {
            var relative = Path.GetRelativePath(testingPath, path);
            if (relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                relative.Equals("..", StringComparison.Ordinal)) return false;

            var segments = relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < Math.Max(0, segments.Length - 1); i++)
            {
                var normalized = segments[i].Replace('-', '_').Replace(' ', '_');
                if (normalized.Contains("evidence", StringComparison.OrdinalIgnoreCase) ||
                    normalized.StartsWith("prior", StringComparison.OrdinalIgnoreCase) ||
                    normalized.Contains("reference", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
        catch { return false; }
    }

    private static string FindExpectedResultZip(string root, string expectedName)
    {
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(expectedName) || !Directory.Exists(root)) return "";
        try
        {
            var exact = Directory.EnumerateFiles(root, expectedName, SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(exact)) return exact;

            var expectedStem = Path.GetFileNameWithoutExtension(expectedName);
            return Directory.EnumerateFiles(root, expectedStem + "*.zip", SearchOption.AllDirectories)
                .Where(path => Path.GetFileNameWithoutExtension(path).StartsWith(expectedStem + " (", StringComparison.OrdinalIgnoreCase))
                .Where(IsPreferredResultZip)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault() ?? "";
        }
        catch (UnauthorizedAccessException) { return ""; }
        catch (IOException) { return ""; }
    }

    private static string? FindNewestResultZip(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return null;
        try
        {
            return Directory.EnumerateFiles(root, "*.zip", SearchOption.AllDirectories)
                .Where(IsPreferredResultZip)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch (UnauthorizedAccessException) { return null; }
        catch (IOException) { return null; }
    }

    private static bool IsSafeRawEvidence(string? path)
        => !string.IsNullOrWhiteSpace(path) && File.Exists(path) && IsRawEvidence(path);


    private static string? FindMatchingRawEvidence(string root, Artifact build)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return null;
        var stem = Path.GetFileNameWithoutExtension(!string.IsNullOrWhiteSpace(build.Name) ? build.Name : build.Path);
        if (string.IsNullOrWhiteSpace(stem)) return null;
        try
        {
            return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Where(IsRawEvidence)
                .Where(path => Path.GetFileNameWithoutExtension(path).Contains(stem, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch (UnauthorizedAccessException) { return null; }
        catch (IOException) { return null; }
    }

    private static string? FindNewestRawEvidence(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return null;
        try
        {
            return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Where(IsRawEvidence)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch (UnauthorizedAccessException) { return null; }
        catch (IOException) { return null; }
    }

    private async Task<bool> IsMatchingResultZipAsync(string path, Artifact build, CancellationToken ct)
    {
        try
        {
            var a = await manifestService.InspectAsync(path, ct);
            if (string.Equals(a.ArtifactType, "Result", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(a.ForBuildId) && !string.IsNullOrWhiteSpace(build.BuildId))
                    return string.Equals(a.ForBuildId, build.BuildId, StringComparison.OrdinalIgnoreCase);
                return IsExpectedResultFilename(path, build);
            }
        }
        catch
        {
            // Legacy filename fallback below still applies to unreadable result packages.
        }
        return IsExpectedResultFilename(path, build);
    }

    private static bool IsExpectedResultFilename(string path, Artifact build)
    {
        var expectedName = ExpectedResultFileName(build);
        if (string.IsNullOrWhiteSpace(expectedName)) return false;
        var actualName = Path.GetFileName(path);
        if (actualName.Equals(expectedName, StringComparison.OrdinalIgnoreCase)) return true;

        var expectedStem = Path.GetFileNameWithoutExtension(expectedName);
        var actualStem = Path.GetFileNameWithoutExtension(actualName);
        return actualStem.StartsWith(expectedStem + " (", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFresh(string path, Artifact build)
    {
        if (!DateTimeOffset.TryParse(build.TestStartedAt, out var started)) return true;
        return File.GetLastWriteTimeUtc(path) >= started.UtcDateTime.AddSeconds(-2);
    }

    private static bool IsRawEvidence(string path)
    {
        var ext = Path.GetExtension(path);
        if (ext.Equals(".log", StringComparison.OrdinalIgnoreCase)) return true;
        if (!ext.Equals(".txt", StringComparison.OrdinalIgnoreCase) && !ext.Equals(".json", StringComparison.OrdinalIgnoreCase)) return false;
        var name = Path.GetFileName(path);
        return name.Contains("result", StringComparison.OrdinalIgnoreCase) || name.Contains("log", StringComparison.OrdinalIgnoreCase);
    }

    private static string Safe(string value, string fallback)
    {
        var s = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s.Replace('/', '_').Replace('\\', '_').Replace(':', '_');
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path)) return path;
        var ext = Path.GetExtension(path);
        var baseName = Path.GetFileNameWithoutExtension(path);
        var dir = Path.GetDirectoryName(path)!;
        for (var i = 2; ; i++)
        {
            var candidate = Path.Combine(dir, $"{baseName} ({i}){ext}");
            if (!File.Exists(candidate)) return candidate;
        }
    }
}
