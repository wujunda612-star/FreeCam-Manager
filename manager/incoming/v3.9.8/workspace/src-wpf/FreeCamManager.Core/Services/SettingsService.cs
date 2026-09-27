using System.Text.Json;
using FreeCamManager.Core.Models;

namespace FreeCamManager.Core.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public AppSettings CreateDefault(string? home = null)
    {
        var root = AppPaths.DefaultRoot(home);
        return new AppSettings
        {
            RootDir = root,
            InboxDir = AppPaths.DefaultInbox(home),
            LogDir = Path.Combine(root, "Logs"),
            ManagerLoggingEnabled = true,
            ScanSeconds = 3,
            DiscardAutoDeleteDays = 0,
            HideFreeCamPrefix = true,
            ShowFilenameAliases = true,
            ShowFeatureAliases = true,
            ShowStageAliases = true,
            DevelopmentFileColumnWidth = 0,
            DevelopmentFeatureColumnWidth = 104,
            DevelopmentStageColumnWidth = 74,
            HistoryFileColumnWidth = 0,
            HistoryFeatureColumnWidth = 104,
            HistoryStageColumnWidth = 74,
            Theme = "system"
        };
    }

    public async Task<AppSettings> LoadOrCreateAsync(string file, string? home = null, CancellationToken ct = default)
    {
        if (!File.Exists(file))
        {
            var created = CreateDefault(home);
            await SaveAsync(file, created, ct);
            return created;
        }

        await using var stream = File.OpenRead(file);
        var cfg = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions, ct) ?? new AppSettings();
        var fallback = CreateDefault(home);
        var changed = Normalize(cfg, fallback);
        if (changed) await SaveAsync(file, cfg, ct);
        return cfg;
    }

    public async Task SaveAsync(string file, AppSettings cfg, CancellationToken ct = default)
    {
        Normalize(cfg, CreateDefault());
        var parent = Path.GetDirectoryName(file);
        if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
        var tmp = file + ".tmp";
        await using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, true))
        {
            await JsonSerializer.SerializeAsync(stream, cfg, JsonOptions, ct);
            await stream.FlushAsync(ct);
        }
        File.Move(tmp, file, true);
    }

    public void EnsureDirectories(AppSettings cfg)
    {
        foreach (var dir in RequiredDirectories(cfg))
        {
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
        }
    }

    public static string TestingRoot(AppSettings cfg) => Path.Combine(cfg.RootDir, "01_Testing");
    public static string StableRoot(AppSettings cfg) => Path.Combine(cfg.RootDir, "10_Stable");
    public static string FeatureRoot(AppSettings cfg) => Path.Combine(cfg.RootDir, "20_Feature");
    public static string ExperimentRoot(AppSettings cfg) => Path.Combine(cfg.RootDir, "30_Experiment");
    public static string ResultRoot(AppSettings cfg) => Path.Combine(cfg.RootDir, "40_Result");
    public static string ArchiveRoot(AppSettings cfg) => Path.Combine(cfg.RootDir, "80_Archive");
    public static string UnknownRoot(AppSettings cfg) => Path.Combine(cfg.RootDir, "90_Unknown");

    private static IEnumerable<string> RequiredDirectories(AppSettings cfg)
    {
        yield return cfg.RootDir;
        yield return cfg.InboxDir;
        if (cfg.ManagerLoggingEnabled) yield return cfg.LogDir;
        // StableBackupDir is optional/external. Do not touch it during startup;
        // validate/create it only when the user actually synchronizes Stable backup.
        yield return TestingRoot(cfg);
        yield return StableRoot(cfg);
        yield return FeatureRoot(cfg);
        yield return ExperimentRoot(cfg);
        yield return ResultRoot(cfg);
        yield return ArchiveRoot(cfg);
        yield return UnknownRoot(cfg);
    }

    private static bool Normalize(AppSettings cfg, AppSettings fallback)
    {
        var changed = false;
        if (string.IsNullOrWhiteSpace(cfg.RootDir)) { cfg.RootDir = fallback.RootDir; changed = true; }
        if (string.IsNullOrWhiteSpace(cfg.InboxDir)) { cfg.InboxDir = fallback.InboxDir; changed = true; }
        if (string.IsNullOrWhiteSpace(cfg.LogDir)) { cfg.LogDir = Path.Combine(cfg.RootDir, "Logs"); changed = true; }
        if (cfg.ScanSeconds <= 0) { cfg.ScanSeconds = 3; changed = true; }
        if (cfg.DiscardAutoDeleteDays is not (0 or 1 or 3 or 7 or 30)) { cfg.DiscardAutoDeleteDays = 0; changed = true; }
        if (cfg.DevelopmentFileColumnWidth is > 0 and < 180) { cfg.DevelopmentFileColumnWidth = 0; changed = true; }
        if (cfg.DevelopmentFeatureColumnWidth < 60) { cfg.DevelopmentFeatureColumnWidth = 104; changed = true; }
        if (cfg.DevelopmentStageColumnWidth < 52) { cfg.DevelopmentStageColumnWidth = 74; changed = true; }
        if (cfg.HistoryFileColumnWidth is > 0 and < 180) { cfg.HistoryFileColumnWidth = 0; changed = true; }
        if (cfg.HistoryFeatureColumnWidth < 60) { cfg.HistoryFeatureColumnWidth = 104; changed = true; }
        if (cfg.HistoryStageColumnWidth < 52) { cfg.HistoryStageColumnWidth = 74; changed = true; }
        if (!string.Equals(cfg.Theme, "system", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(cfg.Theme, "dark", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(cfg.Theme, "light", StringComparison.OrdinalIgnoreCase))
        {
            cfg.Theme = fallback.Theme;
            changed = true;
        }
        cfg.Theme = cfg.Theme.ToLowerInvariant();
        return changed;
    }
}
