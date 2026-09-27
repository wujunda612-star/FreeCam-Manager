using System.IO.Compression;
using FreeCamManager.Core.Models;

namespace FreeCamManager.Core.Services;

public enum ExtractionStatus { Extracted, AlreadyExists }
public sealed record ExtractionResult(string Destination, ExtractionStatus Status);

public sealed class ExtractionService
{
    public bool ShouldExtract(Artifact a)
    {
        var artifact = (a.ArtifactType ?? "").Trim().ToLowerInvariant();
        if (artifact is "result" or "source" or "repo" or "sha256" or "releasenote") return false;
        if (artifact is "runtime" or "bundle") return true;
        if (artifact.Length > 0) return false;
        return (a.BuildType ?? "").Trim().ToLowerInvariant() is "test" or "probe" or "experiment" or "feature" or "regression";
    }

    public async Task<ExtractionResult> ExtractToTestingAsync(string zipPath, string testingRoot, CancellationToken ct = default)
    {
        if (!zipPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("not a zip file");
        if (string.IsNullOrWhiteSpace(testingRoot)) throw new ArgumentException("empty testing root", nameof(testingRoot));
        Directory.CreateDirectory(testingRoot);
        var baseName = Path.GetFileNameWithoutExtension(zipPath);
        var destination = Path.Combine(testingRoot, baseName);
        if (Directory.Exists(destination)) return new(destination, ExtractionStatus.AlreadyExists);
        if (File.Exists(destination)) throw new IOException($"extraction destination exists and is not a directory: {destination}");

        var temp = destination + ".extracting";
        if (Directory.Exists(temp)) Directory.Delete(temp, true);
        Directory.CreateDirectory(temp);
        var completed = false;
        try
        {
            await using var input = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 64 * 1024, true);
            using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: false);
            foreach (var entry in archive.Entries)
            {
                ct.ThrowIfCancellationRequested();
                if (IsUnixSymlink(entry)) throw new InvalidDataException($"unsafe symlink entry: {entry.FullName}");
                var normalized = entry.FullName.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
                if (string.IsNullOrWhiteSpace(normalized)) continue;
                var target = Path.GetFullPath(Path.Combine(temp, normalized));
                var root = Path.GetFullPath(temp) + Path.DirectorySeparatorChar;
                if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase) && !string.Equals(target.TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(temp), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"unsafe zip entry path: {entry.FullName}");

                if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
                {
                    Directory.CreateDirectory(target);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await using var source = entry.Open();
                await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, true);
                await source.CopyToAsync(output, 64 * 1024, ct);
            }
            Directory.Move(temp, destination);
            completed = true;
            return new(destination, ExtractionStatus.Extracted);
        }
        finally
        {
            if (!completed && Directory.Exists(temp))
            {
                try { Directory.Delete(temp, true); } catch { }
            }
        }
    }

    private static bool IsUnixSymlink(ZipArchiveEntry entry)
    {
        var unixMode = (entry.ExternalAttributes >> 16) & 0xFFFF;
        return (unixMode & 0xF000) == 0xA000;
    }
}
