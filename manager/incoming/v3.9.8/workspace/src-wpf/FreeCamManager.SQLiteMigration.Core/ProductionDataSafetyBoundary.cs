namespace FreeCamManager.SQLiteMigration.Core;

public sealed class ProductionDataSafetyBoundary : ISqliteWriteBoundary
{
    private readonly string _dataDirectory;
    private readonly string _freeCamRoot;
    private readonly bool _allowLegacyJsonWrites;
    private readonly StringComparison _comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public ProductionDataSafetyBoundary(ProductionStoragePaths paths, bool allowLegacyJsonWrites = false)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _dataDirectory = Path.GetFullPath(paths.DataDirectory);
        _freeCamRoot = Path.GetFullPath(paths.FreeCamRoot);
        _allowLegacyJsonWrites = allowLegacyJsonWrites;
    }

    public void EnsureWriteAllowed(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new UnauthorizedAccessException("Empty SQLite write path is not allowed.");
        var full = Path.GetFullPath(path);
        if (!_allowLegacyJsonWrites && (PathEquals(full, Path.Combine(_dataDirectory, "library.json")) || PathEquals(full, Path.Combine(_dataDirectory, "library.json.bak"))))
            throw new UnauthorizedAccessException($"Normal SQLite operation cannot write legacy library JSON: {full}");
        if (IsWithin(full, _freeCamRoot))
            throw new UnauthorizedAccessException($"SQLite storage must never write FreeCam assets: {full}");
        if (!IsWithin(full, _dataDirectory))
            throw new UnauthorizedAccessException($"SQLite storage writes are restricted to Manager data: {_dataDirectory}");
    }

    private bool PathEquals(string left, string right) => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), _comparison);

    private bool IsWithin(string candidate, string root)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullCandidate = Path.GetFullPath(candidate);
        if (string.Equals(fullCandidate, fullRoot, _comparison)) return true;
        return fullCandidate.StartsWith(fullRoot + Path.DirectorySeparatorChar, _comparison);
    }
}
