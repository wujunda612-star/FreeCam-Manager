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
        [JsonPropertyName("freeCamVersion")] public string FreeCamVersion { get; set; } = "";
        [JsonPropertyName("gameVersion")] public string GameVersion { get; set; } = "";
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

        // Root manifest owns the package. A nested Source/BUILD_MANIFEST.json must
        // never shadow the runtime BUILD_MANIFEST.json just because it happens to
        // appear first in ZIP entry order.
        var manifestEntries = archive.Entries
            .Where(entry => IsManifestName(Path.GetFileName(entry.FullName)))
            .OrderBy(ManifestEntryRank)
            .ThenBy(entry => entry.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var entry in manifestEntries)
        {
            ct.ThrowIfCancellationRequested();
            var baseName = Path.GetFileName(entry.FullName);
            if (entry.Length > 1024 * 1024) throw new InvalidDataException($"Manifest too large: {entry.FullName}");
            await using var entryStream = entry.Open();
            var raw = await JsonSerializer.DeserializeAsync<RawManifest>(entryStream, JsonOptions, ct)
                      ?? throw new InvalidDataException($"Empty manifest: {entry.FullName}");
            var a = fallback;
            a.SchemaVersion = raw.SchemaVersion;
            // The package's root manifest is authoritative. A valid buildName is
            // only used as fallback if the physical filename is ambiguous.
            var named = InspectFilename(First(raw.BuildName, Path.GetFileNameWithoutExtension(path)) + ".zip");
            a.Project = First(raw.Project, First(named.Project, a.Project));
            a.Version = First(raw.FreeCamVersion, First(raw.Version, a.Version));
            a.BuildName = First(raw.BuildName, a.BuildName);
            a.Base = First(raw.Base, a.Base);
            a.Branch = First(raw.Branch, a.Branch);
            a.Feature = First(raw.Feature, First(named.Feature, a.Feature));
            a.BuildType = First(raw.BuildType, a.BuildType);
            a.ArtifactType = First(raw.ArtifactType, First(PackageRoleToArtifactType(raw.PackageRole), a.ArtifactType));
            // Historical packs wrote pipeline descriptions into stage. Do not
            // mistake a technical description for the standard stage token.
            // Recover the canonical token from the actual Build name instead.
            var declaredStage = CanonicalStage(raw.Stage);
            a.Stage = IsStandardStage(declaredStage)
                ? declaredStage
                : First(named.Stage, a.Stage);
            if (!IsStandardStage(a.Stage)) a.Stage = "";
            NormalizeStageBuildType(a);
            a.BuildId = First(raw.BuildId, a.BuildId);
            a.ParentBuildId = First(raw.ParentBuildId, a.ParentBuildId);
            a.ForBuildId = First(raw.ForBuildId, a.ForBuildId);
            a.Commit = First(raw.Commit, a.Commit);
            a.SourceMode = First(raw.SourceMode, a.SourceMode);
            a.SourceState = First(raw.SourceState, a.SourceState);
            a.ReleaseState = First(raw.ReleaseState, First(raw.ReleaseStage, a.ReleaseState));
            a.BuildDate = First(raw.BuildDate, First(raw.CreatedAt, a.BuildDate));
            if (raw.Stable == true && string.IsNullOrWhiteSpace(a.BuildType)) a.BuildType = "Stable";
            ApplyReleaseCandidateMetadata(a);
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
        var a = new Artifact { Name = name, Status = "待测试" };
        var stem = StripDuplicateSuffix(StripKnownExtension(name));
        if (stem.EndsWith("_Result", StringComparison.OrdinalIgnoreCase))
        {
            stem = stem[..^"_Result".Length];
            a.ArtifactType = "Result";
            a.Status = "已测试";
        }

        // Explicit RC without a feature must run BEFORE the generic
        // FreeCam development matcher, otherwise W37 becomes the feature.
        var match = ReleaseCandidateNameRegex().Match(stem);
        if (match.Success)
        {
            a.Project = "FreeCam";
            a.Version = match.Groups[1].Value;
            a.Base = a.Version;
            a.BuildName = stem;
            a.Feature = "ReleaseCandidate";
            a.Stage = "RC" + match.Groups[2].Value[2..];
            a.BuildType = "ReleaseCandidate";
            a.ReleaseState = "release-candidate";
            if (a.ArtifactType.Length == 0) a.ArtifactType = "Runtime";
            return a;
        }

        match = DevNameRegex().Match(stem);
        if (match.Success)
        {
            a.Project = "FreeCam";
            a.Base = match.Groups[1].Value;
            a.Version = a.Base;
            a.Feature = match.Groups[2].Value;
            a.Stage = CanonicalStage(match.Groups[3].Value);
            a.BuildType = StageType(a.Stage);
            if (a.ArtifactType.Length == 0) a.ArtifactType = "Runtime";
            return a;
        }

        // WW37_GIBloom_Probe2.39 has a distinct game-version prefix.
        // WW37 is not a part of the feature name.
        match = WwDevNameRegex().Match(stem);
        if (match.Success)
        {
            a.Project = "WW底层索引";
            a.Base = "WW" + match.Groups[1].Value;
            a.Feature = match.Groups[2].Value;
            a.Stage = CanonicalStage(match.Groups[3].Value);
            a.BuildType = StageType(a.Stage);
            if (a.ArtifactType.Length == 0) a.ArtifactType = "Runtime";
            return a;
        }

        match = WwReleaseNameRegex().Match(stem);
        if (match.Success)
        {
            a.Project = "WW底层索引";
            a.Base = "WW" + match.Groups[1].Value;
            a.Feature = "IndexLibrary";
            a.Stage = "Fix" + match.Groups[2].Value;
            a.BuildType = "Release";
            if (a.ArtifactType.Length == 0) a.ArtifactType = "Runtime";
            return a;
        }

        match = GenericDevNameRegex().Match(stem);
        if (match.Success)
        {
            a.Project = "FreeCam";
            a.Feature = match.Groups[1].Value;
            a.Stage = CanonicalStage(match.Groups[2].Value);
            a.BuildType = StageType(a.Stage);
            if (a.ArtifactType.Length == 0) a.ArtifactType = "Runtime";
            return a;
        }

        // Result is terminal: never let a _Result ZIP become a stable runtime.
        if (!Eq(a.ArtifactType, "Result") &&
            (TryStable(StableRuntimeRegex(), stem, "Runtime", out var version) ||
             TryStable(StableSourceRegex(), stem, "Source", out version) ||
             TryStable(StableRepoRegex(), stem, "Repo", out version) ||
             TryStable(StableShaRegex(), stem, "SHA256", out version) ||
             TryStable(StableReleaseRegex(), stem, "ReleaseNote", out version)))
        {
            a.Project = "FreeCam";
            a.Version = version;
            a.BuildName = stem;
            a.BuildType = "StableCandidate";
            a.ArtifactType = StableArtifactType(stem);
            a.ReleaseState = "Candidate";
            a.Stage = "Stable";
        }
        return a;
    }

    private static void ApplyReleaseCandidateMetadata(Artifact a)
    {
        if (!string.Equals(a.ReleaseState, "release-candidate", StringComparison.OrdinalIgnoreCase)) return;
        if (string.IsNullOrWhiteSpace(a.Feature)) a.Feature = "ReleaseCandidate";
        if (string.IsNullOrWhiteSpace(a.Stage))
        {
            var source = !string.IsNullOrWhiteSpace(a.BuildName) ? a.BuildName : a.Name;
            var match = RcSuffixRegex().Match(StripDuplicateSuffix(StripKnownExtension(source)));
            a.Stage = match.Success ? match.Groups[1].Value.ToUpperInvariant() : "RC";
        }
        if (string.IsNullOrWhiteSpace(a.BuildType)) a.BuildType = "ReleaseCandidate";
        if (string.IsNullOrWhiteSpace(a.ArtifactType)) a.ArtifactType = "Runtime";
    }

    private static bool IsManifestName(string baseName)
        => baseName.Equals("BUILD_MANIFEST.json", StringComparison.OrdinalIgnoreCase)
           || baseName.Equals("RESULT_MANIFEST.json", StringComparison.OrdinalIgnoreCase);

    private static int ManifestEntryRank(ZipArchiveEntry entry)
    {
        var normalized = entry.FullName.Replace('\\', '/').Trim('/');
        var depth = normalized.Count(ch => ch == '/');
        return depth == 0 ? 0 : 10 + depth;
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

    private static string StripDuplicateSuffix(string stem)
        => DuplicateSuffixRegex().Replace(stem, "");

    private static string PackageRoleToArtifactType(string role)
    {
        return (role ?? "").Trim().ToLowerInvariant() switch
        {
            "runtime" or "stable" => "Runtime",
            "source" or "source-full" => "Source",
            "repo" or "repository" => "Repo",
            "sha256" or "checksum" => "SHA256",
            "releasenote" or "release_note" => "ReleaseNote",
            "result" => "Result",
            _ => ""
        };
    }

    private static string First(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static bool Eq(string? a, string b) =>
        string.Equals(a?.Trim(), b, StringComparison.OrdinalIgnoreCase);

    // A stage is a token, not a free-form description from older scanners.
    private static bool IsStandardStage(string? stage) =>
        stage is not null && (StageTokenRegex().IsMatch(stage) ||
            string.Equals(stage, "Stable", StringComparison.OrdinalIgnoreCase));

    private static string CanonicalStage(string value)
    {
        // Preserve RegN as the canonical new-stage token while retaining RegressionN legacy imports.
        foreach (var kind in new[] { "Regression", "Experiment", "Develop", "Probe", "Test", "Stable", "Fix", "Reg", "RC" })
            if (value.StartsWith(kind, StringComparison.OrdinalIgnoreCase)) return kind + value[kind.Length..];
        return value;
    }

    private static string StageType(string stage)
    {
        if (stage.StartsWith("Test", StringComparison.OrdinalIgnoreCase)) return "Test";
        if (stage.StartsWith("Probe", StringComparison.OrdinalIgnoreCase)) return "Probe";
        if (stage.StartsWith("Experiment", StringComparison.OrdinalIgnoreCase)) return "Experiment";
        if (stage.StartsWith("Regression", StringComparison.OrdinalIgnoreCase) || stage.StartsWith("Reg", StringComparison.OrdinalIgnoreCase)) return "Regression";
        if (stage.StartsWith("Develop", StringComparison.OrdinalIgnoreCase)) return "Feature";
        if (stage.StartsWith("RC", StringComparison.OrdinalIgnoreCase)) return "ReleaseCandidate";
        if (stage.StartsWith("Fix", StringComparison.OrdinalIgnoreCase)) return "Release";
        return "";
    }

    private static void NormalizeStageBuildType(Artifact a)
    {
        // Stage is the most specific development signal. A package manifest may
        // use a generic BuildType such as Develop/Feature while the filename or
        // Stage says Test/Probe/Regression. Preserve the specific stage semantics
        // so classification cannot fall back to Unknown.
        var inferred = StageType(a.Stage ?? "");
        if (string.IsNullOrWhiteSpace(inferred)) return;
        // buildType=test + stage=ProbeN is legal on FreeCam new packages;
        // buildType=probe is legal for WW. Both keep their declared meaning.
        // Old "develop" and unknown types are normalized by stage.
        if (string.IsNullOrWhiteSpace(a.BuildType)
            || !new[] { "test", "probe", "regression", "experiment", "feature",
                         "release", "stable", "stablecandidate", "releasecandidate" }
                 .Contains(a.BuildType.Trim().ToLowerInvariant()))
            a.BuildType = inferred;
    }

    [GeneratedRegex(@"^(?:WW底层索引(?:库)?_)?WW([0-9]+)_(.+?)_((?:Test|Probe|Experiment|Regression|Reg|Develop|RC)[0-9]+(?:\.[0-9]+)*)$", RegexOptions.IgnoreCase)]
    private static partial Regex WwDevNameRegex();
    [GeneratedRegex(@"^WW底层索引(?:库)?_WW([0-9]+)_Fix([0-9]+)(?:_.+)?$", RegexOptions.IgnoreCase)]
    private static partial Regex WwReleaseNameRegex();
    [GeneratedRegex(@"^(?:Probe|Test|Reg|Regression|RC|Experiment|Develop|Fix)[0-9]+(?:\.[0-9]+)*(?:_Fix[0-9]+)*$", RegexOptions.IgnoreCase)]
    private static partial Regex StageTokenRegex();
    [GeneratedRegex(@"^FreeCam_(R[0-9]+(?:\.[0-9]+){0,2})(?:_W[0-9]+)?_(.+?)_((?:Test|Probe|Experiment|Regression|Reg|Develop|RC)[0-9]+(?:\.[0-9]+)*(?:_Fix[0-9]+)*)$", RegexOptions.IgnoreCase)]
    private static partial Regex DevNameRegex();
    [GeneratedRegex(@"^(.+?)_((?:Test|Probe|Experiment|Regression|Reg|Develop|RC)[0-9]+(?:\.[0-9]+)*(?:_Fix[0-9]+)*)$", RegexOptions.IgnoreCase)]
    private static partial Regex GenericDevNameRegex();
    [GeneratedRegex(@"^FreeCam_(R[0-9]+(?:\.[0-9]+){0,2})(?:_W[0-9]+)?_(RC[0-9]+(?:\.[0-9]+)*)$", RegexOptions.IgnoreCase)]
    private static partial Regex ReleaseCandidateNameRegex();
    [GeneratedRegex(@"_(RC[0-9]+(?:\.[0-9]+)*)$", RegexOptions.IgnoreCase)]
    private static partial Regex RcSuffixRegex();
    [GeneratedRegex(@"\s*\([0-9]+\)$", RegexOptions.CultureInvariant)]
    private static partial Regex DuplicateSuffixRegex();
    [GeneratedRegex(@"^FreeCam_(R[0-9]+(?:\.[0-9]+){0,2})(?:_W[0-9]+)?$", RegexOptions.IgnoreCase)]
    private static partial Regex StableRuntimeRegex();
    [GeneratedRegex(@"^FreeCam_(R[0-9]+(?:\.[0-9]+){0,2})(?:_W[0-9]+)?_Source$", RegexOptions.IgnoreCase)]
    private static partial Regex StableSourceRegex();
    [GeneratedRegex(@"^FreeCam_(R[0-9]+(?:\.[0-9]+){0,2})(?:_W[0-9]+)?_Repo$", RegexOptions.IgnoreCase)]
    private static partial Regex StableRepoRegex();
    [GeneratedRegex(@"^FreeCam_(R[0-9]+(?:\.[0-9]+){0,2})(?:_W[0-9]+)?_SHA256$", RegexOptions.IgnoreCase)]
    private static partial Regex StableShaRegex();
    [GeneratedRegex(@"^FreeCam_(R[0-9]+(?:\.[0-9]+){0,2})(?:_W[0-9]+)?_RELEASE_NOTE$", RegexOptions.IgnoreCase)]
    private static partial Regex StableReleaseRegex();
}
