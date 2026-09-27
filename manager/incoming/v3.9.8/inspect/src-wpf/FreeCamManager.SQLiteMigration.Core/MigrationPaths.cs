using FreeCamManager.Core.Services;

namespace FreeCamManager.SQLiteMigration.Core;

public sealed class MigrationPaths
{
    public MigrationPaths(string experimentDataDirectory, string productionDataDirectory, string freeCamRoot)
    {
        ExperimentDataDirectory = Path.GetFullPath(experimentDataDirectory ?? throw new ArgumentNullException(nameof(experimentDataDirectory)));
        ProductionDataDirectory = Path.GetFullPath(productionDataDirectory ?? throw new ArgumentNullException(nameof(productionDataDirectory)));
        FreeCamRoot = Path.GetFullPath(freeCamRoot ?? throw new ArgumentNullException(nameof(freeCamRoot)));
    }

    public string ExperimentDataDirectory { get; }
    public string ProductionDataDirectory { get; }
    public string FreeCamRoot { get; }
    public string DatabaseFile => Path.Combine(ExperimentDataDirectory, "library.db");
    public string BackupsDirectory => Path.Combine(ExperimentDataDirectory, "Backups");
    public string ReportsDirectory => Path.Combine(ExperimentDataDirectory, "Reports");
    public string ExportsDirectory => Path.Combine(ExperimentDataDirectory, "Exports");
    public string CorruptDirectory => Path.Combine(ExperimentDataDirectory, "Corrupt");
    public string DefaultProductionLibraryFile => Path.Combine(ProductionDataDirectory, "library.json");

    public static MigrationPaths CreateDefault()
    {
        var productionDataDirectory = Path.GetDirectoryName(AppPaths.LibraryFile)
            ?? throw new InvalidOperationException($"Cannot resolve Manager data directory from {AppPaths.LibraryFile}.");
        return new(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FreeCamManager_SQLiteMigrationExperiment"),
            productionDataDirectory,
            AppPaths.DefaultRoot());
    }
}
