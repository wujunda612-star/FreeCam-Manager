namespace FreeCamManager.SQLiteMigration.Core;

public interface ISqliteWriteBoundary
{
    void EnsureWriteAllowed(string path);
}
