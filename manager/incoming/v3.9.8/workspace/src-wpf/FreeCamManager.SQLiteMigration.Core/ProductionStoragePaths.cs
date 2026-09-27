namespace FreeCamManager.SQLiteMigration.Core;

public sealed class ProductionStoragePaths
{
    public ProductionStoragePaths(string dataDirectory, string freeCamRoot)
    {
        DataDirectory = Path.GetFullPath(dataDirectory ?? throw new ArgumentNullException(nameof(dataDirectory)));
        FreeCamRoot = Path.GetFullPath(freeCamRoot ?? throw new ArgumentNullException(nameof(freeCamRoot)));
    }

    public string DataDirectory { get; }
    public string FreeCamRoot { get; }
    public string DatabaseFile => Path.Combine(DataDirectory, "library.db");
    public string SqliteBackupsDirectory => Path.Combine(DataDirectory, "SQLiteBackups");
    public string RecoveryDirectory => Path.Combine(DataDirectory, "SQLiteRecovery");
    public string RecoveryExportFile => Path.Combine(RecoveryDirectory, "library-latest.json");
    public string CorruptDirectory => Path.Combine(DataDirectory, "SQLiteCorrupt");
    public string MigrationBackupDirectory => Path.Combine(DataDirectory, "MigrationBackup");
    public string RollbackBackupDirectory => Path.Combine(DataDirectory, "RollbackBackup");
    public string LegacyLibraryFile => Path.Combine(DataDirectory, "library.json");
    public string LegacyBackupFile => LegacyLibraryFile + ".bak";
}
