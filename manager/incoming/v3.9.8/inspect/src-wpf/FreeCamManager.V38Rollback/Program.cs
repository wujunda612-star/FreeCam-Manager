using FreeCamManager.Core.Services;
using FreeCamManager.SQLiteMigration.Core;

var mode = args.Length == 0 ? "verify" : args[0].Trim().ToLowerInvariant();
try
{
    var paths = new ProductionStoragePaths(AppPaths.DataDirectory, AppPaths.DefaultRoot());
    if (mode == "verify")
    {
        var session = await ProductionManagerSqliteLibrarySession.OpenAsync(paths);
        var check = await session.CheckIntegrityAsync(false);
        if (!check.IsOk) throw new InvalidDataException($"SQLite quick_check failed: {check.Message}");
        var items = await session.ReloadAsync();
        Console.WriteLine("V3.8 VERIFY: PASS");
        Console.WriteLine($"database: {session.DatabaseFile}");
        Console.WriteLine($"source: {session.RecoverySource}");
        Console.WriteLine($"quick_check: {check.Message}");
        Console.WriteLine($"items: {items.Count}");
        Console.WriteLine($"fingerprint: {ArtifactSnapshotFingerprint.Compute(items)}");
        Console.WriteLine($"recovery_json: {session.RecoveryExportFile}");
        Console.WriteLine($"recovery_json_exists: {File.Exists(session.RecoveryExportFile)}");
        return 0;
    }

    if (mode == "rollback")
    {
        var result = await new LegacyJsonRollbackService().RestoreLegacyJsonAsync(paths);
        Console.WriteLine("FreeCam Manager V3.8 -> V3.7 Fix1 rollback export complete");
        Console.WriteLine($"library.json: {result.LegacyLibraryFile}");
        Console.WriteLine($"previous legacy backup: {result.BackupFile}");
        Console.WriteLine($"archived SQLite state: {result.SqliteArchiveDirectory}");
        Console.WriteLine($"items: {result.ItemCount}");
        Console.WriteLine($"fingerprint: {result.SnapshotFingerprint}");
        return 0;
    }

    Console.Error.WriteLine("Usage: FreeCam_Manager_V3.8_Tool.exe [verify|rollback]");
    return 2;
}
catch (Exception ex)
{
    Console.Error.WriteLine(mode == "rollback" ? "ROLLBACK FAILED" : "VERIFY FAILED");
    Console.Error.WriteLine(ex);
    return 1;
}
