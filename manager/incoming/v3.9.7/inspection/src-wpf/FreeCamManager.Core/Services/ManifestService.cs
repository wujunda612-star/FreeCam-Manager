using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using FreeCamManager.Core.Models;

namespace FreeCamManager.Core.Services;

public sealed partial class ManifestService
{
    private sealed class RawManifest
    {
        [JsonPropertyName("SchemaVersion")] public int SchemaVersion { get; set; }
        [JsonPropertyName("Project")] public string Project { get; set; } = "";
        [JsonPropertyName("version")] public string Version { get; set; } = "";
        [JsonPropertyName("buildName")] public string BuildName { get; set; } = "";
        [JsonPropertyName("Base")] public string Base { get; set; } = "";
        [JsonPropertyName("Branch")] public string Branch { get; set; } = "";
        [JsonPropertyName("Feature")] public string Feature { get; set; } = "";
        [JsonPropertyName("BuildType")] public string BuildType { get; set; } = "";
        [JsonPropertyName("ArtifactType")] public string ArtifactType { get; set; } = "";
        [JsonPropertyName("packageRole")] public string PackageRole { get; set; } = "";
        [JsonPropertyName("Stage")] public string Stage { get; set; } = "";
        [JsonPropertyName("BuildId")] public string BuildId { get; set; } = "";
        [JsonPropertyName("ParentBuildId")] public string ParentBuildId { get; set; } = "";
        [JsonPropertyName("ForBuildId")] public string ForBuildId { get; set; } = "";
        [JsonPropertyName("Commit")] public string Commit { get; set; } = "";
        [JsonPropertyName("SourceMode")] public string SourceMode { get; set; } = "";
        [JsonPropertyName("SourceState")] public string SourceState { get; set; } = "";
        [JsonPropertyName("ReleaseState")] public string ReleaseState { get; set; } = "";
        [JsonPropertyName("releaseStage")] public string ReleaseStage { get; set; } = "";
        [JsonPropertyName("BuildDate")] public string BuildDate { get; set; } = "";
        [JsonPropertyName("createdAt")] public string CreatedAt { get; set; } = "";
        [JsonPropertyName("stable")] public bool? Stable { get; set; }
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<Artifact> InspectAsync(string path, CancellationToken ct = default)
    {
        var fallback = InspectFilename(Path.GetFileName(path));
        fallback.Path = path;
        fallback.Name = Path.GetFileName(path);
        if (!string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase)) return fallback;

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 64 * 1024, true);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();
            var baseName = Path.GetFileName(entry.FullName);
            if (!baseName.Equals("BUILD_MANIFEST.json", StringComparison.OrdinalIgnoreCase) &&
                !baseName.Equals("RESULT_MANIFEST.json", StringComparison.OrdinalIgnoreCase)) continue;
            if (entry.Length > 1024 * 1024) throw new InvalidDataException($"Manifest too large: {entry.FullName}");
            await using var entryStream = entry.Open();
            var raw = await JsonSerializer.DeserializeAsync<RawManifest>(entryStream, JsonOptions, ct)
                      ?? throw new InvalidDataException($"Empty manifest: {entry.FullName}");
            var a = fallback;
            a.SchemaVersion = raw.SchemaVersion;
            a.Project = First(raw.Project, a.Project);
            a.Version = First(raw.Version, a.Version);
            a.BuildName = First(raw.BuildName, a.BuildName);
            a.Base = First(raw.Base, a.Base);
            a.Branch = First(raw.Branch, a.Branch);
            a.Feature = First(raw.Feature, a.Feature);
            a.BuildType = First(raw.BuildType, a.BuildType);
            a.ArtifactType = First(raw.ArtifactType, First(PackageRoleToArtifactType(raw.PackageRole), a.ArtifactType));
            a.Stage = First(raw.Stage, a.Stage);
            a.BuildId = First(raw.BuildId, a.BuildId);
            a.ParentBuildId = First(raw.ParentBuildId, a.ParentBuildId);
            a.ForBuildId = First(raw.ForBuildId, a.ForBuildId);
            a.Commit = First(raw.Commit, a.Commit);
            a.SourceMode = First(raw.SourceMode, a.SourceMode);
            a.SourceState = First(raw.SourceState, a.SourceState);
            a.ReleaseState = First(raw.ReleaseState, First(raw.ReleaseStage, a.ReleaseState));
            a.BuildDate = First(raw.BuildDate, First(raw.CreatedAt, a.BuildDate));
            if (raw.Stable == true && string.IsNullOrWhiteSpace(a.BuildType)) a.BuildType = "Stable";
            a.ManifestFound = true;
            a.ManifestName = entry.FullName;
            if (baseName.Equals("RESULT_MANIFEST.json", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(a.ArtifactType))
                a.ArtifactType = "Result";
            return a;
        }
        return fallback;
    }

    public Artifact InspectFilename(string name)
    {
        var a = new Artifact { Name = name, Project = "FreeCam", Status = "待测试" };
        var stem = StripKnownExtension(name);
        if (stem.EndsWith("_Result", StringComparison.OrdinalIgnoreCase))
        {
            stem = stem[..^"_Result".Length];
            a.ArtifactType = "Result";
            a.Status = "已测试";
        }

        var m = DevNameRegex().Match(stem);
        if (m.Success)
        {
            a.Base = m.Groups[1].Value;
            a.Feature = m.Groups[2].Value;
            a.Stage = CanonicalStage(m.Groups[3].Value);
            a.BuildType = StageType(a.Stage);
            if (string.IsNullOrWhiteSpace(a.ArtifactType)) a.ArtifactType = "Runtime";
            return a;
        }

        m = GenericDevNameRegex().Match(stem);
        if (m.Success)
        {
            a.Feature = m.Groups[1].Value;
            a.Stage = CanonicalStage(m.Groups[2].Value);
            a.BuildType = StageType(a.Stage);
            if (string.IsNullOrWhiteSpace(a.ArtifactType)) a.ArtifactType = "Runtime";
            return a;
        }

        if (TryStable(StableRuntimeRegex(), stem, "Runtime", out var version) ||
            TryStable(StableSourceRegex(), stem, "Source", out version) ||
            TryStable(StableRepoRegex(), stem, "Repo", out version) ||
            TryStable(StableShaRegex(), stem, "SHA256", out version) ||
            TryStable(StableReleaseRegex(), stem, "ReleaseNote", out version))
        {
            a.Version = version;
            a.BuildName = version;
            a.BuildType = "StableCandidate";
            a.ArtifactType = StableArtifactType(stem);
            a.ReleaseState = "Candidate";
            a.Stage = version;
        }
        return a;
    }

    private static bool TryStable(Regex regex, string stem, string artifact, out string version)
    {
        var match = regex.Match(stem);
        version = match.Success ? match.Groups[1].Value : "";
        return match.Success;
    }

    private static string StableArtifactType(string stem)
    {
        if (StableSourceRegex().IsMatch(stem)) return "Source";
        if (StableRepoRegex().IsMatch(stem)) return "Repo";
        if (StableShaRegex().IsMatch(stem)) return "SHA256";
        if (StableReleaseRegex().IsMatch(stem)) return "ReleaseNote";
        return "Runtime";
    }

    private static string StripKnownExtension(string name)
    {
        foreach (var ext in new[] { ".zip", ".bundle", ".txt", ".md" })
            if (name.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) return name[..^ext.Length];
        return name;
    }


    private static string PackageRoleToArtifactType(string role)
    {
        return (role ?? "").Trim().ToLowerInvariant() switch
        {
            "runtime" => "Runtime",
            "source" => "Source",
            "repo" or "repository" => "Repo",
            "sha256" or "checksum" => "SHA256",
            "releasenote" or "release_note" => "ReleaseNote",
            "result" => "Result",
            _ => ""
        };
    }

    private static string First(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string CanonicalStage(string value)
    {
        foreach (var kind in new[] { "Test", "Probe", "Experiment", "Regression" })
            if (value.StartsWith(kind, StringComparison.OrdinalIgnoreCase)) return kind + value[kind.Length..];
        return value;
    }

    private static string StageType(string stage)
    {
        if (stage.StartsWith("Test", StringComparison.OrdinalIgnoreCase)) return "Test";
        if (stage.StartsWith("Probe", StringComparison.OrdinalIgnoreCase)) return "Probe";
        if (stage.StartsWith("Experiment", StringComparison.OrdinalIgnoreCase)) return "Experiment";
        if (stage.StartsWith("Regression", StringComparison.OrdinalIgnoreCase)) return "Regression";
        return "";
    }

    [GeneratedRegex(@"^FreeCam_(R[0-9]+(?:\.[0-9]+){0,2})_(.+?)_((?:Test|Probe|Experiment|Regression)[0-9]+(?:\.[0-9]+)*(?:_Fix[0-9]+)*)$", RegexOptions.IgnoreCase)]
    private static partial Regex DevNameRegex();
    [GeneratedRegex(@"^(.+?)_((?:Test|Probe|Experiment|Regression)[0-9]+(?:\.[0-9]+)*(?:_Fix[0-9]+)*)$", RegexOptions.IgnoreCase)]
    private static partial Regex GenericDevNameRegex();
    [GeneratedRegex(@"^FreeCam_(R[0-9]+(?:\.[0-9]+){0,2})$", RegexOptions.IgnoreCase)]
    private static partial Regex StableRuntimeRegex();
    [GeneratedRegex(@"^FreeCam_(R[0-9]+(?:\.[0-9]+){0,2})_Source$", RegexOptions.IgnoreCase)]
    private static partial Regex StableSourceRegex();
    [GeneratedRegex(@"^FreeCam_(R[0-9]+(?:\.[0-9]+){0,2})_Repo$", RegexOptions.IgnoreCase)]
    private static partial Regex StableRepoRegex();
    [GeneratedRegex(@"^FreeCam_(R[0-9]+(?:\.[0-9]+){0,2})_SHA256$", RegexOptions.IgnoreCase)]
    private static partial Regex StableShaRegex();
    [GeneratedRegex(@"^FreeCam_(R[0-9]+(?:\.[0-9]+){0,2})_RELEASE_NOTE$", RegexOptions.IgnoreCase)]
    private static partial Regex StableReleaseRegex();
}
