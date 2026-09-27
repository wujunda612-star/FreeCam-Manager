using System.Text.Json.Serialization;

namespace FreeCamManager.Core.Models;

public sealed class AppSettings
{
    [JsonPropertyName("root_dir")] public string RootDir { get; set; } = "";
    [JsonPropertyName("inbox_dir")] public string InboxDir { get; set; } = "";
    [JsonPropertyName("stable_backup_dir")] public string StableBackupDir { get; set; } = "";
    [JsonPropertyName("log_dir")] public string LogDir { get; set; } = "";
    [JsonPropertyName("manager_logging_enabled")] public bool ManagerLoggingEnabled { get; set; } = true;
    [JsonPropertyName("scan_seconds")] public int ScanSeconds { get; set; } = 3;
    [JsonPropertyName("discard_auto_delete_days")] public int DiscardAutoDeleteDays { get; set; } = 0;
    [JsonPropertyName("hide_freecam_prefix")] public bool HideFreeCamPrefix { get; set; } = true;
    [JsonPropertyName("show_filename_aliases")] public bool ShowFilenameAliases { get; set; } = true;
    [JsonPropertyName("show_feature_aliases")] public bool ShowFeatureAliases { get; set; } = true;
    [JsonPropertyName("show_stage_aliases")] public bool ShowStageAliases { get; set; } = true;
    [JsonPropertyName("development_file_column_width")] public double DevelopmentFileColumnWidth { get; set; } = 0;
    [JsonPropertyName("development_feature_column_width")] public double DevelopmentFeatureColumnWidth { get; set; } = 104;
    [JsonPropertyName("development_stage_column_width")] public double DevelopmentStageColumnWidth { get; set; } = 74;
    [JsonPropertyName("history_file_column_width")] public double HistoryFileColumnWidth { get; set; } = 0;
    [JsonPropertyName("history_feature_column_width")] public double HistoryFeatureColumnWidth { get; set; } = 104;
    [JsonPropertyName("history_stage_column_width")] public double HistoryStageColumnWidth { get; set; } = 74;
    [JsonPropertyName("theme")] public string Theme { get; set; } = "system";
}
