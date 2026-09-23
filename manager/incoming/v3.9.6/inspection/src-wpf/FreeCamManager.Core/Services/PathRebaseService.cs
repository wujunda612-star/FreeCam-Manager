using FreeCamManager.Core.Models;

namespace FreeCamManager.Core.Services;

/// <summary>
/// Keeps persisted artifact paths portable when the FreeCam root folder is moved.
/// Absolute paths remain available for runtime compatibility, while root-relative
/// paths are persisted as the durable locator.
/// </summary>
public sealed class PathRebaseService
{
    private static readonly string[] RootMarkers =
    [
        "00_Downloa", "01_Testing", "10_Stable", "20_Feature", "30_Experiment",
        "40_Result", "80_Archive", "90_Unknown", "Logs"
    ];

    public async Task<int> RepairLibraryPathsAsync(LibraryService library, string root, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(root)) return 0;
        var repaired = 0;
        foreach (var original in library.Snapshot())
        {
            ct.ThrowIfCancellationRequested();
            var item = original.Clone();
            var changed = false;

            var resolvedPath = ResolveFile(item.Path, item.RelativePath, item.Name, root);
            if (!string.IsNullOrWhiteSpace(resolvedPath) && !SamePath(item.Path, resolvedPath))
            {
                item.Path = resolvedPath;
                changed = true;
            }
            var relative = TryMakeRelative(root, item.Path);
            if (!string.IsNullOrWhiteSpace(relative) && !string.Equals(item.RelativePath, relative, StringComparison.OrdinalIgnoreCase))
            {
                item.RelativePath = relative;
                changed = true;
            }

            var resolvedTesting = ResolveDirectory(item.TestingPath, item.TestingRelativePath, root);
            if (!string.IsNullOrWhiteSpace(resolvedTesting) && !SamePath(item.TestingPath, resolvedTesting))
            {
                item.TestingPath = resolvedTesting;
                changed = true;
            }
            var testingRelative = TryMakeRelative(root, item.TestingPath);
            if (!string.IsNullOrWhiteSpace(testingRelative) && !string.Equals(item.TestingRelativePath, testingRelative, StringComparison.OrdinalIgnoreCase))
            {
                item.TestingRelativePath = testingRelative;
                changed = true;
            }

            var resolvedResult = ResolveFile(item.ResultPath, item.ResultRelativePath, Path.GetFileName(item.ResultPath), root);
            if (!string.IsNullOrWhiteSpace(resolvedResult) && !SamePath(item.ResultPath, resolvedResult))
            {
                item.ResultPath = resolvedResult;
                changed = true;
            }
            var resultRelative = TryMakeRelative(root, item.ResultPath);
            if (!string.IsNullOrWhiteSpace(resultRelative) && !string.Equals(item.ResultRelativePath, resultRelative, StringComparison.OrdinalIgnoreCase))
            {
                item.ResultRelativePath = resultRelative;
                changed = true;
            }

            if (!changed) continue;
            if (!library.ReplacePath(original.Path, item)) library.Upsert(item);
            repaired++;
        }

        if (repaired > 0) await library.SaveAsync(ct);
        return repaired;
    }

    public static string TryMakeRelative(string root, string path)
    {
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(path)) return "";
        try
        {
            var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var fullPath = Path.GetFullPath(path);
            if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) return "";
            return Path.GetRelativePath(root, fullPath);
        }
        catch { return ""; }
    }

    public static string ResolveFile(string storedPath, string relativePath, string name, string root)
        => Resolve(storedPath, relativePath, name, root, File.Exists);

    public static string ResolveDirectory(string storedPath, string relativePath, string root)
        => Resolve(storedPath, relativePath, "", root, Directory.Exists);

    private static string Resolve(string storedPath, string relativePath, string name, string root, Func<string, bool> exists)
    {
        // The currently configured FreeCam root is authoritative. Prefer a valid
        // root-relative locator even if an old absolute copy still exists elsewhere.
        foreach (var rel in CandidateRelativePaths(storedPath, relativePath))
        {
            var candidate = Path.Combine(root, rel);
            if (exists(candidate)) return candidate;
        }

        if (!string.IsNullOrWhiteSpace(storedPath) && exists(storedPath)) return storedPath;

        // Last-resort compatibility for very old records that lost their root-relative tail.
        // Search only by exact file name and only under FreeCam's managed directory tree.
        if (!string.IsNullOrWhiteSpace(name) && Directory.Exists(root))
        {
            foreach (var marker in RootMarkers)
            {
                var managedRoot = Path.Combine(root, marker);
                if (!Directory.Exists(managedRoot)) continue;
                try
                {
                    var match = Directory.EnumerateFiles(managedRoot, name, SearchOption.AllDirectories).FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(match)) return match;
                }
                catch (UnauthorizedAccessException) { }
                catch (IOException) { }
            }
        }
        return storedPath ?? "";
    }

    private static IEnumerable<string> CandidateRelativePaths(string storedPath, string relativePath)
    {
        if (!string.IsNullOrWhiteSpace(relativePath)) yield return NormalizeRelative(relativePath);
        var inferred = InferManagedTail(storedPath);
        if (!string.IsNullOrWhiteSpace(inferred) && !string.Equals(inferred, relativePath, StringComparison.OrdinalIgnoreCase))
            yield return inferred;
    }

    private static string InferManagedTail(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        var unified = path.Replace('\\', '/');
        foreach (var marker in RootMarkers)
        {
            var token = "/" + marker + "/";
            var index = unified.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (index >= 0) return NormalizeRelative(unified[(index + 1)..]);
            if (unified.EndsWith("/" + marker, StringComparison.OrdinalIgnoreCase)) return marker;
        }
        return "";
    }

    private static string NormalizeRelative(string value)
        => value.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static bool SamePath(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
        catch { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
    }
}
