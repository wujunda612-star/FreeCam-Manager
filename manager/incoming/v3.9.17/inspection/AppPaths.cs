namespace FreeCamManager.Core.Services;

public static class AppPaths
{
    public static string DataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FreeCamManager");

    public static string LocalDataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FreeCamManager");

    public static string UpdateDirectory => Path.Combine(LocalDataDirectory, "Updates");

    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");
    public static string LibraryFile => Path.Combine(DataDirectory, "library.json");
    public static string LibraryDatabaseFile => Path.Combine(DataDirectory, "library.db");
    public static string SQLiteBackupsDirectory => Path.Combine(DataDirectory, "SQLiteBackups");
    public static string SQLiteRecoveryDirectory => Path.Combine(DataDirectory, "SQLiteRecovery");
    public static string SQLiteMigrationBackupDirectory => Path.Combine(DataDirectory, "MigrationBackup");
    public static string FilenameTermsFile => Path.Combine(AppContext.BaseDirectory, "Config", "FilenameTerms.json");
    public static string FilenameTermsVersionFile => Path.Combine(AppContext.BaseDirectory, "Config", "version.json");

    public static string DefaultRoot(string? home = null)
    {
        // User-selected project convention: new Windows installs default to D:\\FreeCam.
        // Supplying home keeps tests/portable tooling deterministic.
        if (!string.IsNullOrWhiteSpace(home)) return Path.Combine(home, "FreeCam");
        if (OperatingSystem.IsWindows()) return @"D:\FreeCam";
        home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, "FreeCam");
    }

    public static string DefaultInbox(string? home = null)
        => Path.Combine(DefaultRoot(home), "00_Downloa");
}
