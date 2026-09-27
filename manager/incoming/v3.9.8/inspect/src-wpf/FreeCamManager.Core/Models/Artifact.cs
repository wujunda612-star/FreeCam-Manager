using System.Text.Json.Serialization;

namespace FreeCamManager.Core.Models;

public sealed class Artifact
{
    [JsonPropertyName("path")] public string Path { get; set; } = "";
    [JsonPropertyName("relative_path")] public string RelativePath { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("schema_version")] public int SchemaVersion { get; set; }
    [JsonPropertyName("project")] public string Project { get; set; } = "";
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("build_name")] public string BuildName { get; set; } = "";
    [JsonPropertyName("base")] public string Base { get; set; } = "";
    [JsonPropertyName("branch")] public string Branch { get; set; } = "";
    [JsonPropertyName("feature")] public string Feature { get; set; } = "";
    [JsonPropertyName("build_type")] public string BuildType { get; set; } = "";
    [JsonPropertyName("artifact_type")] public string ArtifactType { get; set; } = "";
    [JsonPropertyName("stage")] public string Stage { get; set; } = "";
    [JsonPropertyName("build_id")] public string BuildId { get; set; } = "";
    [JsonPropertyName("parent_build_id")] public string ParentBuildId { get; set; } = "";
    [JsonPropertyName("for_build_id")] public string ForBuildId { get; set; } = "";
    [JsonPropertyName("commit")] public string Commit { get; set; } = "";
    [JsonPropertyName("source_mode")] public string SourceMode { get; set; } = "";
    [JsonPropertyName("source_state")] public string SourceState { get; set; } = "";
    [JsonPropertyName("release_state")] public string ReleaseState { get; set; } = "";
    [JsonPropertyName("build_date")] public string BuildDate { get; set; } = "";
    [JsonPropertyName("manifest_found")] public bool ManifestFound { get; set; }
    [JsonPropertyName("manifest_name")] public string ManifestName { get; set; } = "";
    [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("category")] public string Category { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("test_status")] public string TestStatus { get; set; } = "";
    [JsonPropertyName("manual_status")] public string ManualStatus { get; set; } = "";
    [JsonPropertyName("launch_override_relative")] public string LaunchOverrideRelative { get; set; } = "";
    [JsonPropertyName("drag_override_path")] public string DragOverridePath { get; set; } = "";
    [JsonPropertyName("drag_override_relative")] public string DragOverrideRelative { get; set; } = "";
    [JsonPropertyName("testing_path")] public string TestingPath { get; set; } = "";
    [JsonPropertyName("testing_relative_path")] public string TestingRelativePath { get; set; } = "";
    [JsonPropertyName("result_path")] public string ResultPath { get; set; } = "";
    [JsonPropertyName("result_relative_path")] public string ResultRelativePath { get; set; } = "";
    [JsonPropertyName("last_tested_at")] public string LastTestedAt { get; set; } = "";
    [JsonPropertyName("test_started_at")] public string TestStartedAt { get; set; } = "";
    [JsonPropertyName("tags")] public List<string> Tags { get; set; } = [];
    [JsonPropertyName("notes")] public string Notes { get; set; } = "";
    [JsonPropertyName("favorite")] public bool Favorite { get; set; }
    [JsonPropertyName("rating")] public int Rating { get; set; }
    [JsonPropertyName("protected")] public bool Protected { get; set; }
    [JsonPropertyName("paired_build_id")] public string PairedBuildId { get; set; } = "";
    [JsonPropertyName("imported_at")] public string ImportedAt { get; set; } = "";
    [JsonPropertyName("duplicate_of")] public string DuplicateOf { get; set; } = "";
    [JsonPropertyName("auto_delete_at")] public string AutoDeleteAt { get; set; } = "";

    public Artifact Clone() => new()
    {
        Path = Path, RelativePath = RelativePath, Name = Name, SchemaVersion = SchemaVersion, Project = Project, Version = Version, BuildName = BuildName, Base = Base,
        Branch = Branch, Feature = Feature, BuildType = BuildType, ArtifactType = ArtifactType,
        Stage = Stage, BuildId = BuildId, ParentBuildId = ParentBuildId, ForBuildId = ForBuildId,
        Commit = Commit, SourceMode = SourceMode, SourceState = SourceState, ReleaseState = ReleaseState,
        BuildDate = BuildDate, ManifestFound = ManifestFound, ManifestName = ManifestName, Sha256 = Sha256,
        Size = Size, Category = Category, Status = Status, TestStatus = TestStatus, ManualStatus = ManualStatus,
        LaunchOverrideRelative = LaunchOverrideRelative, DragOverridePath = DragOverridePath, DragOverrideRelative = DragOverrideRelative,
        TestingPath = TestingPath, TestingRelativePath = TestingRelativePath, ResultPath = ResultPath, ResultRelativePath = ResultRelativePath,
        LastTestedAt = LastTestedAt, TestStartedAt = TestStartedAt, Tags = [.. Tags], Notes = Notes, Favorite = Favorite, Rating = Rating,
        Protected = Protected, PairedBuildId = PairedBuildId, ImportedAt = ImportedAt, DuplicateOf = DuplicateOf, AutoDeleteAt = AutoDeleteAt
    };
}
