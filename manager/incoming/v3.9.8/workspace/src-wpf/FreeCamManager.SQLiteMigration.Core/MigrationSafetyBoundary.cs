namespace FreeCamManager.SQLiteMigration.Core;

public sealed class MigrationSafetyBoundary : ISqliteWriteBoundary
{
    private readonly MigrationPaths _paths;
    private readonly StringComparison _comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public MigrationSafetyBoundary(MigrationPaths paths) => _paths = paths ?? throw new ArgumentNullException(nameof(paths));

    public void EnsureExperimentWriteAllowed(string path) => EnsureWriteAllowed(path);

    public void EnsureWriteAllowed(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new UnauthorizedAccessException("Empty write path is not allowed.");
        var full = Path.GetFullPath(path);
        if (IsWithin(full, _paths.ProductionDataDirectory))
            throw new UnauthorizedAccessException($"SQLite migration experiment cannot write production Manager data: {full}");
        if (IsWithin(full, _paths.FreeCamRoot))
            throw new UnauthorizedAccessException($"SQLite migration experiment cannot write FreeCam assets: {full}");
        if (!IsWithin(full, _paths.ExperimentDataDirectory))
            throw new UnauthorizedAccessException($"SQLite migration experiment writes are restricted to: {_paths.ExperimentDataDirectory}");
    }

    private bool IsWithin(string candidate, string root)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullCandidate = Path.GetFullPath(candidate);
        if (string.Equals(fullCandidate, fullRoot, _comparison)) return true;
        var prefix = fullRoot + Path.DirectorySeparatorChar;
        return fullCandidate.StartsWith(prefix, _comparison);
    }
}
