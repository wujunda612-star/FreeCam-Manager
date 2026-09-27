namespace FreeCamManager.SQLiteMigration.Core;

public sealed record MigrationReport(
    bool Success,
    bool IntegrityOk,
    bool UsedSourceBackup,
    string SourceFile,
    string SourceSha256,
    int ItemCount,
    string SnapshotFingerprint,
    string DatabaseFile,
    string PreviousDatabaseBackup,
    string Message);

public enum MigrationFault
{
    None = 0,
    BeforePromotion = 1
}
