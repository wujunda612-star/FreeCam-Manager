using FreeCamManager.Core.Models;

namespace FreeCamManager.Core.Services;

public sealed class OrganizerService(
    string root,
    string stableBackup,
    LibraryService library,
    ManifestService manifest,
    ClassificationService classification,
    HashService hash)
{
    public string Root { get; set; } = root;
    public string StableBackup { get; set; } = stableBackup;
    private readonly PathRebaseService pathRebase = new();

    public async Task<Artifact> ProcessAsync(string path, CancellationToken ct = default)
    {
        EnsureDirs();
        Artifact a;
        try { a = await manifest.InspectAsync(path, ct); }
        catch (Exception ex)
        {
            a = manifest.InspectFilename(Path.GetFileName(path));
            a.Path = path;
            a.Name = Path.GetFileName(path);
            a.Notes = "识别警告: " + ex.Message;
        }
        var info = new FileInfo(path);
        a.Size = info.Length;
        a.Sha256 = await hash.FileSha256Async(path, ct);
        a.ImportedAt = DateTimeOffset.Now.ToString("O");

        var existing = library.ByHash(a.Sha256);
        if (existing is not null && !SamePath(existing.Path, path))
        {
            var duplicateDir = Path.Combine(Root, "80_Archive", "Duplicates", DateTime.Now.ToString("yyyy-MM-dd"));
            var dst = await MoveUniqueAsync(path, duplicateDir, ct);
            a.Path = dst;
            a.RelativePath = PathRebaseService.TryMakeRelative(Root, dst);
            a.Category = "Duplicate";
            a.Status = "重复文件";
            a.DuplicateOf = existing.Path;
            library.Upsert(a);
            await library.SaveAsync(ct);
            return a;
        }

        var decision = classification.Plan(a);
        var destination = await MoveUniqueAsync(path, Path.Combine(Root, decision.RelativeDirectory), ct);
        a.Path = destination;
        a.RelativePath = PathRebaseService.TryMakeRelative(Root, destination);
        a.Category = decision.Category;
        if (string.IsNullOrWhiteSpace(a.TestStatus) && a.Category is "Feature" or "Experiment") a.TestStatus = "待测试";
        if (a.Category == "StableCandidate") a.Status = "待确认 Stable（稳定版）";
        library.Upsert(a);
        library.PairResults();
        await library.SaveAsync(ct);
        return a;
    }

    public async Task<IReadOnlyList<Artifact>> ConfirmStableAsync(string version, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(version)) throw new ArgumentException("empty stable version", nameof(version));
        EnsureDirs();
        await pathRebase.RepairLibraryPathsAsync(library, Root, ct);

        var candidates = library.Snapshot()
            .Where(a => a.Category == "StableCandidate" && string.Equals(StableVersionResolver.Resolve(a), version, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (candidates.Count == 0) throw new InvalidOperationException($"no Stable candidate for {version}");

        var hasRuntime = candidates.Any(a => Eq(a.ArtifactType, "Runtime"));
        var hasSource = candidates.Any(a => Eq(a.ArtifactType, "Source"));
        if (!hasRuntime || !hasSource)
            throw new InvalidOperationException("Stable 冻结至少需要运行包 + 完整源码。Repo.bundle（Git 仓库备份）可选，SHA256（校验文件）会在确认后自动生成。");

        var moved = new List<Artifact>();
        foreach (var a0 in candidates)
        {
            ct.ThrowIfCancellationRequested();
            var a = a0.Clone();
            var oldPath = a.Path;
            var dst = await MoveUniqueAsync(oldPath, Path.Combine(Root, "10_Stable", version), ct);
            a.Path = dst;
            a.RelativePath = PathRebaseService.TryMakeRelative(Root, dst);
            a.Category = "Stable";
            a.BuildType = "Stable";
            a.ReleaseState = "Stable";
            a.Status = "已冻结";
            a.Protected = true;
            if (File.Exists(dst)) a.Sha256 = await hash.FileSha256Async(dst, ct);
            if (!library.ReplacePath(oldPath, a)) library.Upsert(a);
            moved.Add(a);
        }

        if (!moved.Any(a => Eq(a.ArtifactType, "SHA256")))
        {
            var checksum = await CreateStableSha256ArtifactAsync(version, moved, ct);
            library.Upsert(checksum);
            moved.Add(checksum);
        }

        await library.SaveAsync(ct);

        if (!string.IsNullOrWhiteSpace(StableBackup))
        {
            foreach (var item in moved)
            {
                if (File.Exists(item.Path))
                    await CopyUniqueAsync(item.Path, Path.Combine(StableBackup, version), ct);
            }
        }
        return moved;
    }

    private async Task<Artifact> CreateStableSha256ArtifactAsync(string version, IReadOnlyList<Artifact> stableArtifacts, CancellationToken ct)
    {
        var stableDir = Path.Combine(Root, "10_Stable", version);
        Directory.CreateDirectory(stableDir);
        var checksumPath = Path.Combine(stableDir, $"FreeCam_{version}_SHA256.txt");
        var lines = new List<string>();
        foreach (var item in stableArtifacts.Where(a => !Eq(a.ArtifactType, "SHA256") && File.Exists(a.Path)).OrderBy(a => Path.GetFileName(a.Path), StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            var fileHash = string.IsNullOrWhiteSpace(item.Sha256) ? await hash.FileSha256Async(item.Path, ct) : item.Sha256;
            lines.Add($"{fileHash}  {Path.GetFileName(item.Path)}");
        }
        await File.WriteAllTextAsync(checksumPath, string.Join(Environment.NewLine, lines) + Environment.NewLine, new System.Text.UTF8Encoding(false), ct);
        var checksumHash = await hash.FileSha256Async(checksumPath, ct);
        var lineage = stableArtifacts.FirstOrDefault();
        return new Artifact
        {
            Path = checksumPath,
            RelativePath = PathRebaseService.TryMakeRelative(Root, checksumPath),
            Name = Path.GetFileName(checksumPath),
            Version = version,
            BuildName = version,
            Base = lineage?.Base ?? "",
            Branch = lineage?.Branch ?? "main",
            BuildType = "Stable",
            ArtifactType = "SHA256",
            ReleaseState = "Stable",
            Category = "Stable",
            Status = "已冻结",
            Protected = true,
            Sha256 = checksumHash,
            ImportedAt = DateTimeOffset.Now.ToString("O")
        };
    }

    private static bool Eq(string? a, string b) => string.Equals(a?.Trim(), b, StringComparison.OrdinalIgnoreCase);

    public async Task<int> RepairStableCandidateDirectoriesAsync(CancellationToken ct = default)
    {
        var repaired = 0;
        var candidateRoot = Path.Combine(Root, "90_Unknown", "Stable_Candidate");
        if (!Directory.Exists(candidateRoot)) return 0;
        foreach (var a0 in library.Snapshot())
        {
            ct.ThrowIfCancellationRequested();
            if (a0.Category != "StableCandidate") continue;
            var version = StableVersionResolver.Resolve(a0);
            if (string.IsNullOrWhiteSpace(version)) continue;

            var a = a0.Clone();
            var changedMetadata = false;
            if (string.IsNullOrWhiteSpace(a.Version)) { a.Version = version; changedMetadata = true; }
            if (string.IsNullOrWhiteSpace(a.BuildName)) { a.BuildName = version; changedMetadata = true; }

            var expectedDirectory = Path.Combine(candidateRoot, version);
            var currentDirectory = Path.GetDirectoryName(a.Path) ?? "";
            if (File.Exists(a.Path) && !SamePath(currentDirectory, expectedDirectory))
            {
                var oldPath = a.Path;
                var oldDirectory = currentDirectory;
                var destination = await MoveUniqueAsync(oldPath, expectedDirectory, ct);
                a.Path = destination;
                a.RelativePath = PathRebaseService.TryMakeRelative(Root, destination);
                if (!library.ReplacePath(oldPath, a)) library.Upsert(a);
                TryDeleteEmptyCandidateDirectory(oldDirectory, candidateRoot);
                repaired++;
                continue;
            }

            if (changedMetadata)
            {
                if (!library.ReplacePath(a0.Path, a)) library.Upsert(a);
                repaired++;
            }
        }
        if (repaired > 0) await library.SaveAsync(ct);
        return repaired;
    }

    public async Task<Artifact> ArchiveAsync(string path, CancellationToken ct = default)
    {
        var a = library.ByPath(path) ?? throw new FileNotFoundException("artifact not indexed", path);
        if (a.Protected) throw new InvalidOperationException("artifact is protected");
        var oldPath = a.Path;
        var dst = await MoveUniqueAsync(oldPath, Path.Combine(Root, "80_Archive", Safe(a.Feature, "Unknown"), Safe(a.Stage, "Unstaged")), ct);
        a.Path = dst;
        a.RelativePath = PathRebaseService.TryMakeRelative(Root, dst);
        a.Category = "Archive";
        a.Status = "已废弃";
        a.ManualStatus = "已废弃";
        if (!library.ReplacePath(oldPath, a)) library.Upsert(a);
        await library.SaveAsync(ct);
        return a;
    }

    public async Task DeleteAsync(string path, CancellationToken ct = default)
    {
        var a = library.ByPath(path) ?? throw new FileNotFoundException("artifact not indexed", path);
        if (a.Protected) throw new InvalidOperationException("artifact is protected");
        ct.ThrowIfCancellationRequested();
        if (File.Exists(a.Path)) File.Delete(a.Path);
        library.RemoveByPath(a.Path);
        await library.SaveAsync(ct);
    }

    public async Task<(int Verified, int Copied, int Failed)> SyncStableBackupAsync(CancellationToken ct = default)
    {
        await pathRebase.RepairLibraryPathsAsync(library, Root, ct);
        if (string.IsNullOrWhiteSpace(StableBackup)) throw new InvalidOperationException("还没有设置 Stable Backup（稳定版备份盘）");
        var verified = 0; var copied = 0; var failed = 0;
        foreach (var item in library.Snapshot().Where(x => x.Category == "Stable"))
        {
            ct.ThrowIfCancellationRequested();
            var version = StableVersionResolver.Resolve(item);
            if (string.IsNullOrWhiteSpace(version)) { failed++; continue; }
            var dir = Path.Combine(StableBackup, version);
            Directory.CreateDirectory(dir);
            var dst = Path.Combine(dir, Path.GetFileName(item.Path));
            try
            {
                if (File.Exists(dst) && string.Equals(await hash.FileSha256Async(dst, ct), item.Sha256, StringComparison.OrdinalIgnoreCase))
                { verified++; continue; }
                File.Copy(item.Path, dst, true);
                if (string.Equals(await hash.FileSha256Async(dst, ct), item.Sha256, StringComparison.OrdinalIgnoreCase)) copied++; else failed++;
            }
            catch { failed++; }
        }
        return (verified, copied, failed);
    }

    private void EnsureDirs()
    {
        foreach (var d in new[] { "01_Testing", "10_Stable", "20_Feature", "30_Experiment", "40_Result", "50_Manager", "60_索引库", "80_Archive", "90_Unknown" })
            Directory.CreateDirectory(Path.Combine(Root, d));
        // StableBackup is optional/external. Do not touch it while processing normal builds.
        // SyncStableBackupAsync/ConfirmStableAsync are the only operations that may access it.
    }


    private static void TryDeleteEmptyCandidateDirectory(string directory, string candidateRoot)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(directory) || SamePath(directory, candidateRoot)) return;
            if (!Directory.Exists(directory)) return;
            if (Directory.EnumerateFileSystemEntries(directory).Any()) return;
            Directory.Delete(directory);
        }
        catch { }
    }

    private static async Task<string> MoveUniqueAsync(string source, string directory, CancellationToken ct)
    {
        Directory.CreateDirectory(directory);
        var destination = UniquePath(Path.Combine(directory, Path.GetFileName(source)));
        try { File.Move(source, destination); return destination; }
        catch (IOException)
        {
            await CopyFileAsync(source, destination, overwrite: false, ct);
            File.Delete(source);
            return destination;
        }
    }

    private static async Task<string> CopyUniqueAsync(string source, string directory, CancellationToken ct)
    {
        Directory.CreateDirectory(directory);
        var destination = UniquePath(Path.Combine(directory, Path.GetFileName(source)));
        await CopyFileAsync(source, destination, overwrite: false, ct);
        return destination;
    }

    private static async Task CopyFileAsync(string source, string destination, bool overwrite, CancellationToken ct)
    {
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, true);
        await using var output = new FileStream(destination, overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, true);
        await input.CopyToAsync(output, 128 * 1024, ct);
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return path;
        var ext = Path.GetExtension(path);
        var baseName = Path.GetFileNameWithoutExtension(path);
        var dir = Path.GetDirectoryName(path)!;
        for (var i = 2; ; i++)
        {
            var candidate = Path.Combine(dir, $"{baseName} ({i}){ext}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }
    }

    private static string Safe(string value, string fallback)
    {
        var s = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s.Replace('/', '_').Replace('\\', '_').Replace(':', '_');
    }

    private static bool SamePath(string a, string b)
    {
        try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
        catch { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
    }
}
