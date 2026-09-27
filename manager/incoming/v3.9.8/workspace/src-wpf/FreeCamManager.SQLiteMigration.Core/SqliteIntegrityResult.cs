namespace FreeCamManager.SQLiteMigration.Core;

public sealed record SqliteIntegrityResult(bool IsOk, string Check, string Message)
{
    public static SqliteIntegrityResult Ok(string check) => new(true, check, "ok");
    public static SqliteIntegrityResult Failed(string check, string message) => new(false, check, message);
}
