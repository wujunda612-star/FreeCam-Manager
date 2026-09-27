using System.Text.Json;

namespace FreeCamManager.Core.Services;

public sealed record ManagerUpdateCleanupResult(int CleanedDirectories, int RemovedEntries, int FailedDirectories);

public static class ManagerUpdateCleanupService
{
    private static readonly HashSet<string> DiagnosticExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".log", ".json"
    };

    public static ManagerUpdateCleanupResult CleanupSuccessfulUpdates(string updateRoot)
    {
        if (string.IsNullOrWhiteSpace(updateRoot) || !Directory.Exists(updateRoot))
            return new ManagerUpdateCleanupResult(0, 0, 0);

        var cleaned = 0;
        var removed = 0;
        var failed = 0;

        foreach (var stagingDirectory in Directory.EnumerateDirectories(updateRoot))
        {
            try
            {
                var statusFile = Path.Combine(stagingDirectory, "UPDATE_STATUS.json");
                if (!IsSuccessful(statusFile)) continue;

                foreach (var entry in Directory.EnumerateFileSystemEntries(stagingDirectory))
                {
                    if (File.Exists(entry) && IsDiagnosticFile(entry)) continue;

                    if (Directory.Exists(entry))
                        Directory.Delete(entry, recursive: true);
                    else if (File.Exists(entry))
                        File.Delete(entry);

                    removed++;
                }

                cleaned++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                failed++;
            }
        }

        return new ManagerUpdateCleanupResult(cleaned, removed, failed);
    }

    private static bool IsSuccessful(string statusFile)
    {
        if (!File.Exists(statusFile)) return false;

        using var document = JsonDocument.Parse(File.ReadAllText(statusFile));
        return document.RootElement.TryGetProperty("state", out var state)
            && string.Equals(state.GetString(), "success", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDiagnosticFile(string path)
    {
        var name = Path.GetFileName(path);
        return name.StartsWith("UPDATE_", StringComparison.OrdinalIgnoreCase)
            && DiagnosticExtensions.Contains(Path.GetExtension(name));
    }
}
