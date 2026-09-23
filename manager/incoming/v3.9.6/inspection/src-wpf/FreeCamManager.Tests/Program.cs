using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FreeCamManager.Core.Models;
using FreeCamManager.Core.Services;

namespace FreeCamManager.Tests;

internal static class Program
{
    private static int _failed;
    private static int _passed;

    public static async Task<int> Main()
    {
        await Run("Artifact snake_case JSON", ArtifactSnakeCaseJson);
        await Run("Legacy metadata migration", LegacyMetadataMigration);
        await Run("Settings defaults and repair", SettingsDefaultsAndRepair);
        await Run("V3.1 settings defaults and repair", V31SettingsDefaultsAndRepair);
        await Run("V3.2 filename aliases external dictionary", V32FilenameAliasesExternalDictionary);
        await Run("Optional Stable backup does not block startup", OptionalStableBackupDoesNotBlockStartup);
        await Run("Log directory fallback does not block startup", LogDirectoryFallbackDoesNotBlockStartup);
        await Run("Inline metadata persistence", InlineMetadataPersistence);
        await Run("Filename and manifest inspection", FilenameAndManifestInspection);
        await Run("Classification and Chinese labels", ClassificationAndLabels);
        await Run("Stable release version beats base", StableReleaseVersionBeatsBase);
        await Run("Stable manifest schema and candidate repair", StableManifestSchemaAndCandidateRepair);
        await Run("Stable freeze requires runtime/source and auto-generates SHA256", StableFreezeMinimumMaterials);
        await Run("Moved FreeCam root rebases stale artifact paths", MovedRootRebasesStalePaths);
        await Run("V3.8 path rebase merges duplicate target safely", V38PathRebaseMergesDuplicateTargetSafely);
        await Run("V3.9 Manager and index library classification", V39ManagerAndIndexLibraryClassification);
        await Run("V3.9 UI protection and settings contract", V39UiProtectionAndSettingsContract);
        await Run("V3.9 five-star rating locks without auto-unlock", V39FiveStarLocksWithoutAutoUnlock);
        await Run("SHA256", Sha256Test);
        await Run("Safe Testing extraction", SafeExtraction);
        await Run("One-click test workspace prepares and discovers launcher", OneClickTestWorkspace);
        await Run("Raw test log marks build tested and stores draggable evidence", RawLogMarksTested);
        await Run("Testing cleanup preserves result evidence", TestingCleanupPreservesEvidence);
        await Run("Deleted Testing folder clears workspace flag but keeps tested history", DeletedTestingFolderKeepsTestHistory);
        await Run("Paired Result immediately stores draggable path", PairedResultStoresDraggablePath);
        await Run("V3.1 preferred drag evidence ignores internal marker", PreferredDragEvidenceIgnoresInternalMarker);
        await Run("Manager logging defaults enabled", ManagerLoggingDefaultsEnabled);
        await Run("Manager logging switch is runtime controllable", ManagerLoggingRuntimeSwitch);
        await Run("Discarded build delete schedule persists and cancels", DiscardDeleteSchedulePersistsAndCancels);
        await Run("Due discarded build cleanup preserves evidence", DueDiscardCleanupPreservesEvidence);
        await Run("Results detection", ResultsDetection);
        await Run("Organizer archive and protection", OrganizerArchiveProtection);
        await Run("Inbox two-scan stability and manager classification", InboxWatcherStability);
        await Run("V3.2 manual refresh processes completed download immediately", V32ManualRefreshImmediateScan);
        await Run("WPF list pages avoid DataGrid/ComboBox style regression", WpfListPagesAvoidFragileStyles);
        await Run("Fix13 WPF workflow contract", Fix13WpfWorkflowContract);
        await Run("Fix14 WPF interaction contract", Fix14WpfInteractionContract);
        await Run("V3.2 WPF refresh and filename alias contract", V32WpfRefreshAliasContract);
        await Run("V3.2 Fix2 responsive settings and resizable list columns", V32Fix2ResponsiveColumnsContract);
        await Run("V3.3 online terms update validates SHA and avoids redundant download", V33OnlineTermsUpdate);
        await Run("V3.3 current workspace result beats prior evidence folders", V33CurrentWorkspaceResultPriority);
        await Run("V3.4 settings support system theme", V34SystemThemeSettings);
        await Run("V3.4 manager update manifest and safe staging", V34ManagerUpdateService);
        await Run("V3.4 Fix3 stage display removes duplicated type prefix", V34Fix3StageDisplay);
        await Run("V3.4 Fix4 staging failures persist diagnostics", V34Fix4StageFailureDiagnostics);
        await Run("V3.4 Fix5 manager manifest bypasses mutable-branch cache", V34Fix5ManifestUsesGitHubApi);
        await Run("V3.4 Fix6 parses GitHub Contents envelope and reports local-ahead state", V34Fix6ContentsEnvelopeAndLocalAhead);
        await Run("V3.4 Fix7 coalesces rapid manager update checks", V34Fix7CoalescesRapidUpdateChecks);
        await Run("V3.5 settings and context menu contract", V35SettingsAndContextMenuContract);
        await Run("V3.5 update cleanup keeps diagnostics only", V35UpdateCleanupKeepsDiagnosticsOnly);
        await Run("V3.6 context menu separator uses MenuItem separator key", V36ContextMenuSeparatorUsesMenuItemKey);
        await Run("V3.6 home summary icons share one presentation style", V36HomeSummaryIconsShareOnePresentationStyle);
        await Run("V3.6 single-instance startup contract", V36SingleInstanceStartupContract);
        await Run("V3.7 matched Result ZIP beats raw log", V37MatchedResultZipBeatsRawLog);
        await Run("V3.7 tested raw log upgrades when Result ZIP appears", V37RefreshUpgradesRawLogToResultZip);
        await Run("V3.7 tested status click opens Result location without breaking drag", V37TestedStatusClickContract);
        await Run("V3.7 Fix1 background refresh preserves manual conclusion interaction", V371BackgroundRefreshPreservesManualConclusionInteraction);
        await Run("V3.7 Fix1 corrupted library restores from valid backup", V371CorruptedLibraryRestoresFromBackup);
        await Run("V3.7 Fix1 safe save keeps last-known-good backup", V371SafeSaveKeepsBackup);
        await Run("V3.7 Fix1 root reconciliation rebuilds managed library without moving files", V371RootReconciliationRebuildsManagedLibrary);
        await Run("V3.7 Fix1 root reconciliation preserves existing manual metadata", V371RootReconciliationPreservesManualMetadata);
        await Run("V3.7 Fix1 startup and manual refresh wire recovery reconciliation", V371RecoveryWiringContract);

        Console.WriteLine($"\nTests: {_passed} passed, {_failed} failed");
        return _failed == 0 ? 0 : 1;
    }

    private static Task ArtifactSnakeCaseJson()
    {
        var a = new Artifact { Path = "A.zip", RelativePath = Path.Combine("20_Feature", "A.zip"), BuildId = "B1", ManualStatus = "通过", Rating = 3 };
        var json = JsonSerializer.Serialize(a);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert(root.GetProperty("build_id").GetString() == "B1", "build_id mapping missing");
        Assert(root.GetProperty("relative_path").GetString() == Path.Combine("20_Feature", "A.zip"), "relative_path mapping missing");
        Assert(root.GetProperty("manual_status").GetString() == "通过", "manual_status mapping missing");
        return Task.CompletedTask;
    }

    private static async Task LegacyMetadataMigration()
    {
        var dir = TempDir();
        var file = Path.Combine(dir, "library.json");
        await File.WriteAllTextAsync(file, "{\"items\":[{\"path\":\"A.zip\",\"favorite\":true,\"status\":\"待复测\"}]}");
        var lib = await LibraryService.LoadAsync(file);
        var item = lib.ByPath("A.zip")!;
        Assert(item.Rating == 5, "favorite=true should migrate to rating=5");
        Assert(item.ManualStatus == "待复测", "legacy manual status migration failed");
    }

    private static async Task SettingsDefaultsAndRepair()
    {
        var dir = TempDir();
        var file = Path.Combine(dir, "app", "settings.json");
        var service = new SettingsService();
        var cfg = await service.LoadOrCreateAsync(file, Path.Combine(dir, "home"));
        Assert(cfg.ScanSeconds == 3 && cfg.Theme == "system", "default settings invalid");
        Assert(cfg.InboxDir == Path.Combine(cfg.RootDir, "00_Downloa"), "default inbox should live under FreeCam root");
        cfg.RootDir = Path.Combine(dir, "root");
        cfg.InboxDir = Path.Combine(dir, "inbox");
        cfg.LogDir = "";
        cfg.Theme = "neon";
        await service.SaveAsync(file, cfg);
        var repaired = await service.LoadOrCreateAsync(file, Path.Combine(dir, "home"));
        Assert(repaired.LogDir == Path.Combine(repaired.RootDir, "Logs"), "log dir repair failed");
        Assert(repaired.Theme == "system", "theme repair failed");
        service.EnsureDirectories(repaired);
        Assert(Directory.Exists(Path.Combine(repaired.RootDir, "01_Testing")), "01_Testing missing");
    }


    private static async Task V31SettingsDefaultsAndRepair()
    {
        var dir = TempDir();
        var file = Path.Combine(dir, "settings.json");
        var service = new SettingsService();
        var cfg = service.CreateDefault(dir);
        Assert(cfg.DiscardAutoDeleteDays == 0, "discard auto-delete should default to disabled");
        Assert(cfg.HideFreeCamPrefix, "FreeCam_ prefix should be hidden by default");
        Assert(cfg.DevelopmentFileColumnWidth == 0, "file column width 0 should mean auto-fill");
        Assert(cfg.DevelopmentFeatureColumnWidth == 104 && cfg.DevelopmentStageColumnWidth == 74, "development column defaults changed unexpectedly");
        Assert(cfg.HistoryFileColumnWidth == 0 && cfg.HistoryFeatureColumnWidth == 104 && cfg.HistoryStageColumnWidth == 74, "history column defaults changed unexpectedly");

        cfg.DiscardAutoDeleteDays = 2;
        cfg.DevelopmentFileColumnWidth = 40;
        cfg.DevelopmentFeatureColumnWidth = 20;
        cfg.DevelopmentStageColumnWidth = 10;
        cfg.HistoryFileColumnWidth = 40;
        cfg.HistoryFeatureColumnWidth = 20;
        cfg.HistoryStageColumnWidth = 10;
        await service.SaveAsync(file, cfg);
        var repaired = await service.LoadOrCreateAsync(file, dir);
        Assert(repaired.DiscardAutoDeleteDays == 0, "unsupported discarded auto-delete policy should repair to disabled");
        Assert(repaired.DevelopmentFileColumnWidth == 0, "too-small file width should repair to auto-fill");
        Assert(repaired.DevelopmentFeatureColumnWidth == 104 && repaired.DevelopmentStageColumnWidth == 74, "too-small resizable columns should repair to defaults");
        Assert(repaired.HistoryFileColumnWidth == 0 && repaired.HistoryFeatureColumnWidth == 104 && repaired.HistoryStageColumnWidth == 74, "too-small history widths should repair to defaults");
    }


    private static async Task V32FilenameAliasesExternalDictionary()
    {
        var dir = TempDir();
        var file = Path.Combine(dir, "Config", "FilenameTerms.json");
        var service = new FilenameAliasService(file);
        var count = await service.EnsureAndReloadAsync();
        Assert(count >= 10, "default filename term dictionary should be populated");
        Assert(File.Exists(file), "external filename term dictionary was not created");
        Assert(service.Translate("WW36_ExternalVerify_Probe1.3.zip") == "WW36_外部验证_探针1.3.zip", "default alias translation failed");
        Assert(service.Translate("R40.4.0_Env_Probe1.6_Bundle.zip") == "R40.4.0_环境_探针1.6_整合包.zip", "Env/Bundle alias translation failed");

        await File.WriteAllTextAsync(file, "[{\"from\":\"ExternalVerify\",\"to\":\"外验\"},{\"from\":\"Probe\",\"to\":\"探针\"}]");
        var reloaded = await service.ReloadAsync();
        Assert(reloaded == 2, "custom filename term reload count invalid");
        Assert(service.Translate("WW36_ExternalVerify_Probe1.3.zip") == "WW36_外验_探针1.3.zip", "reloaded external dictionary was not applied");

        var settings = new SettingsService().CreateDefault(dir);
        Assert(settings.ShowFilenameAliases, "filename aliases should default to visible");
        Assert(settings.ShowFeatureAliases, "feature aliases should default to visible");
        Assert(settings.ShowStageAliases, "stage aliases should default to visible");
    }

    private static async Task OptionalStableBackupDoesNotBlockStartup()
    {
        var dir = TempDir();
        var root = Path.Combine(dir, "root");
        var inbox = Path.Combine(dir, "inbox");
        var blockedBackup = Path.Combine(dir, "backup-blocker");
        await File.WriteAllTextAsync(blockedBackup, "this is a file, not a directory");

        var cfg = new AppSettings
        {
            RootDir = root,
            InboxDir = inbox,
            LogDir = Path.Combine(root, "Logs"),
            StableBackupDir = blockedBackup,
            ScanSeconds = 3,
            Theme = "dark"
        };

        var settings = new SettingsService();
        settings.EnsureDirectories(cfg);
        Assert(Directory.Exists(root), "root should be created");
        Assert(Directory.Exists(inbox), "inbox should be created");
        Assert(File.Exists(blockedBackup), "optional Stable backup path must not be touched at startup");

        var source = Path.Combine(inbox, "FreeCam_R40.3.1_LookAt_Test3.zip");
        using (var zip = ZipFile.Open(source, ZipArchiveMode.Create))
        {
            var e = zip.CreateEntry("BUILD_MANIFEST.json");
            await using var stream = e.Open();
            await JsonSerializer.SerializeAsync(stream, new Dictionary<string, object?>
            {
                ["Base"] = "R40.3.1", ["Feature"] = "LookAt", ["Stage"] = "Test3",
                ["BuildType"] = "Test", ["Branch"] = "experiment/lookat",
                ["ArtifactType"] = "Runtime", ["BuildId"] = "B3"
            });
        }

        var library = await LibraryService.LoadAsync(Path.Combine(dir, "library.json"));
        var organizer = new OrganizerService(root, blockedBackup, library, new ManifestService(), new ClassificationService(), new HashService());
        var indexed = await organizer.ProcessAsync(source);
        Assert(indexed.Category == "Experiment", "normal archive processing must not depend on Stable backup access");
        Assert(File.Exists(blockedBackup), "normal archive processing must not touch Stable backup path");
    }

    private static async Task LogDirectoryFallbackDoesNotBlockStartup()
    {
        var dir = TempDir();
        var blocked = Path.Combine(dir, "blocked-log-path");
        await File.WriteAllTextAsync(blocked, "this is a file, not a directory");
        var fallback = Path.Combine(dir, "fallback-logs");

        var log = LogService.TryCreateWithFallback(blocked, "test", out var usedDirectory, fallback);
        Assert(log is not null, "log fallback should return a usable logger");
        Assert(string.Equals(Path.GetFullPath(usedDirectory), Path.GetFullPath(fallback), StringComparison.OrdinalIgnoreCase), "fallback log directory was not selected");
        log!.Event("TEST_LOG");
        await log.DisposeAsync();
        Assert(Directory.Exists(fallback), "fallback log directory missing");
        Assert(Directory.EnumerateFiles(fallback, "*.log").Any(), "fallback log file missing");
    }

    private static async Task InlineMetadataPersistence()
    {
        var dir = TempDir();
        var file = Path.Combine(dir, "library.json");
        var lib = await LibraryService.LoadAsync(file);
        var path = Path.Combine(dir, "build.zip");
        lib.Upsert(new Artifact { Path = path, Name = "build.zip" });
        Assert(lib.SetManualStatus(path, "通过"), "manual status update failed");
        Assert(lib.SetRating(path, 4), "rating update failed");
        var (locked, found) = lib.ToggleProtected(path);
        Assert(found && locked, "lock toggle failed");
        Assert(lib.SetTags(path, ["关键", "复测"]), "tags update failed");
        Assert(lib.SetNotes(path, "镜头抖动"), "notes update failed");
        await lib.SaveAsync();
        var loaded = await LibraryService.LoadAsync(file);
        var got = loaded.ByPath(path)!;
        Assert(got.ManualStatus == "通过" && got.Rating == 4 && got.Protected, "metadata not persisted");
        Assert(got.Tags.Count == 2 && got.Notes == "镜头抖动", "tags/notes not persisted");
        Assert(loaded.SetRating(path, 0), "rating zero failed");
        Assert(loaded.ByPath(path)!.Rating == 0, "rating zero not applied");
    }

    private static async Task FilenameAndManifestInspection()
    {
        var service = new ManifestService();
        var a = service.InspectFilename("FreeCam_R39_NoFade_ModDB_Test1_Fix2_Result.zip");
        Assert(a.Base == "R39" && a.Feature == "NoFade_ModDB" && a.Stage == "Test1_Fix2", "legacy filename parse failed");
        Assert(a.BuildType == "Test" && a.ArtifactType == "Result", "legacy result type failed");
        var generic = service.InspectFilename("BridgeControl_Test6.2_Result.zip");
        Assert(generic.Feature == "BridgeControl" && generic.Stage == "Test6.2", "generic legacy parse failed");
        var stable = service.InspectFilename("FreeCam_R40.3.1.zip");
        Assert(stable.BuildType == "StableCandidate" && stable.ReleaseState == "Candidate", "filename must not confirm Stable");

        var dir = TempDir();
        var zipPath = Path.Combine(dir, "FreeCam_R40.3.1_LookAt_Test112.1.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("_BuildMeta/BUILD_MANIFEST.json");
            await using var w = entry.Open();
            await JsonSerializer.SerializeAsync(w, new Dictionary<string, object?>
            {
                ["SchemaVersion"] = 1, ["Base"] = "R40.3.1", ["Branch"] = "experiment/lookat",
                ["Feature"] = "LookAt", ["BuildType"] = "Test", ["Stage"] = "Test112.1",
                ["BuildId"] = "FC-LOOKAT-1121", ["ArtifactType"] = "Runtime"
            });
        }
        var inspected = await service.InspectAsync(zipPath);
        Assert(inspected.ManifestFound && inspected.BuildId == "FC-LOOKAT-1121" && inspected.Branch == "experiment/lookat", "manifest override failed");
    }

    private static Task ClassificationAndLabels()
    {
        var service = new ClassificationService();
        var feature = new Artifact { Feature = "LookAt", Stage = "Test12.1", Branch = "feature/lookat", BuildType = "Test" };
        var d = service.Plan(feature);
        Assert(d.Category == "Feature" && d.RelativeDirectory.Contains("20_Feature"), "feature route failed");
        var probe = new Artifact { Category = "Experiment", BuildType = "Probe" };
        Assert(service.CategoryLabel(probe) == "实验 · 探针", "probe Chinese label failed");
        Assert(service.MatchesDevelopmentFilter(new Artifact { Category = "Experiment", BuildType = "Test", Stage = "Test2" }, DevelopmentFilter.Test), "test filter failed");
        Assert(service.IsManagerArtifact(new Artifact { Name = "FreeCam_Manager_v3.0.zip" }), "manager package should be hidden");
        var stable = service.Plan(new Artifact { Base = "R40.4.0", BuildType = "Stable", ReleaseState = "Stable" });
        Assert(stable.Category == "StableCandidate" && stable.NeedsStableConfirmation, "stable must remain candidate");
        return Task.CompletedTask;
    }

    private static Task StableReleaseVersionBeatsBase()
    {
        var service = new ClassificationService();
        var stable = new Artifact
        {
            Name = "FreeCam_R40.4.0.zip",
            Base = "R40.3.1",
            Stage = "R40.4.0",
            BuildType = "Stable",
            ReleaseState = "Stable",
            ArtifactType = "Runtime"
        };
        var decision = service.Plan(stable);
        Assert(decision.Category == "StableCandidate", "stable must remain candidate before manual confirmation");
        Assert(decision.RelativeDirectory.EndsWith(Path.Combine("Stable_Candidate", "R40.4.0"), StringComparison.OrdinalIgnoreCase),
            $"stable candidate used base instead of release version: {decision.RelativeDirectory}");
        return Task.CompletedTask;
    }

    private static async Task StableManifestSchemaAndCandidateRepair()
    {
        var dir = TempDir();
        var zipPath = Path.Combine(dir, "FreeCam_R40.4.0.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("BUILD_MANIFEST.json");
            await using var stream = entry.Open();
            await JsonSerializer.SerializeAsync(stream, new Dictionary<string, object?>
            {
                ["schemaVersion"] = 1,
                ["project"] = "FreeCam",
                ["version"] = "R40.4.0",
                ["buildName"] = "R40.4.0",
                ["buildId"] = "FC-20260912-R4040-STABLE-001",
                ["packageRole"] = "runtime",
                ["buildType"] = "stable",
                ["stable"] = true,
                ["base"] = "R40.3.1",
                ["branch"] = "main",
                ["sourceMode"] = "full",
                ["sourceState"] = "clean",
                ["createdAt"] = "2026-09-12T20:36:36+08:00",
                ["releaseStage"] = "stable"
            });
        }

        var manifest = new ManifestService();
        var inspected = await manifest.InspectAsync(zipPath);
        Assert(inspected.Version == "R40.4.0", "manifest version was not preserved");
        Assert(inspected.BuildName == "R40.4.0", "manifest buildName was not preserved");
        Assert(inspected.Base == "R40.3.1", "manifest base lineage was lost");
        Assert(inspected.ArtifactType == "Runtime", "packageRole runtime mapping failed");
        Assert(StableVersionResolver.Resolve(inspected) == "R40.4.0", "stable resolver must prefer release version over base");

        var decision = new ClassificationService().Plan(inspected);
        Assert(decision.RelativeDirectory.EndsWith(Path.Combine("Stable_Candidate", "R40.4.0"), StringComparison.OrdinalIgnoreCase),
            $"manifest candidate routed to wrong version directory: {decision.RelativeDirectory}");

        var root = Path.Combine(dir, "root");
        var wrongDir = Path.Combine(root, "90_Unknown", "Stable_Candidate", "R40.3.1");
        Directory.CreateDirectory(wrongDir);
        var wrongPath = Path.Combine(wrongDir, "FreeCam_R40.4.0.zip");
        File.Copy(zipPath, wrongPath);
        var oldRecord = new Artifact
        {
            Path = wrongPath, Name = "FreeCam_R40.4.0.zip", Base = "R40.3.1", Stage = "R40.4.0",
            BuildType = "Stable", ReleaseState = "Stable", ArtifactType = "Runtime", Category = "StableCandidate"
        };
        var library = LibraryService.CreateInMemory([oldRecord]);
        var organizer = new OrganizerService(root, "", library, manifest, new ClassificationService(), new HashService());
        var repaired = await organizer.RepairStableCandidateDirectoriesAsync();
        Assert(repaired == 1, $"expected one repaired stable candidate, got {repaired}");
        var repairedItem = library.Snapshot().Single();
        Assert(repairedItem.Path.Contains(Path.Combine("Stable_Candidate", "R40.4.0"), StringComparison.OrdinalIgnoreCase),
            $"old candidate path was not repaired: {repairedItem.Path}");
        Assert(repairedItem.Version == "R40.4.0", "repair did not persist release version metadata");
        Assert(!Directory.Exists(wrongDir), "empty wrong candidate directory should be removed");
    }

    private static async Task StableFreezeMinimumMaterials()
    {
        var dir = TempDir();
        var root = Path.Combine(dir, "root");
        var candidateDir = Path.Combine(root, "90_Unknown", "Stable_Candidate", "R40.4.0");
        Directory.CreateDirectory(candidateDir);
        var runtimePath = Path.Combine(candidateDir, "FreeCam_R40.4.0.zip");
        var sourcePath = Path.Combine(candidateDir, "FreeCam_R40.4.0_Source.zip");
        await File.WriteAllBytesAsync(runtimePath, Encoding.UTF8.GetBytes("runtime"));
        await File.WriteAllBytesAsync(sourcePath, Encoding.UTF8.GetBytes("source"));

        var hash = new HashService();
        var runtimeHash = await hash.FileSha256Async(runtimePath);
        var sourceHash = await hash.FileSha256Async(sourcePath);
        var library = LibraryService.CreateInMemory([
            new Artifact { Path = runtimePath, Name = Path.GetFileName(runtimePath), Version = "R40.4.0", BuildName = "R40.4.0", Base = "R40.3.1", BuildType = "Stable", ReleaseState = "Stable", ArtifactType = "Runtime", Category = "StableCandidate", Sha256 = runtimeHash },
            new Artifact { Path = sourcePath, Name = Path.GetFileName(sourcePath), Version = "R40.4.0", BuildName = "R40.4.0", Base = "R40.3.1", BuildType = "Stable", ReleaseState = "Stable", ArtifactType = "Source", Category = "StableCandidate", Sha256 = sourceHash }
        ]);
        var organizer = new OrganizerService(root, "", library, new ManifestService(), new ClassificationService(), hash);
        await organizer.ConfirmStableAsync("R40.4.0");

        var stable = library.Snapshot().Where(x => x.Category == "Stable").ToList();
        Assert(stable.Any(x => x.ArtifactType == "Runtime"), "runtime should be frozen");
        Assert(stable.Any(x => x.ArtifactType == "Source"), "source should be frozen");
        var checksum = stable.SingleOrDefault(x => x.ArtifactType == "SHA256");
        Assert(checksum is not null, "SHA256 artifact should be generated automatically");
        Assert(File.Exists(checksum!.Path), "SHA256 file should exist after freeze");
        var checksumText = await File.ReadAllTextAsync(checksum.Path);
        Assert(checksumText.Contains(runtimeHash, StringComparison.OrdinalIgnoreCase) && checksumText.Contains(Path.GetFileName(runtimePath), StringComparison.Ordinal), "SHA256 file missing runtime hash");
        Assert(checksumText.Contains(sourceHash, StringComparison.OrdinalIgnoreCase) && checksumText.Contains(Path.GetFileName(sourcePath), StringComparison.Ordinal), "SHA256 file missing source hash");
        Assert(stable.All(x => x.Protected), "all frozen Stable artifacts should be protected");
        Assert(!stable.Any(x => x.ArtifactType == "Repo"), "Repo.bundle must remain optional");

        var missingRoot = Path.Combine(dir, "missing-root");
        var missingCandidateDir = Path.Combine(missingRoot, "90_Unknown", "Stable_Candidate", "R40.5.0");
        Directory.CreateDirectory(missingCandidateDir);
        var onlyRuntimePath = Path.Combine(missingCandidateDir, "FreeCam_R40.5.0.zip");
        await File.WriteAllTextAsync(onlyRuntimePath, "runtime-only");
        var missingLibrary = LibraryService.CreateInMemory([
            new Artifact { Path = onlyRuntimePath, Name = Path.GetFileName(onlyRuntimePath), Version = "R40.5.0", BuildType = "Stable", ReleaseState = "Stable", ArtifactType = "Runtime", Category = "StableCandidate" }
        ]);
        var missingOrganizer = new OrganizerService(missingRoot, "", missingLibrary, new ManifestService(), new ClassificationService(), hash);
        var rejected = false;
        try { await missingOrganizer.ConfirmStableAsync("R40.5.0"); }
        catch (InvalidOperationException ex) { rejected = ex.Message.Contains("运行包", StringComparison.Ordinal) && ex.Message.Contains("完整源码", StringComparison.Ordinal); }
        Assert(rejected, "Stable freeze must reject candidates missing runtime or full source");
    }


    private static async Task MovedRootRebasesStalePaths()
    {
        var dir = TempDir();
        var oldRoot = Path.Combine(dir, "old-home", "Documents", "FreeCam");
        var newRoot = Path.Combine(dir, "new-drive", "FreeCam");
        var relativeDir = Path.Combine("90_Unknown", "Stable_Candidate", "R40.4.0");
        var newCandidateDir = Path.Combine(newRoot, relativeDir);
        Directory.CreateDirectory(newCandidateDir);
        var runtimeName = "FreeCam_R40.4.0.zip";
        var sourceName = "FreeCam_R40.4.0_Source.zip";
        var newRuntime = Path.Combine(newCandidateDir, runtimeName);
        var newSource = Path.Combine(newCandidateDir, sourceName);
        await File.WriteAllTextAsync(newRuntime, "runtime");
        await File.WriteAllTextAsync(newSource, "source");

        var oldRuntime = Path.Combine(oldRoot, relativeDir, runtimeName);
        var oldSource = Path.Combine(oldRoot, relativeDir, sourceName);
        var libraryFile = Path.Combine(dir, "library.json");
        var library = LibraryService.CreateInMemory([
            new Artifact { Path = oldRuntime, Name = runtimeName, Version = "R40.4.0", Base = "R40.3.1", ArtifactType = "Runtime", BuildType = "Stable", ReleaseState = "Stable", Category = "StableCandidate" },
            new Artifact { Path = oldSource, Name = sourceName, Version = "R40.4.0", Base = "R40.3.1", ArtifactType = "Source", BuildType = "Stable", ReleaseState = "Stable", Category = "StableCandidate" }
        ]);

        var repaired = await new PathRebaseService().RepairLibraryPathsAsync(library, newRoot);
        Assert(repaired >= 2, $"expected stale paths to be repaired, got {repaired}");
        var items = library.Snapshot();
        Assert(items.Any(x => string.Equals(x.Path, newRuntime, StringComparison.OrdinalIgnoreCase)), "runtime stale path was not rebased");
        Assert(items.Any(x => string.Equals(x.Path, newSource, StringComparison.OrdinalIgnoreCase)), "source stale path was not rebased");
        Assert(items.All(x => x.RelativePath.StartsWith(Path.Combine("90_Unknown", "Stable_Candidate", "R40.4.0"), StringComparison.OrdinalIgnoreCase)), "root-relative path was not persisted");

        var organizer = new OrganizerService(newRoot, "", library, new ManifestService(), new ClassificationService(), new HashService());
        await organizer.ConfirmStableAsync("R40.4.0");
        var stable = library.Snapshot().Where(x => x.Category == "Stable").ToList();
        Assert(stable.Any(x => x.ArtifactType == "Runtime") && stable.Any(x => x.ArtifactType == "Source"), "rebased candidate did not freeze");
        Assert(stable.All(x => x.Path.StartsWith(Path.Combine(newRoot, "10_Stable", "R40.4.0"), StringComparison.OrdinalIgnoreCase)), "Stable freeze used stale root after rebase");
    }

    private static async Task V38PathRebaseMergesDuplicateTargetSafely()
    {
        var dir = TempDir();
        var oldRoot = Path.Combine(dir, "old-root");
        var newRoot = Path.Combine(dir, "new-root");
        var relative = Path.Combine("20_Feature", "FeatureA", "FeatureA_Test1.zip");
        var target = Path.Combine(newRoot, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await File.WriteAllTextAsync(target, "same physical artifact");

        var stale = new Artifact
        {
            Path = Path.Combine(oldRoot, relative),
            RelativePath = relative,
            Name = Path.GetFileName(target),
            BuildId = "B-STALE",
            ManualStatus = "",
            Rating = 5,
            Protected = true,
            Notes = "legacy-note",
            Tags = ["legacy-tag"]
        };
        var current = new Artifact
        {
            Path = target,
            RelativePath = relative,
            Name = Path.GetFileName(target),
            BuildId = "B-CURRENT",
            ManualStatus = "通过",
            Rating = 2,
            Notes = "current-note",
            Tags = ["current-tag"]
        };

        IReadOnlyList<Artifact>? persisted = null;
        var library = LibraryService.CreatePersistent([stale, current], (snapshot, _) =>
        {
            var duplicate = snapshot
                .GroupBy(x => Path.GetFullPath(x.Path), StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(x => x.Count() > 1);
            if (duplicate is not null)
                throw new InvalidDataException($"duplicate path reached persistence: {duplicate.Key}");
            persisted = snapshot.Select(x => x.Clone()).ToList();
            return Task.CompletedTask;
        });

        var repaired = await new PathRebaseService().RepairLibraryPathsAsync(library, newRoot);
        Assert(repaired >= 1, "stale duplicate path was not rebased");
        var items = library.Snapshot();
        Assert(items.Count == 1, $"path collision must merge to one artifact, got {items.Count}");
        var merged = items.Single();
        Assert(string.Equals(Path.GetFullPath(merged.Path), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase), "merged artifact did not keep the authoritative target path");
        Assert(merged.ManualStatus == "通过", "current-path manual conclusion must survive collision merge");
        Assert(merged.Rating == 5, "higher rating from stale metadata must not be lost");
        Assert(merged.Protected, "lock/protection metadata from stale record must not be lost");
        Assert(merged.Tags.Contains("legacy-tag") && merged.Tags.Contains("current-tag"), "tags from both records must be preserved");
        Assert(merged.Notes.Contains("legacy-note", StringComparison.Ordinal) && merged.Notes.Contains("current-note", StringComparison.Ordinal), "notes from both records must be preserved");
        Assert(persisted is { Count: 1 }, "persistent snapshot must contain exactly one artifact for the physical path");
    }

    private static async Task Sha256Test()
    {
        var dir = TempDir();
        var p = Path.Combine(dir, "x");
        await File.WriteAllBytesAsync(p, Encoding.UTF8.GetBytes("abc"));
        var got = await new HashService().FileSha256Async(p);
        Assert(got == "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", $"bad sha256 {got}");
    }

    private static async Task SafeExtraction()
    {
        var dir = TempDir();
        var zipPath = Path.Combine(dir, "FreeCam_R40.3.1_LookAt_Test9.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var e = zip.CreateEntry("Start.cmd");
            await using var w = new StreamWriter(e.Open());
            await w.WriteAsync("echo ok");
        }
        var service = new ExtractionService();
        var testingRoot = Path.Combine(dir, "01_Testing");
        var result = await service.ExtractToTestingAsync(zipPath, testingRoot);
        Assert(result.Status == ExtractionStatus.Extracted && File.Exists(Path.Combine(result.Destination, "Start.cmd")), "testing extraction failed");
        var second = await service.ExtractToTestingAsync(zipPath, testingRoot);
        Assert(second.Status == ExtractionStatus.AlreadyExists, "existing destination should not be overwritten");
        Assert(File.Exists(zipPath), "source zip must remain before organizer archives it");

        var evil = Path.Combine(dir, "Evil.zip");
        using (var zip = ZipFile.Open(evil, ZipArchiveMode.Create))
        {
            var e = zip.CreateEntry("../escape.txt");
            await using var w = new StreamWriter(e.Open());
            await w.WriteAsync("bad");
        }
        var rejected = false;
        try { await service.ExtractToTestingAsync(evil, testingRoot); }
        catch (InvalidDataException) { rejected = true; }
        Assert(rejected, "Zip Slip should be rejected");
    }

    private static async Task OneClickTestWorkspace()
    {
        var dir = TempDir();
        var zipPath = Path.Combine(dir, "FreeCam_R40.4.0_EyeAF_Probe1.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var nested = zip.CreateEntry("payload/Start_Test.cmd");
            await using var w = new StreamWriter(nested.Open());
            await w.WriteAsync("echo test");
        }

        var workspace = new TestWorkspaceService(new ExtractionService());
        var prepared = await workspace.PrepareAsync(zipPath, Path.Combine(dir, "01_Testing"));
        Assert(Directory.Exists(prepared.TestingPath), "one-click prepare did not create Testing folder");
        Assert(prepared.ExtractionStatus == ExtractionStatus.Extracted, "first one-click prepare should extract");
        Assert(prepared.LaunchPath.EndsWith(Path.Combine("payload", "Start_Test.cmd"), StringComparison.OrdinalIgnoreCase), "Start*.cmd launcher was not discovered recursively");

        var second = await workspace.PrepareAsync(zipPath, Path.Combine(dir, "01_Testing"));
        Assert(second.ExtractionStatus == ExtractionStatus.AlreadyExists, "existing Testing folder should be reused on double-click");
        Assert(string.Equals(second.LaunchPath, prepared.LaunchPath, StringComparison.OrdinalIgnoreCase), "reused Testing folder resolved a different launcher");
    }

    private static async Task RawLogMarksTested()
    {
        var dir = TempDir();
        var testing = Path.Combine(dir, "01_Testing", "FreeCam_R40.4.0_EyeAF_Probe1");
        var logs = Path.Combine(testing, "Logs");
        Directory.CreateDirectory(logs);
        var log = Path.Combine(logs, "EyeAF_Probe1.log");
        await File.WriteAllTextAsync(log, "probe result");

        var buildPath = Path.Combine(dir, "30_Experiment", "EyeAF", "Probe1", "FreeCam_R40.4.0_EyeAF_Probe1.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(buildPath)!);
        await File.WriteAllBytesAsync(buildPath, []);
        var library = LibraryService.CreateInMemory([
            new Artifact { Path = buildPath, Name = Path.GetFileName(buildPath), BuildId = "EYEAF-P1", TestingPath = testing, TestStatus = "测试中" }
        ]);
        var refresh = new TestStatusRefreshService(library, new TestResultService(new ManifestService()), dir);
        var changed = await refresh.RefreshAsync();
        var updated = library.ByPath(buildPath)!;
        Assert(changed, "raw test log should change test state");
        Assert(updated.TestStatus == "已测试", "raw test log should mark system state as 已测试");
        Assert(string.Equals(updated.ResultPath, log, StringComparison.OrdinalIgnoreCase), "draggable result/log path was not stored");
        Assert(!string.IsNullOrWhiteSpace(updated.LastTestedAt), "last tested timestamp was not stored");
    }

    private static async Task TestingCleanupPreservesEvidence()
    {
        var dir = TempDir();
        var testing = Path.Combine(dir, "01_Testing", "FreeCam_R40.4.0_EyeAF_Probe1");
        var results = Path.Combine(testing, "Results");
        Directory.CreateDirectory(results);
        var log = Path.Combine(results, "EyeAF_Probe1.log");
        await File.WriteAllTextAsync(log, "keep me");

        var service = new TestResultService(new ManifestService());
        var build = new Artifact { Feature = "EyeAF", Stage = "Probe1", BuildId = "EYEAF-P1", TestingPath = testing };
        var resultRoot = Path.Combine(dir, "40_Result");
        var preserved = await service.PreserveEvidenceAsync(testing, build, resultRoot);
        Assert(!string.IsNullOrWhiteSpace(preserved), "testing cleanup should preserve evidence before deleting workspace");
        Assert(File.Exists(preserved), "preserved evidence file missing");
        Assert(preserved.StartsWith(resultRoot, StringComparison.OrdinalIgnoreCase), "evidence must be copied to 40_Result");
        Assert(await File.ReadAllTextAsync(preserved) == "keep me", "preserved evidence content changed");
    }

    private static async Task DeletedTestingFolderKeepsTestHistory()
    {
        var dir = TempDir();
        var buildPath = Path.Combine(dir, "30_Experiment", "EyeAF", "Probe2", "FreeCam_R40.4.0_EyeAF_Probe2.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(buildPath)!);
        await File.WriteAllBytesAsync(buildPath, []);
        var missingTesting = Path.Combine(dir, "01_Testing", "FreeCam_R40.4.0_EyeAF_Probe2");
        var resultPath = Path.Combine(dir, "40_Result", "EyeAF", "Probe2", "probe2.log");
        Directory.CreateDirectory(Path.GetDirectoryName(resultPath)!);
        await File.WriteAllTextAsync(resultPath, "tested");

        var library = LibraryService.CreateInMemory([
            new Artifact
            {
                Path = buildPath, Name = Path.GetFileName(buildPath), TestingPath = missingTesting,
                TestingRelativePath = Path.GetRelativePath(dir, missingTesting), TestStatus = "已测试",
                ResultPath = resultPath, ResultRelativePath = Path.GetRelativePath(dir, resultPath)
            }
        ]);
        var refresh = new TestStatusRefreshService(library, new TestResultService(new ManifestService()), dir);
        var changed = await refresh.RefreshAsync();
        var updated = library.ByPath(buildPath)!;
        Assert(changed, "missing Testing workspace should update the library");
        Assert(string.IsNullOrWhiteSpace(updated.TestingPath), "stale TestingPath should be cleared");
        Assert(updated.TestStatus == "已测试", "clearing deleted workspace must keep historical tested state");
        Assert(string.Equals(updated.ResultPath, resultPath, StringComparison.OrdinalIgnoreCase), "historical Result path must be preserved");
    }

    private static Task PairedResultStoresDraggablePath()
    {
        var dir = TempDir();
        var buildPath = Path.Combine(dir, "FreeCam_R40.4.0_EyeAF_Probe2.zip");
        var resultPath = Path.Combine(dir, "FreeCam_R40.4.0_EyeAF_Probe2_Result.zip");
        var library = LibraryService.CreateInMemory([
            new Artifact { Path = buildPath, Name = Path.GetFileName(buildPath), BuildId = "B-P2", ArtifactType = "Runtime", TestStatus = "待测试" },
            new Artifact { Path = resultPath, Name = Path.GetFileName(resultPath), ArtifactType = "Result", ForBuildId = "B-P2", ImportedAt = "2026-09-12T22:00:00+08:00" }
        ]);
        library.PairResults();
        var updated = library.ByPath(buildPath)!;
        Assert(updated.TestStatus == "已测试", "paired Result should mark build tested");
        Assert(string.Equals(updated.ResultPath, resultPath, StringComparison.OrdinalIgnoreCase), "paired Result should immediately populate draggable ResultPath");
        return Task.CompletedTask;
    }


    private static Task PreferredDragEvidenceIgnoresInternalMarker()
    {
        var dir = TempDir();
        var testing = Path.Combine(dir, "01_Testing", "FreeCam_R40.3.1_Environment_ConfigDB_Runtime_Probe1.1");
        var resultRoot = Path.Combine(dir, "40_Result");
        Directory.CreateDirectory(testing);
        Directory.CreateDirectory(resultRoot);

        var marker = Path.Combine(testing, "CONFIGDB_RUNTIME");
        File.WriteAllText(marker, "internal marker");
        var expected = Path.Combine(resultRoot, "FreeCam_R40.3.1_Environment_ConfigDB_Runtime_Probe1.1_Result.zip");
        File.WriteAllText(expected, "zip placeholder");
        var build = new Artifact
        {
            Path = Path.Combine(dir, "FreeCam_R40.3.1_Environment_ConfigDB_Runtime_Probe1.1.zip"),
            Name = "FreeCam_R40.3.1_Environment_ConfigDB_Runtime_Probe1.1.zip",
            ResultPath = marker
        };

        var service = new TestResultService(new ManifestService());
        var chosen = service.ResolvePreferredDragPath(build, testing, resultRoot);
        Assert(string.Equals(chosen, expected, StringComparison.OrdinalIgnoreCase), $"Result ZIP should beat internal marker, got: {chosen}");

        File.Delete(expected);
        var log = Path.Combine(testing, "Probe1.1_Result.log");
        File.WriteAllText(log, "log");
        chosen = service.ResolvePreferredDragPath(build, testing, resultRoot);
        Assert(string.Equals(chosen, log, StringComparison.OrdinalIgnoreCase), "log should be fallback when no Result ZIP exists");
        return Task.CompletedTask;
    }

    private static Task ManagerLoggingDefaultsEnabled()
    {
        var cfg = new SettingsService().CreateDefault();
        Assert(cfg.ManagerLoggingEnabled, "Manager logging should default to enabled");
        return Task.CompletedTask;
    }

    private static async Task ManagerLoggingRuntimeSwitch()
    {
        var dir = TempDir();
        var preferred = Path.Combine(dir, "manager-logs");
        var fallback = Path.Combine(dir, "fallback-logs");
        var controller = new RuntimeLogController("test", fallback);

        Assert(controller.Configure(false, preferred, out var disabledDir), "disabling Manager logging should succeed");
        Assert(string.IsNullOrWhiteSpace(disabledDir), "disabled logging should not report an active log directory");
        controller.Event("DISABLED_EVENT");
        Assert(!Directory.Exists(preferred), "disabled logging must not create the configured log directory");

        Assert(controller.Configure(true, preferred, out var enabledDir), "enabling Manager logging should succeed");
        controller.Event("ENABLED_EVENT");
        await controller.DisposeAsync();
        Assert(string.Equals(Path.GetFullPath(enabledDir), Path.GetFullPath(preferred), StringComparison.OrdinalIgnoreCase), "enabled logging should use the preferred writable directory");
        Assert(Directory.EnumerateFiles(preferred, "*.log").Any(), "enabled logging should create a Manager log file");
    }


    private static async Task DiscardDeleteSchedulePersistsAndCancels()
    {
        var dir = TempDir();
        var file = Path.Combine(dir, "library.json");
        var path = Path.Combine(dir, "build.zip");
        await File.WriteAllTextAsync(path, "build");
        var lib = await LibraryService.LoadAsync(file);
        lib.Upsert(new Artifact { Path = path, Name = "build.zip", ManualStatus = "已废弃" });
        var due = DateTimeOffset.Now.AddDays(3);
        Assert(lib.SetAutoDeleteAt(path, due), "discard delete deadline should be set");
        await lib.SaveAsync();
        var loaded = await LibraryService.LoadAsync(file);
        Assert(DateTimeOffset.TryParse(loaded.ByPath(path)!.AutoDeleteAt, out var parsed) && Math.Abs((parsed - due).TotalSeconds) < 2, "discard delete deadline did not persist");
        Assert(loaded.SetManualStatus(path, "通过"), "manual status change failed");
        Assert(loaded.ClearAutoDeleteAt(path), "discard delete schedule should be cancellable");
        Assert(string.IsNullOrWhiteSpace(loaded.ByPath(path)!.AutoDeleteAt), "discard delete deadline was not cleared");
    }

    private static async Task DueDiscardCleanupPreservesEvidence()
    {
        var dir = TempDir();
        var root = Path.Combine(dir, "root");
        var testing = Path.Combine(root, "01_Testing", "FreeCam_Test");
        var results = Path.Combine(root, "40_Result");
        Directory.CreateDirectory(testing);
        var build = Path.Combine(root, "30_Experiment", "FreeCam_Test.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(build)!);
        await File.WriteAllTextAsync(build, "zip");
        await File.WriteAllTextAsync(Path.Combine(testing, "probe.log"), "evidence");

        var lib = LibraryService.CreateInMemory(new[]
        {
            new Artifact
            {
                Path = build, Name = Path.GetFileName(build), ManualStatus = "已废弃",
                AutoDeleteAt = DateTimeOffset.Now.AddMinutes(-1).ToString("O"), TestingPath = testing,
                TestStatus = "已测试", BuildId = "B-DISCARD"
            }
        });
        var organizer = new OrganizerService(root, "", lib, new ManifestService(), new ClassificationService(), new HashService());
        var cleanup = new DiscardCleanupService(lib, organizer, new TestResultService(new ManifestService()), () => root, () => results);
        var result = await cleanup.CleanupDueAsync(DateTimeOffset.Now);
        Assert(result.Deleted == 1 && result.SkippedProtected == 0, "due discarded build should be deleted");
        Assert(!File.Exists(build), "original build zip was not deleted");
        Assert(!Directory.Exists(testing), "testing workspace was not deleted");
        Assert(Directory.Exists(results) && Directory.EnumerateFiles(results, "*", SearchOption.AllDirectories).Any(), "result/log evidence was not preserved");
        Assert(lib.ByPath(build) is null, "deleted build remained in library");
    }

    private static async Task ResultsDetection()
    {
        var dir = TempDir();
        var testing = Path.Combine(dir, "Build");
        var results = Path.Combine(testing, "Results");
        Directory.CreateDirectory(results);
        var resultZip = Path.Combine(results, "result.zip");
        using (var zip = ZipFile.Open(resultZip, ZipArchiveMode.Create))
        {
            var e = zip.CreateEntry("RESULT_MANIFEST.json");
            await using var stream = e.Open();
            await JsonSerializer.SerializeAsync(stream, new Dictionary<string, object?> { ["ArtifactType"] = "Result", ["ForBuildId"] = "B1" });
        }
        var service = new TestResultService(new ManifestService());
        var (found, _) = await service.HasMatchingResultAsync(testing, new Artifact { BuildId = "B1" });
        Assert(found, "matching Result not detected");
        var (wrong, _) = await service.HasMatchingResultAsync(testing, new Artifact { BuildId = "B2" });
        Assert(!wrong, "mismatched Result accepted");
    }

    private static async Task OrganizerArchiveProtection()
    {
        var dir = TempDir();
        var root = Path.Combine(dir, "root");
        var inbox = Path.Combine(dir, "inbox");
        Directory.CreateDirectory(inbox);
        var file = Path.Combine(inbox, "FreeCam_R40.3.1_LookAt_Test1.zip");
        using (var zip = ZipFile.Open(file, ZipArchiveMode.Create))
        {
            var e = zip.CreateEntry("BUILD_MANIFEST.json");
            await using var stream = e.Open();
            await JsonSerializer.SerializeAsync(stream, new Dictionary<string, object?>
            {
                ["Base"] = "R40.3.1", ["Feature"] = "LookAt", ["Stage"] = "Test1",
                ["BuildType"] = "Test", ["Branch"] = "feature/lookat", ["ArtifactType"] = "Runtime", ["BuildId"] = "B1"
            });
        }
        var library = await LibraryService.LoadAsync(Path.Combine(dir, "library.json"));
        var organizer = new OrganizerService(root, "", library, new ManifestService(), new ClassificationService(), new HashService());
        var indexed = await organizer.ProcessAsync(file);
        Assert(indexed.Category == "Feature" && indexed.Path.Contains("20_Feature"), "organizer route failed");
        library.ToggleProtected(indexed.Path);
        var refused = false;
        try { await organizer.ArchiveAsync(indexed.Path); }
        catch (InvalidOperationException) { refused = true; }
        Assert(refused, "protected archive should be refused");
        library.ToggleProtected(indexed.Path);
        var archived = await organizer.ArchiveAsync(indexed.Path);
        Assert(archived.Category == "Archive" && archived.ManualStatus == "已废弃", "archive failed");
    }

    private static async Task InboxWatcherStability()
    {
        var dir = TempDir();
        var root = Path.Combine(dir, "root");
        var inbox = Path.Combine(dir, "inbox");
        Directory.CreateDirectory(inbox);
        var cfg = new AppSettings { RootDir = root, InboxDir = inbox, LogDir = Path.Combine(root, "Logs"), ScanSeconds = 3 };
        var settingsService = new SettingsService();
        settingsService.EnsureDirectories(cfg);
        var library = await LibraryService.LoadAsync(Path.Combine(dir, "library.json"));
        var manifest = new ManifestService();
        var classification = new ClassificationService();
        var organizer = new OrganizerService(root, "", library, manifest, classification, new HashService());
        var results = new TestResultService(manifest);
        var watcher = new InboxWatcherService(() => cfg, library, organizer, manifest, classification, new ExtractionService(), new TestStatusRefreshService(library, results));

        var manager = Path.Combine(inbox, "FreeCam_Manager_v3.0.zip");
        await File.WriteAllTextAsync(manager, "manager");
        File.SetLastWriteTimeUtc(manager, DateTime.UtcNow.AddSeconds(-10));
        await watcher.ScanOnceAsync();
        await watcher.ScanOnceAsync();
        Assert(!File.Exists(manager), "Manager delivery zip should be organized after the stable scan");
        Assert(File.Exists(Path.Combine(root, "50_Manager", "FreeCam_Manager_v3.0.zip")), "Manager delivery zip was not moved to 50_Manager");
        Assert(library.Snapshot().Any(x => x.Category == "Manager"), "Manager delivery zip missing from project index");
        Assert(!Directory.Exists(Path.Combine(root, "01_Testing", "FreeCam_Manager_v3.0")), "Manager delivery zip must not be extracted into 01_Testing");

        var build = Path.Combine(inbox, "FreeCam_R40.3.1_LookAt_Test2.zip");
        using (var zip = ZipFile.Open(build, ZipArchiveMode.Create))
        {
            var e = zip.CreateEntry("BUILD_MANIFEST.json");
            await using (var stream = e.Open())
            {
                await JsonSerializer.SerializeAsync(stream, new Dictionary<string, object?>
                {
                    ["Base"]="R40.3.1", ["Feature"]="LookAt", ["Stage"]="Test2", ["BuildType"]="Test",
                    ["Branch"]="experiment/lookat", ["ArtifactType"]="Runtime", ["BuildId"]="B2"
                });
            }
            var start = zip.CreateEntry("Start.cmd");
            await using var w = new StreamWriter(start.Open());
            await w.WriteAsync("echo test");
        }
        File.SetLastWriteTimeUtc(build, DateTime.UtcNow.AddSeconds(-10));
        var first = await watcher.ScanOnceAsync();
        Assert(!first && File.Exists(build), "first stable scan must not process");
        var second = await watcher.ScanOnceAsync();
        Assert(second && !File.Exists(build), "second stable scan should process/move file");
        Assert(Directory.Exists(Path.Combine(root, "01_Testing", "FreeCam_R40.3.1_LookAt_Test2")), "Testing folder missing");
        Assert(library.Snapshot().Any(x => x.BuildId == "B2"), "processed build missing from library");
    }


    private static async Task V32ManualRefreshImmediateScan()
    {
        var dir = TempDir();
        var root = Path.Combine(dir, "root");
        var inbox = Path.Combine(dir, "inbox");
        Directory.CreateDirectory(inbox);
        var cfg = new AppSettings { RootDir = root, InboxDir = inbox, LogDir = Path.Combine(root, "Logs"), ScanSeconds = 3 };
        new SettingsService().EnsureDirectories(cfg);
        var library = await LibraryService.LoadAsync(Path.Combine(dir, "library.json"));
        var manifest = new ManifestService();
        var classification = new ClassificationService();
        var organizer = new OrganizerService(root, "", library, manifest, classification, new HashService());
        var results = new TestResultService(manifest);
        var watcher = new InboxWatcherService(() => cfg, library, organizer, manifest, classification, new ExtractionService(), new TestStatusRefreshService(library, results));

        var build = Path.Combine(inbox, "WW36_ExternalVerify_Probe1.3.zip");
        using (var zip = ZipFile.Open(build, ZipArchiveMode.Create))
        {
            var e = zip.CreateEntry("BUILD_MANIFEST.json");
            await using (var stream = e.Open())
            {
                await JsonSerializer.SerializeAsync(stream, new Dictionary<string, object?>
                {
                    ["Base"]="WW36", ["Feature"]="ExternalVerify", ["Stage"]="Probe1.3", ["BuildType"]="Probe",
                    ["Branch"]="experiment/external-verify", ["ArtifactType"]="Runtime", ["BuildId"]="V32-MANUAL-1"
                });
            }
            var start = zip.CreateEntry("Start.cmd");
            await using var w = new StreamWriter(start.Open());
            await w.WriteAsync("echo test");
        }

        var changed = await watcher.ScanNowAsync();
        Assert(changed, "manual refresh should process a completed zip on the first click");
        Assert(!File.Exists(build), "manual refresh did not move the processed zip");
        Assert(library.Snapshot().Any(x => x.BuildId == "V32-MANUAL-1"), "manual refresh did not update the library");
    }

    private static Task WpfListPagesAvoidFragileStyles()
    {
        var sourceRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "FreeCamManager"));
        foreach (var name in new[] { "DevelopmentView.xaml", "HistoryView.xaml", "StableView.xaml", "SettingsView.xaml" })
        {
            var path = Path.Combine(sourceRoot, "Views", name);
            var text = File.ReadAllText(path);
            Assert(!text.Contains("<DataGrid", StringComparison.Ordinal), $"{name} still uses DataGrid, which triggered the WPF Style popup storm");
            Assert(!text.Contains("<ComboBox", StringComparison.Ordinal), $"{name} still uses the fragile ComboBox style path");
            if (name is "DevelopmentView.xaml" or "HistoryView.xaml")
                Assert(text.Contains("CompactPickerControl", StringComparison.Ordinal), $"{name} must use the custom conclusion picker");
        }
        return Task.CompletedTask;
    }

    private static Task Fix13WpfWorkflowContract()
    {
        var sourceRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "FreeCamManager"));
        var dev = File.ReadAllText(Path.Combine(sourceRoot, "Views", "DevelopmentView.xaml"));
        var history = File.ReadAllText(Path.Combine(sourceRoot, "Views", "HistoryView.xaml"));
        var row = File.ReadAllText(Path.Combine(sourceRoot, "ViewModels", "ArtifactRowViewModel.cs"));
        var star = File.ReadAllText(Path.Combine(sourceRoot, "Controls", "StarRatingControl.xaml"));
        var settings = File.ReadAllText(Path.Combine(sourceRoot, "Views", "SettingsView.xaml"));
        var project = File.ReadAllText(Path.Combine(sourceRoot, "FreeCamManager.csproj"));

        Assert(row.Contains("/C call", StringComparison.Ordinal) && !row.Contains("/k call", StringComparison.Ordinal), "cmd test launcher must close when script exits");
        Assert(!row.Contains("-NoExit", StringComparison.Ordinal), "PowerShell test launcher must close when script exits");
        Assert(row.Contains("IsExtracted", StringComparison.Ordinal), "artifact row must expose extraction state");
        Assert(dev.Contains("IconPackage", StringComparison.Ordinal) && dev.Contains("IconOpenPackage", StringComparison.Ordinal), "development list needs packed/open-package icons");
        Assert(!dev.Contains("编辑标签", StringComparison.Ordinal) && !history.Contains("编辑标签", StringComparison.Ordinal), "tag editor should be removed from list menus");
        Assert(!history.Contains("列表中的人工结论、星级和锁都会立即保存", StringComparison.Ordinal), "redundant history auto-save hint should be removed");
        Assert(star.Contains("Orientation=\"Vertical\"", StringComparison.Ordinal) && star.Contains("Width=\"18\"", StringComparison.Ordinal), "rating control must be a narrow vertical strip");
        Assert(dev.Contains("DimUnmarked=\"True\"", StringComparison.Ordinal) && history.Contains("DimUnmarked=\"True\"", StringComparison.Ordinal), "unmarked conclusion should be visually muted");
        Assert(settings.Contains("Manager 日志记录", StringComparison.Ordinal), "settings page needs Manager log switch");
        Assert(project.Contains("ApplicationIcon", StringComparison.Ordinal) && project.Contains("FreeCamManager.ico", StringComparison.Ordinal), "application icon must be configured");
        return Task.CompletedTask;
    }


    private static Task Fix14WpfInteractionContract()
    {
        var sourceRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "FreeCamManager"));
        var controls = File.ReadAllText(Path.Combine(sourceRoot, "Styles", "Controls.xaml"));
        var picker = File.ReadAllText(Path.Combine(sourceRoot, "Controls", "CompactPickerControl.xaml.cs"));
        var settings = File.ReadAllText(Path.Combine(sourceRoot, "Views", "SettingsView.xaml"));
        var dev = File.ReadAllText(Path.Combine(sourceRoot, "Views", "DevelopmentView.xaml"));
        var history = File.ReadAllText(Path.Combine(sourceRoot, "Views", "HistoryView.xaml"));
        var row = File.ReadAllText(Path.Combine(sourceRoot, "ViewModels", "ArtifactRowViewModel.cs"));

        Assert(controls.Contains("TargetType=\"ToolTip\"", StringComparison.Ordinal) && controls.Contains("PanelElevatedBrush", StringComparison.Ordinal), "ToolTip must follow the dark/light app theme");
        Assert(controls.Contains("MiniScrollBarStyle", StringComparison.Ordinal), "mini hover scrollbar style missing");
        Assert(picker.Contains("OwnerWindow_PreviewMouseDown", StringComparison.Ordinal) && picker.Contains("Key.Escape", StringComparison.Ordinal), "picker must close on outside click and Escape");
        Assert(settings.Contains("Height=\"34\"", StringComparison.Ordinal), "settings compact controls must keep the unified height");
        Assert(dev.Contains("FileColumnThumb_DragDelta", StringComparison.Ordinal), "development file column must be manually resizable");
        Assert(history.Contains("SharedSizeGroup=\"HistoryFile\"", StringComparison.Ordinal), "history file column must use the shared resizable layout");
        Assert(!row.Contains("ChooseDiscardDeleteDays", StringComparison.Ordinal), "discarded status must use the global Settings policy without a second popup");
        return Task.CompletedTask;
    }


    private static Task V32WpfRefreshAliasContract()
    {
        var sourceRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "FreeCamManager"));
        var settings = File.ReadAllText(Path.Combine(sourceRoot, "Views", "SettingsView.xaml"));
        var dev = File.ReadAllText(Path.Combine(sourceRoot, "Views", "DevelopmentView.xaml"));
        var history = File.ReadAllText(Path.Combine(sourceRoot, "Views", "HistoryView.xaml"));
        var home = File.ReadAllText(Path.Combine(sourceRoot, "Views", "HomeView.xaml"));
        var homeVm = File.ReadAllText(Path.Combine(sourceRoot, "ViewModels", "HomeViewModel.cs"));
        var devVm = File.ReadAllText(Path.Combine(sourceRoot, "ViewModels", "DevelopmentViewModel.cs"));
        var row = File.ReadAllText(Path.Combine(sourceRoot, "ViewModels", "ArtifactRowViewModel.cs"));

        Assert(settings.Contains("显示文件名中文别名", StringComparison.Ordinal), "filename alias toggle missing from Settings");
        Assert(settings.Contains("OpenFilenameTermsCommand", StringComparison.Ordinal) && settings.Contains("ReloadFilenameTermsCommand", StringComparison.Ordinal), "filename term file actions missing from Settings");
        Assert(dev.Contains("FileNameToolTip", StringComparison.Ordinal) && history.Contains("FileNameToolTip", StringComparison.Ordinal), "original filename tooltip binding missing");
        Assert(home.Contains("DisplayName", StringComparison.Ordinal) && home.Contains("FileNameToolTip", StringComparison.Ordinal), "home recent list must use alias display and original-name tooltip");
        Assert(homeVm.Contains("原始文件名", StringComparison.Ordinal) && homeVm.Contains("备注", StringComparison.Ordinal), "home tooltip must combine original filename and notes");
        Assert(row.Contains("原始文件名", StringComparison.Ordinal) && row.Contains("备注", StringComparison.Ordinal), "tooltip must combine original filename and notes");
        Assert(devVm.Contains("ManualRefreshAsync", StringComparison.Ordinal) && devVm.Contains("正在扫描收件箱", StringComparison.Ordinal), "development refresh must trigger real inbox scan");
        Assert(settings.Contains("显示与行为", StringComparison.Ordinal), "settings must keep the display/behavior card");
        Assert(settings.Contains("功能中文显示", StringComparison.Ordinal) && settings.Contains("阶段中文显示", StringComparison.Ordinal), "feature/stage alias toggles missing");
        Assert(row.Contains("DisplayFeature", StringComparison.Ordinal) && row.Contains("FeatureToolTip", StringComparison.Ordinal), "feature alias display/tooltip missing");
        Assert(row.Contains("DisplayStage", StringComparison.Ordinal) && row.Contains("StageToolTip", StringComparison.Ordinal), "stage alias display/tooltip missing");
        Assert(dev.Contains("{Binding DisplayFeature}", StringComparison.Ordinal) && dev.Contains("{Binding FeatureToolTip}", StringComparison.Ordinal), "development feature alias binding missing");
        Assert(dev.Contains("{Binding DisplayStage}", StringComparison.Ordinal) && dev.Contains("{Binding StageToolTip}", StringComparison.Ordinal), "development stage alias binding missing");
        Assert(history.Contains("{Binding DisplayFeature}", StringComparison.Ordinal) && history.Contains("{Binding FeatureToolTip}", StringComparison.Ordinal), "history feature alias binding missing");
        Assert(history.Contains("{Binding DisplayStage}", StringComparison.Ordinal) && history.Contains("{Binding StageToolTip}", StringComparison.Ordinal), "history stage alias binding missing");
        return Task.CompletedTask;
    }


    private static Task V32Fix2ResponsiveColumnsContract()
    {
        var sourceRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "FreeCamManager"));
        var coreRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "FreeCamManager.Core"));
        var dev = File.ReadAllText(Path.Combine(sourceRoot, "Views", "DevelopmentView.xaml"));
        var history = File.ReadAllText(Path.Combine(sourceRoot, "Views", "HistoryView.xaml"));
        var historyCode = File.ReadAllText(Path.Combine(sourceRoot, "Views", "HistoryView.xaml.cs"));
        var settings = File.ReadAllText(Path.Combine(sourceRoot, "Views", "SettingsView.xaml"));
        var appSettings = File.ReadAllText(Path.Combine(coreRoot, "Models", "AppSettings.cs"));
        var stable = File.ReadAllText(Path.Combine(sourceRoot, "Views", "StableView.xaml"));

        Assert(dev.Contains("MinWidth=\"180\" SharedSizeGroup=\"DevFile\"", StringComparison.Ordinal), "development file column should shrink to 180");
        Assert(dev.Contains("MinWidth=\"60\" SharedSizeGroup=\"DevFeature\"", StringComparison.Ordinal), "development feature column should shrink to 60");
        Assert(dev.Contains("MinWidth=\"52\" SharedSizeGroup=\"DevStage\"", StringComparison.Ordinal), "development stage column should shrink to 52");
        Assert(history.Contains("HistoryHeaderGrid", StringComparison.Ordinal) && history.Contains("FileColumnThumb_DragDelta", StringComparison.Ordinal), "history file column resize UI missing");
        Assert(history.Contains("HistoryFeature", StringComparison.Ordinal) && history.Contains("HistoryStage", StringComparison.Ordinal), "history feature/stage resize columns missing");
        Assert(historyCode.Contains("PersistColumnWidthsAsync", StringComparison.Ordinal), "history column widths must persist after drag");
        Assert(appSettings.Contains("history_file_column_width", StringComparison.Ordinal) && appSettings.Contains("history_feature_column_width", StringComparison.Ordinal) && appSettings.Contains("history_stage_column_width", StringComparison.Ordinal), "history persisted width settings missing");
        Assert(settings.Contains("HorizontalContentAlignment=\"Stretch\"", StringComparison.Ordinal) && settings.Contains("<Grid HorizontalAlignment=\"Stretch\">", StringComparison.Ordinal), "settings page must stretch to viewport width");
        Assert(!stable.Contains("FileColumnThumb_DragDelta", StringComparison.Ordinal), "stable page should remain non-resizable");
        return Task.CompletedTask;
    }



    private static async Task V33OnlineTermsUpdate()
    {
        var dir = TempDir();
        var termsPath = Path.Combine(dir, "Config", "FilenameTerms.json");
        var versionPath = Path.Combine(dir, "Config", "version.json");
        Directory.CreateDirectory(Path.GetDirectoryName(termsPath)!);
        await File.WriteAllTextAsync(termsPath, "[{\"from\":\"Old\",\"to\":\"旧\"}]");

        var remoteTerms = Encoding.UTF8.GetBytes("[{\"from\":\"SlowShutter\",\"to\":\"慢快门\"},{\"from\":\"Probe\",\"to\":\"探针\"}]");
        var sha = Convert.ToHexString(SHA256.HashData(remoteTerms)).ToLowerInvariant();
        var versionJson = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            version = "2026.09.13.1",
            updatedAt = "2026-09-13T14:34:00+08:00",
            termCount = 2,
            sha256 = sha,
            termsFile = "FilenameTerms.json"
        });

        var handler = new StubHttpHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("version.json", StringComparison.OrdinalIgnoreCase))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(versionJson, Encoding.UTF8, "application/json") };
            if (request.RequestUri.AbsolutePath.EndsWith("FilenameTerms.json", StringComparison.OrdinalIgnoreCase))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(remoteTerms) };
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        using var http = new HttpClient(handler);
        using var updater = new TermsUpdateService(termsPath, versionPath, http);

        var first = await updater.CheckForUpdateAsync();
        Assert(first.Status == TermsUpdateStatus.Updated, "first online terms check should update changed local content");
        Assert(first.TermCount == 2 && first.Version == "2026.09.13.1", "online terms metadata was not preserved");
        Assert(File.ReadAllBytes(termsPath).SequenceEqual(remoteTerms), "validated remote terms were not atomically installed");
        Assert(File.Exists(versionPath), "local version metadata was not written");

        var aliases = new FilenameAliasService(termsPath);
        var count = await aliases.ReloadAsync();
        Assert(count == 2 && aliases.Translate("SlowShutter_Probe1") == "慢快门_探针1", "updated terms were not consumable by alias service");

        var second = await updater.CheckForUpdateAsync();
        Assert(second.Status == TermsUpdateStatus.UpToDate, "matching SHA should report up-to-date");
        Assert(handler.TermsRequests == 1, "up-to-date check should not re-download FilenameTerms.json");
        Assert(handler.AllRequestsBypassCache, "online terms requests must bypass stale GitHub raw/CDN cache");
    }


    private static async Task V33CurrentWorkspaceResultPriority()
    {
        var dir = TempDir();
        var testing = Path.Combine(dir, "01_Testing", "WW36_P1_LookAtSightLock_Probe1");
        var results = Path.Combine(testing, "Results");
        var prior = Path.Combine(testing, "Validation", "Prior_Runtime_Evidence");
        var resultRoot = Path.Combine(dir, "40_Result");
        Directory.CreateDirectory(results);
        Directory.CreateDirectory(prior);
        Directory.CreateDirectory(resultRoot);

        var currentResult = Path.Combine(results, "WW36_P1_LookAtSightLock_Probe1_Result.zip");
        var priorResult = Path.Combine(prior, "WW36_ExternalVerify_Probe1.5_Result.zip");
        await File.WriteAllTextAsync(currentResult, "current");
        await File.WriteAllTextAsync(priorResult, "prior reference");
        File.SetLastWriteTimeUtc(priorResult, DateTime.UtcNow.AddMinutes(1));

        var build = new Artifact
        {
            Path = Path.Combine(dir, "WW36_P1_LookAtSightLock_Probe1.zip"),
            Name = "WW36_P1_LookAtSightLock_Probe1.zip",
            BuildId = "WW36-P1-LOOKAT",
            TestingPath = testing,
            TestStatus = "已测试",
            ResultPath = priorResult
        };

        var service = new TestResultService(new ManifestService());
        var chosen = service.ResolvePreferredDragPath(build, testing, resultRoot);
        Assert(string.Equals(chosen, currentResult, StringComparison.OrdinalIgnoreCase),
            $"current Results output must beat stored prior evidence; got: {chosen}");

        var library = LibraryService.CreateInMemory([build]);
        var refresh = new TestStatusRefreshService(library, service, dir);
        var refreshed = await refresh.RefreshAsync();
        var refreshedBuild = library.ByPath(build.Path)!;
        Assert(refreshed, "tested build with a stored prior-evidence path must be rescanned");
        Assert(string.Equals(refreshedBuild.ResultPath, currentResult, StringComparison.OrdinalIgnoreCase),
            $"refresh must replace stale prior-evidence ResultPath with current output; got: {refreshedBuild.ResultPath}");

        File.Delete(currentResult);
        var unrelatedResult = Path.Combine(results, "WW36_ExternalVerify_Probe1.4_Result.zip");
        await File.WriteAllTextAsync(unrelatedResult, "unrelated old result");
        var currentLog = Path.Combine(testing, "WW36_P1_LOOKAT_REPORT.log");
        await File.WriteAllTextAsync(currentLog, "current log");
        chosen = service.ResolvePreferredDragPath(build, testing, resultRoot);
        Assert(string.Equals(chosen, currentLog, StringComparison.OrdinalIgnoreCase),
            $"current workspace log must beat prior evidence fallback; got: {chosen}");

        var evidence = await service.FindEvidenceAsync(testing, build);
        Assert(evidence is not null && string.Equals(evidence.Path, currentLog, StringComparison.OrdinalIgnoreCase),
            $"test status scan must ignore prior/reference evidence folders; got: {evidence?.Path}");
    }

    private static async Task V34SystemThemeSettings()
    {
        var dir = TempDir();
        var file = Path.Combine(dir, "settings.json");
        var service = new SettingsService();
        var cfg = service.CreateDefault(dir);
        Assert(cfg.Theme == "system", "V3.4 default theme should follow system");

        cfg.Theme = "system";
        await service.SaveAsync(file, cfg);
        var loaded = await service.LoadOrCreateAsync(file, dir);
        Assert(loaded.Theme == "system", "system theme should round-trip");

        loaded.Theme = "neon";
        await service.SaveAsync(file, loaded);
        var repaired = await service.LoadOrCreateAsync(file, dir);
        Assert(repaired.Theme == "system", "invalid theme should repair to system");
    }


    private static Task V34Fix3StageDisplay()
    {
        Assert(StageDisplayService.Format("Probe1.5.2") == "1.5.2", "Probe prefix should be removed from stage display");
        Assert(StageDisplayService.Format("Test1.1") == "1.1", "Test prefix should be removed from stage display");
        Assert(StageDisplayService.Format("Experiment2") == "2", "Experiment prefix should be removed from stage display");
        Assert(StageDisplayService.Format("Regression3_Fix2") == "3 · Fix2", "Fix suffix should remain readable after type removal");
        Assert(StageDisplayService.Format("") == "—", "blank stage should render as dash");
        return Task.CompletedTask;
    }

    private static async Task V34Fix4StageFailureDiagnostics()
    {
        var dir = TempDir();
        var badArchive = Encoding.UTF8.GetBytes("not the expected archive");
        var manifest = new ManagerUpdateManifest
        {
            SchemaVersion = 1,
            Version = "3.4.4",
            DisplayVersion = "V3.4 Fix4",
            SourceRef = "0123456789abcdef0123456789abcdef01234567",
            SourceArchiveUrl = "https://raw.githubusercontent.com/wujunda612-star/FreeCam-Manager/0123456789abcdef0123456789abcdef01234567/manager/packages/FreeCam_Manager_V3.4_Fix4_Source.zip",
            SourceSha256 = new string('0', 64)
        };
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(badArchive)
        });
        using var http = new HttpClient(handler);
        using var updates = new ManagerUpdateService(Path.Combine(dir, "Updates"), http);

        var failed = false;
        try
        {
            await updates.StageAsync(manifest);
        }
        catch (InvalidDataException)
        {
            failed = true;
        }
        Assert(failed, "bad update archive should fail staging");

        var staging = Path.Combine(dir, "Updates", "3.4.4");
        var errorFile = Path.Combine(staging, "UPDATE_STAGE_ERROR.txt");
        var statusFile = Path.Combine(staging, "UPDATE_STATUS.json");
        Assert(File.Exists(errorFile), "staging failure text diagnostic missing");
        Assert(File.Exists(statusFile), "staging failure status JSON missing");
        Assert((await File.ReadAllTextAsync(errorFile)).Contains("SHA256", StringComparison.OrdinalIgnoreCase), "staging failure diagnostic should include root cause");
        using var status = JsonDocument.Parse(await File.ReadAllTextAsync(statusFile));
        Assert(status.RootElement.GetProperty("stage").GetString() == "staging_failed", "staging status stage invalid");
        Assert(status.RootElement.GetProperty("version").GetString() == "3.4.4", "staging status version invalid");
    }

    private static async Task V34ManagerUpdateService()
    {
        var dir = TempDir();

        var baselineJson = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            version = "3.4.0",
            displayVersion = "V3.4",
            sourceRef = "",
            sourceArchiveUrl = "",
            sourceSha256 = "",
            publishedAt = "2026-09-13T15:30:00Z",
            notes = "baseline"
        });
        var baselineHandler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(baselineJson, Encoding.UTF8, "application/json") });
        using (var baselineHttp = new HttpClient(baselineHandler))
        using (var baselineUpdates = new ManagerUpdateService(Path.Combine(dir, "BaselineUpdates"), baselineHttp))
        {
            var baseline = await baselineUpdates.CheckAsync(new Version(3, 4, 0));
            Assert(baseline.Status == ManagerUpdateStatus.UpToDate, "current-version manifest should not require a downloadable package");
        }

        byte[] archiveBytes;
        await using (var ms = new MemoryStream())
        {
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                var build = zip.CreateEntry("FreeCam-Manager-0123456/src-wpf/Build_v3_On_Windows.ps1");
                await using (var writer = new StreamWriter(build.Open())) await writer.WriteAsync("# build");
                var updater = zip.CreateEntry("FreeCam-Manager-0123456/src-wpf/Apply_Manager_Update.ps1");
                await using (var writer = new StreamWriter(updater.Open())) await writer.WriteAsync("# updater");
                var project = zip.CreateEntry("FreeCam-Manager-0123456/src-wpf/FreeCamManager.sln");
                await using (var writer = new StreamWriter(project.Open())) await writer.WriteAsync("solution");
            }
            archiveBytes = ms.ToArray();
        }

        var sourceSha = Convert.ToHexString(SHA256.HashData(archiveBytes)).ToLowerInvariant();
        var manifestJson = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            version = "3.4.1",
            displayVersion = "V3.4 Fix1",
            sourceRef = "0123456789abcdef0123456789abcdef01234567",
            sourceArchiveUrl = "https://raw.githubusercontent.com/wujunda612-star/FreeCam-Manager/0123456789abcdef0123456789abcdef01234567/manager/packages/FreeCam_Manager_V3.4_Source.zip",
            sourceSha256 = sourceSha,
            publishedAt = "2026-09-13T15:30:00Z",
            notes = "test"
        });

        var handler = new StubHttpHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/manager/update.json", StringComparison.OrdinalIgnoreCase))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(manifestJson, Encoding.UTF8, "application/json") };
            if (request.RequestUri.AbsolutePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archiveBytes) };
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        using var http = new HttpClient(handler);
        using var updates = new ManagerUpdateService(Path.Combine(dir, "Updates"), http);

        var check = await updates.CheckAsync(new Version(3, 4, 0));
        Assert(check.Status == ManagerUpdateStatus.UpdateAvailable, "newer manager manifest should be offered");
        Assert(check.Manifest?.DisplayVersion == "V3.4 Fix1", "display version missing");
        Assert(handler.AllRequestsBypassCache, "manager update checks must bypass cache");

        var staged = await updates.StageAsync(check.Manifest!);
        Assert(File.Exists(staged.UpdaterScriptPath), "updater script missing from staged source");
        Assert(File.Exists(Path.Combine(staged.SourceRoot, "src-wpf", "Build_v3_On_Windows.ps1")), "build script missing from staged source");

        var invalidJson = manifestJson.Replace("raw.githubusercontent.com/wujunda612-star/FreeCam-Manager", "example.com/evil", StringComparison.Ordinal);
        var invalidHandler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(invalidJson, Encoding.UTF8, "application/json") });
        using var invalidHttp = new HttpClient(invalidHandler);
        using var invalidUpdates = new ManagerUpdateService(Path.Combine(dir, "InvalidUpdates"), invalidHttp);
        var invalid = await invalidUpdates.CheckAsync(new Version(3, 4, 0));
        Assert(invalid.Status == ManagerUpdateStatus.Failed, "untrusted update URL must be rejected");
    }

    private static async Task V34Fix5ManifestUsesGitHubApi()
    {
        var dir = TempDir();
        var manifestJson = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            version = "3.4.5",
            displayVersion = "V3.4 Fix5",
            sourceRef = "0123456789abcdef0123456789abcdef01234567",
            sourceArchiveUrl = "https://raw.githubusercontent.com/wujunda612-star/FreeCam-Manager/0123456789abcdef0123456789abcdef01234567/manager/packages/FreeCam_Manager_V3.4_Fix5_Source.zip",
            sourceSha256 = new string('0', 64),
            publishedAt = "2026-09-13T17:30:00Z",
            notes = "test"
        });
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(manifestJson, Encoding.UTF8, "application/json")
        });
        using var http = new HttpClient(handler);
        using var updates = new ManagerUpdateService(Path.Combine(dir, "Updates"), http);

        var result = await updates.CheckAsync(new Version(3, 4, 4));
        Assert(result.Status == ManagerUpdateStatus.UpdateAvailable, "Fix5 manifest should be newer than Fix4");
        Assert(handler.SawRawManagerManifest, "manager manifest request must use raw GitHub");
        Assert(handler.SawUserAgent, "manager manifest request must include User-Agent");
        Assert(handler.AllRequestsBypassCache, "raw manifest request must bypass caches");
    }


    private static async Task V34Fix6ContentsEnvelopeAndLocalAhead()
    {
        var dir = TempDir();
        var manifestJson = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            version = "3.4.5",
            displayVersion = "V3.4 Fix5",
            sourceRef = "0123456789abcdef0123456789abcdef01234567",
            sourceArchiveUrl = "https://raw.githubusercontent.com/wujunda612-star/FreeCam-Manager/0123456789abcdef0123456789abcdef01234567/manager/packages/FreeCam_Manager_V3.4_Fix5_Source.zip",
            sourceSha256 = new string('0', 64),
            publishedAt = "2026-09-13T17:30:00Z",
            notes = "test"
        });
        var envelopeJson = JsonSerializer.Serialize(new
        {
            name = "update.json",
            path = "manager/update.json",
            sha = "0123456789abcdef0123456789abcdef01234567",
            encoding = "base64",
            content = Convert.ToBase64String(Encoding.UTF8.GetBytes(manifestJson))
        });
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(envelopeJson, Encoding.UTF8, "application/json")
        });
        using var http = new HttpClient(handler);
        using var updates = new ManagerUpdateService(Path.Combine(dir, "Updates"), http);

        var result = await updates.CheckAsync(new Version(3, 4, 6));
        Assert(result.Status == ManagerUpdateStatus.UpToDate, "local Fix6 should not be downgraded to online Fix5");
        Assert(result.LatestVersion == new Version(3, 4, 5), "online version should be preserved when local build is ahead");
        Assert(result.Manifest?.DisplayVersion == "V3.4 Fix5", "GitHub contents envelope must decode the manifest");
    }

    private static async Task V34Fix7CoalescesRapidUpdateChecks()
    {
        var dir = TempDir();
        var manifestJson = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            version = "3.4.7",
            displayVersion = "V3.4 Fix7",
            sourceRef = "0123456789abcdef0123456789abcdef01234567",
            sourceArchiveUrl = "https://raw.githubusercontent.com/wujunda612-star/FreeCam-Manager/0123456789abcdef0123456789abcdef01234567/manager/packages/FreeCam_Manager_V3.4_Fix7_Source.zip",
            sourceSha256 = new string('0', 64),
            publishedAt = "2026-09-13T18:45:00Z",
            notes = "test"
        });

        var handler = new DelayedManifestHandler(manifestJson);
        using var http = new HttpClient(handler);
        using var updates = new ManagerUpdateService(Path.Combine(dir, "Updates"), http);

        var checks = Enumerable.Range(0, 12)
            .Select(_ => updates.CheckAsync(new Version(3, 4, 6)))
            .ToArray();
        var results = await Task.WhenAll(checks);

        Assert(results.All(x => x.Status == ManagerUpdateStatus.UpdateAvailable), "rapid checks should share the successful update result");
        Assert(handler.RequestCount == 1, "concurrent manager update checks must produce only one HTTP request");

        var immediateRepeat = await updates.CheckAsync(new Version(3, 4, 6));
        Assert(immediateRepeat.Status == ManagerUpdateStatus.UpdateAvailable, "cooldown repeat should reuse the previous result");
        Assert(handler.RequestCount == 1, "checks within cooldown must not produce another HTTP request");
        Assert(handler.SawRawManagerManifest, "Fix7 must read manager update manifest from raw GitHub");
        Assert(handler.AllRequestsBypassCache, "Fix7 raw manifest request must bypass cache");
    }

    private static Task V35SettingsAndContextMenuContract()
    {
        var sourceRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "FreeCamManager"));
        var settings = File.ReadAllText(Path.Combine(sourceRoot, "Views", "SettingsView.xaml"));
        var controls = File.ReadAllText(Path.Combine(sourceRoot, "Styles", "Controls.xaml"));

        Assert(!settings.Contains("工作区规则", StringComparison.Ordinal), "workspace rules card must be removed in V3.5");
        Assert(!settings.Contains("测试工作区行为", StringComparison.Ordinal), "settings subtitle must not mention removed workspace rules");
        Assert(controls.Contains("<Style TargetType=\"ContextMenu\">", StringComparison.Ordinal)
            && controls.Contains("HorizontalContentAlignment\" Value=\"Stretch\"", StringComparison.Ordinal),
            "context menu content must stretch across the menu width");
        Assert(controls.Contains("x:Key=\"{x:Static MenuItem.SeparatorStyleKey}\"", StringComparison.Ordinal)
            && controls.Contains("Margin=\"0,4\"", StringComparison.Ordinal),
            "context menu separators must use the WPF menu separator key and full available width");
        return Task.CompletedTask;
    }

    private static Task V35UpdateCleanupKeepsDiagnosticsOnly()
    {
        var root = TempDir();
        var updates = Path.Combine(root, "Updates");
        var success = Path.Combine(updates, "3.5.0");
        Directory.CreateDirectory(Path.Combine(success, "source", "src-wpf"));
        Directory.CreateDirectory(Path.Combine(success, "build-output"));
        File.WriteAllText(Path.Combine(success, "source.zip"), "zip");
        File.WriteAllText(Path.Combine(success, "Apply_Manager_Update_Bridge.ps1"), "script");
        File.WriteAllText(Path.Combine(success, "Run_Manager_Update.cmd"), "cmd");
        File.WriteAllText(Path.Combine(success, "UPDATE_RESULT.txt"), "result");
        File.WriteAllText(Path.Combine(success, "UPDATE_BOOTSTRAP.log"), "bootstrap");
        File.WriteAllText(Path.Combine(success, "UPDATE_HANDOFF.txt"), "handoff");
        File.WriteAllText(Path.Combine(success, "UPDATE_STATUS.json"), "{\"state\":\"success\",\"targetVersion\":\"V3.5\"}");

        var failed = Path.Combine(updates, "3.4.9");
        Directory.CreateDirectory(Path.Combine(failed, "source"));
        File.WriteAllText(Path.Combine(failed, "UPDATE_STATUS.json"), "{\"state\":\"failed\"}");
        File.WriteAllText(Path.Combine(failed, "source.zip"), "keep");

        var result = ManagerUpdateCleanupService.CleanupSuccessfulUpdates(updates);

        Assert(result.CleanedDirectories == 1, "exactly one successful update directory should be cleaned");
        Assert(!Directory.Exists(Path.Combine(success, "source")), "successful update source directory should be removed");
        Assert(!Directory.Exists(Path.Combine(success, "build-output")), "successful update build-output should be removed");
        Assert(!File.Exists(Path.Combine(success, "source.zip")), "successful update source archive should be removed");
        Assert(!File.Exists(Path.Combine(success, "Apply_Manager_Update_Bridge.ps1")), "embedded updater payload should be removed after success");
        Assert(!File.Exists(Path.Combine(success, "Run_Manager_Update.cmd")), "bootstrap script should be removed after success");
        Assert(File.Exists(Path.Combine(success, "UPDATE_RESULT.txt")), "update result diagnostic must be preserved");
        Assert(File.Exists(Path.Combine(success, "UPDATE_BOOTSTRAP.log")), "bootstrap diagnostic must be preserved");
        Assert(File.Exists(Path.Combine(success, "UPDATE_HANDOFF.txt")), "handoff diagnostic must be preserved");
        Assert(File.Exists(Path.Combine(success, "UPDATE_STATUS.json")), "update status diagnostic must be preserved");
        Assert(Directory.Exists(Path.Combine(failed, "source")), "failed update staging must remain for diagnosis");
        Assert(File.Exists(Path.Combine(failed, "source.zip")), "failed update archive must remain for diagnosis");
        return Task.CompletedTask;
    }


    private static Task V36ContextMenuSeparatorUsesMenuItemKey()
    {
        var sourceRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "FreeCamManager"));
        var controls = File.ReadAllText(Path.Combine(sourceRoot, "Styles", "Controls.xaml"));
        Assert(controls.Contains("x:Key=\"{x:Static MenuItem.SeparatorStyleKey}\"", StringComparison.Ordinal),
            "WPF ContextMenu separators must use MenuItem.SeparatorStyleKey rather than only an implicit Separator style");
        Assert(controls.Contains("Margin=\"0,4\"", StringComparison.Ordinal),
            "menu separator must have zero horizontal inset");
        return Task.CompletedTask;
    }

    private static Task V36HomeSummaryIconsShareOnePresentationStyle()
    {
        var sourceRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "FreeCamManager"));
        var home = File.ReadAllText(Path.Combine(sourceRoot, "Views", "HomeView.xaml"));
        Assert(home.Contains("x:Key=\"HomeSummaryIconStyle\"", StringComparison.Ordinal), "Home summary icon style is missing");
        Assert(home.Contains("<Setter Property=\"StrokeThickness\" Value=\"1.4\"/>", StringComparison.Ordinal), "Home summary icon stroke thickness must be shared");
        Assert(home.Contains("<Setter Property=\"Width\" Value=\"23\"/>", StringComparison.Ordinal)
            && home.Contains("<Setter Property=\"Height\" Value=\"23\"/>", StringComparison.Ordinal),
            "Home summary icon dimensions must be shared");
        Assert(home.Contains("<Setter Property=\"Stretch\" Value=\"Uniform\"/>", StringComparison.Ordinal), "Home summary icons must all use Uniform stretch");
        Assert(home.Contains("<Setter Property=\"VerticalAlignment\" Value=\"Top\"/>", StringComparison.Ordinal), "Home summary icon alignment must be shared");
        var usageCount = home.Split("Style=\"{StaticResource HomeSummaryIconStyle}\"", StringSplitOptions.None).Length - 1;
        Assert(usageCount == 4, $"exactly four Home summary icons must use the shared style, got {usageCount}");
        return Task.CompletedTask;
    }

    private static Task V36SingleInstanceStartupContract()
    {
        var sourceRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "FreeCamManager"));
        var servicePath = Path.Combine(sourceRoot, "Services", "SingleInstanceService.cs");
        Assert(File.Exists(servicePath), "SingleInstanceService.cs must exist");
        var service = File.ReadAllText(servicePath);
        var app = File.ReadAllText(Path.Combine(sourceRoot, "App.xaml.cs"));
        Assert(service.Contains("FreeCamManager.SingleInstance", StringComparison.Ordinal)
            && service.Contains("new Mutex(true, MutexName", StringComparison.Ordinal),
            "single-instance mutex ownership contract is missing");
        Assert(service.Contains("SignalPrimaryInstance", StringComparison.Ordinal)
            && service.Contains("EventWaitHandle.OpenExisting", StringComparison.Ordinal),
            "secondary instance activation signal is missing");
        Assert(app.Contains("if (!_singleInstance.TryAcquirePrimary())", StringComparison.Ordinal)
            && app.Contains("SingleInstanceService.SignalPrimaryInstance();", StringComparison.Ordinal)
            && app.Contains("Shutdown();", StringComparison.Ordinal),
            "secondary Manager instance must signal the primary and exit");
        Assert(app.Contains("_singleInstance.StartListening", StringComparison.Ordinal)
            && app.Contains("BringPrimaryWindowToFront", StringComparison.Ordinal),
            "primary Manager instance must listen and restore/activate its window");
        return Task.CompletedTask;
    }


    private static Task V37MatchedResultZipBeatsRawLog()
    {
        var dir = TempDir();
        var testing = Path.Combine(dir, "01_Testing", "FreeCam_R40.4.0_ResultPriority_Probe1");
        var logs = Path.Combine(testing, "Logs");
        Directory.CreateDirectory(logs);
        var log = Path.Combine(logs, "ResultPriority_Probe1.log");
        File.WriteAllText(log, "raw log");

        var resultRoot = Path.Combine(dir, "40_Result");
        var resultDir = Path.Combine(resultRoot, "ResultPriority", "Probe1");
        Directory.CreateDirectory(resultDir);
        var resultZip = Path.Combine(resultDir, "FreeCam_R40.4.0_ResultPriority_Probe1_Result.zip");
        using (ZipFile.Open(resultZip, ZipArchiveMode.Create)) { }

        var build = new Artifact
        {
            Path = Path.Combine(dir, "30_Experiment", "ResultPriority", "Probe1", "FreeCam_R40.4.0_ResultPriority_Probe1.zip"),
            Name = "FreeCam_R40.4.0_ResultPriority_Probe1.zip",
            Feature = "ResultPriority",
            Stage = "Probe1",
            TestingPath = testing,
            TestStatus = "已测试",
            ResultPath = log
        };

        var chosen = new TestResultService(new ManifestService()).ResolvePreferredDragPath(build, testing, resultRoot);
        Assert(string.Equals(chosen, resultZip, StringComparison.OrdinalIgnoreCase),
            $"matched Result ZIP must beat raw log even when log was discovered first; got: {chosen}");
        return Task.CompletedTask;
    }

    private static async Task V37RefreshUpgradesRawLogToResultZip()
    {
        var dir = TempDir();
        var testing = Path.Combine(dir, "01_Testing", "FreeCam_R40.4.0_ResultPriority_Probe2");
        var logs = Path.Combine(testing, "Logs");
        var results = Path.Combine(testing, "Results");
        Directory.CreateDirectory(logs);
        Directory.CreateDirectory(results);
        var log = Path.Combine(logs, "ResultPriority_Probe2.log");
        await File.WriteAllTextAsync(log, "raw log first");

        var buildPath = Path.Combine(dir, "30_Experiment", "ResultPriority", "Probe2", "FreeCam_R40.4.0_ResultPriority_Probe2.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(buildPath)!);
        await File.WriteAllBytesAsync(buildPath, []);
        var build = new Artifact
        {
            Path = buildPath,
            Name = Path.GetFileName(buildPath),
            Feature = "ResultPriority",
            Stage = "Probe2",
            TestingPath = testing,
            TestStatus = "已测试",
            ResultPath = log
        };
        var library = LibraryService.CreateInMemory([build]);

        var resultZip = Path.Combine(results, "FreeCam_R40.4.0_ResultPriority_Probe2_Result.zip");
        using (ZipFile.Open(resultZip, ZipArchiveMode.Create)) { }
        File.SetLastWriteTimeUtc(resultZip, DateTime.UtcNow.AddSeconds(1));

        var refresh = new TestStatusRefreshService(library, new TestResultService(new ManifestService()), dir);
        var changed = await refresh.RefreshAsync();
        var updated = library.ByPath(buildPath)!;
        Assert(changed, "refresh must re-scan a tested build whose stored evidence is only a raw log");
        Assert(string.Equals(updated.ResultPath, resultZip, StringComparison.OrdinalIgnoreCase),
            $"refresh must upgrade stored raw log to Result ZIP; got: {updated.ResultPath}");
    }

    private static Task V37TestedStatusClickContract()
    {
        var sourceRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "FreeCamManager"));
        var view = File.ReadAllText(Path.Combine(sourceRoot, "Views", "DevelopmentView.xaml"));
        var code = File.ReadAllText(Path.Combine(sourceRoot, "Views", "DevelopmentView.xaml.cs"));
        Assert(view.Contains("PreviewMouseLeftButtonUp=\"ResultBadge_PreviewMouseLeftButtonUp\"", StringComparison.Ordinal),
            "tested status badge must have a click-up handler in addition to drag handlers");
        Assert(code.Contains("ResultBadge_PreviewMouseLeftButtonUp", StringComparison.Ordinal)
            && code.Contains("OpenResultCommand.CanExecute", StringComparison.Ordinal)
            && code.Contains("OpenResultCommand.Execute", StringComparison.Ordinal),
            "tested status click must invoke the existing Result location command");
        Assert(code.Contains("_resultDragStarted", StringComparison.Ordinal),
            "status click must distinguish a click from a completed file drag");
        return Task.CompletedTask;
    }


    private static Task V371BackgroundRefreshPreservesManualConclusionInteraction()
    {
        var sourceRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "FreeCamManager"));
        var development = File.ReadAllText(Path.Combine(sourceRoot, "ViewModels", "DevelopmentViewModel.cs"));
        var row = File.ReadAllText(Path.Combine(sourceRoot, "ViewModels", "ArtifactRowViewModel.cs"));

        Assert(development.Contains("RefreshFromArtifact", StringComparison.Ordinal)
            && development.Contains("Items.Move(", StringComparison.Ordinal)
            && !development.Contains("Items.Clear();", StringComparison.Ordinal),
            "background refresh must reconcile existing rows instead of clearing and recreating the list");
        Assert(row.Contains("public void RefreshFromArtifact(Artifact artifact)", StringComparison.Ordinal),
            "row view models must refresh their backing artifact in place so an open manual-conclusion picker survives background refresh");
        return Task.CompletedTask;
    }


    private static Task V39ManagerAndIndexLibraryClassification()
    {
        var classification = new ClassificationService();

        var manager = classification.Plan(new Artifact
        {
            Name = "FreeCam_Manager_V3.9_Source.zip",
            ArtifactType = "Source",
            BuildType = "Stable"
        });
        Assert(manager.Category == "Manager", "FreeCam_Manager_* must be classified as Manager before normal build rules");
        Assert(manager.RelativeDirectory == "50_Manager", "Manager artifacts must move to 50_Manager");
        Assert(classification.CategoryLabel(new Artifact { Category = "Manager" }) == "管理器", "Manager category label missing");

        foreach (var name in new[] { "WW底层索引_Fix12_VisualFix1.zip", "WW底层索引库_R40.4.0_LookAt正式回填.zip" })
        {
            var index = classification.Plan(new Artifact { Name = name, BuildType = "Probe", ArtifactType = "Runtime" });
            Assert(index.Category == "IndexLibrary", $"{name} must be classified as IndexLibrary before Probe/Experiment rules");
            Assert(index.RelativeDirectory == "60_索引库", $"{name} must move to 60_索引库");
        }
        Assert(classification.CategoryLabel(new Artifact { Category = "IndexLibrary" }) == "索引库", "IndexLibrary category label missing");
        return Task.CompletedTask;
    }

    private static async Task V39FiveStarLocksWithoutAutoUnlock()
    {
        var dir = TempDir();
        var file = Path.Combine(dir, "library.json");
        var artifactPath = Path.Combine(dir, "FreeCam_R40.4.0_Test.zip");
        var library = await LibraryService.LoadAsync(file);
        library.Upsert(new Artifact { Path = artifactPath, Name = Path.GetFileName(artifactPath), Rating = 0, Protected = false });

        Assert(library.SetRating(artifactPath, 5), "five-star rating update should find the artifact");
        Assert(library.ByPath(artifactPath)?.Protected == true, "five-star rating must lock the artifact");

        Assert(library.SetRating(artifactPath, 4), "rating downgrade should find the artifact");
        Assert(library.ByPath(artifactPath)?.Protected == true, "rating below five must not auto-unlock an already protected artifact");
    }

    private static Task V39UiProtectionAndSettingsContract()
    {
        var sourceRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "FreeCamManager"));
        var coreRoot = Path.GetFullPath(Path.Combine(sourceRoot, "..", "FreeCamManager.Core"));
        var dev = File.ReadAllText(Path.Combine(sourceRoot, "Views", "DevelopmentView.xaml"));
        var history = File.ReadAllText(Path.Combine(sourceRoot, "Views", "HistoryView.xaml"));
        var settings = File.ReadAllText(Path.Combine(sourceRoot, "Views", "SettingsView.xaml"));
        var row = File.ReadAllText(Path.Combine(sourceRoot, "ViewModels", "ArtifactRowViewModel.cs"));
        var dark = File.ReadAllText(Path.Combine(sourceRoot, "Styles", "Colors.Dark.xaml"));
        var light = File.ReadAllText(Path.Combine(sourceRoot, "Styles", "Colors.Light.xaml"));
        var watcher = File.ReadAllText(Path.Combine(coreRoot, "Services", "InboxWatcherService.cs"));
        var organizer = File.ReadAllText(Path.Combine(coreRoot, "Services", "OrganizerService.cs"));
        var rebuild = File.ReadAllText(Path.Combine(coreRoot, "Services", "LibraryRebuildService.cs"));

        Assert(!dev.Contains("LockToggleControl", StringComparison.Ordinal) && !dev.Contains("Text=\"锁\"", StringComparison.Ordinal),
            "development list must not keep a dedicated lock column");
        Assert(!history.Contains("LockToggleControl", StringComparison.Ordinal) && !history.Contains("Text=\"锁\"", StringComparison.Ordinal),
            "history list must not keep a dedicated lock column");
        Assert(dev.Contains("ProtectionMenuText", StringComparison.Ordinal) && dev.Contains("ToggleProtectionCommand", StringComparison.Ordinal)
            && history.Contains("ProtectionMenuText", StringComparison.Ordinal) && history.Contains("ToggleProtectionCommand", StringComparison.Ordinal),
            "lock/unlock must be available from the row context menu");
        Assert(row.Contains("clamped == 5", StringComparison.Ordinal) && row.Contains("已标记 5 星并锁定保护", StringComparison.Ordinal),
            "setting 5 stars must also lock the artifact");
        Assert(row.Contains("ProtectionMenuText", StringComparison.Ordinal) && row.Contains("ToggleProtectionCommand", StringComparison.Ordinal),
            "row view model must expose context-menu protection state and command");

        Assert(dark.Contains("x:Key=\"StarActiveBrush\" Color=\"#5B8DEF\"", StringComparison.Ordinal),
            "dark theme active star color must match AccentBrush blue");
        Assert(light.Contains("x:Key=\"StarActiveBrush\" Color=\"#3F73D8\"", StringComparison.Ordinal),
            "light theme active star color must match AccentBrush blue");

        Assert(settings.Contains("x:Name=\"SettingsBehaviorGrid\"", StringComparison.Ordinal),
            "V3.9.2 display-and-behavior settings must use a 3x2 grid");
        Assert(!settings.Contains("x:Name=\"SettingsBehaviorSingleRow\"", StringComparison.Ordinal),
            "V3.9.2 must not retain the V3.9.1 single-row settings layout");
        var gridStart = settings.IndexOf("x:Name=\"SettingsBehaviorGrid\"", StringComparison.Ordinal);
        var termsStart = settings.IndexOf("x:Name=\"SettingsRowTerms\"", StringComparison.Ordinal);
        Assert(gridStart >= 0 && termsStart > gridStart, "V3.9.2 settings grid marker is missing or out of order");
        var behaviorGrid = settings[gridStart..termsStart];
        var expectedCells = new[]
        {
            ("Grid.Row=\"0\" Grid.Column=\"0\"", "界面主题"),
            ("Grid.Row=\"0\" Grid.Column=\"2\"", "已废弃自动删除"),
            ("Grid.Row=\"0\" Grid.Column=\"4\"", "隐藏 FreeCam_ 文件名前缀"),
            ("Grid.Row=\"2\" Grid.Column=\"0\"", "显示文件名中文别名"),
            ("Grid.Row=\"2\" Grid.Column=\"2\"", "功能中文显示"),
            ("Grid.Row=\"2\" Grid.Column=\"4\"", "阶段中文显示")
        };
        foreach (var (cell, label) in expectedCells)
        {
            var cellPos = behaviorGrid.IndexOf(cell, StringComparison.Ordinal);
            var labelPos = cellPos < 0 ? -1 : behaviorGrid.IndexOf(label, cellPos, StringComparison.Ordinal);
            Assert(cellPos >= 0 && labelPos >= cellPos && labelPos - cellPos < 900,
                $"V3.9.2 settings item is missing from expected 3x2 cell: {label}");
        }

        Assert(!watcher.Contains("if (name.StartsWith(\"FreeCam_Manager_\"", StringComparison.Ordinal),
            "inbox watcher must no longer ignore Manager packages");
        Assert(watcher.Contains("decision.Category is not \"Manager\" and not \"IndexLibrary\"", StringComparison.Ordinal),
            "Manager/IndexLibrary packages must bypass 01_Testing extraction");
        Assert(organizer.Contains("\"50_Manager\"", StringComparison.Ordinal) && organizer.Contains("\"60_索引库\"", StringComparison.Ordinal),
            "organizer must create Manager and IndexLibrary roots");
        Assert(rebuild.Contains("\"50_Manager\"", StringComparison.Ordinal) && rebuild.Contains("\"60_索引库\"", StringComparison.Ordinal)
            && rebuild.Contains("\"50_Manager\" => \"Manager\"", StringComparison.Ordinal)
            && rebuild.Contains("\"60_索引库\" => \"IndexLibrary\"", StringComparison.Ordinal),
            "library rebuild must reconcile Manager and IndexLibrary roots");
        return Task.CompletedTask;
    }

    private static async Task V371CorruptedLibraryRestoresFromBackup()
    {
        var dir = TempDir();
        var file = Path.Combine(dir, "library.json");
        var backup = file + ".bak";
        var artifact = new Artifact
        {
            Path = Path.Combine(dir, "FreeCam_R40.4.0_LookAt_Probe1.zip"),
            Name = "FreeCam_R40.4.0_LookAt_Probe1.zip",
            Category = "Experiment",
            Feature = "LookAt",
            Stage = "Probe1",
            ManualStatus = "通过",
            Rating = 4,
            Notes = "keep me"
        };
        var json = JsonSerializer.Serialize(new { items = new[] { artifact } }, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(backup, json, new UTF8Encoding(false));
        await File.WriteAllBytesAsync(file, Enumerable.Repeat((byte)0, 4096).ToArray());

        var recovered = await LibraryService.LoadAsync(file);
        var item = recovered.Snapshot().Single();
        Assert(item.ManualStatus == "通过" && item.Rating == 4 && item.Notes == "keep me",
            "valid .bak library must recover metadata when primary library is corrupt");
        var restoredBytes = await File.ReadAllBytesAsync(file);
        Assert(restoredBytes.Any(b => b != 0), "backup recovery must restore a valid primary library file");
    }

    private static async Task V371SafeSaveKeepsBackup()
    {
        var dir = TempDir();
        var file = Path.Combine(dir, "library.json");
        var artifactPath = Path.Combine(dir, "FreeCam_R40.4.0_Test.zip");
        var original = new Artifact { Path = artifactPath, Name = Path.GetFileName(artifactPath), ManualStatus = "通过", Rating = 2 };
        var json = JsonSerializer.Serialize(new { items = new[] { original } }, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(file, json, new UTF8Encoding(false));

        var library = await LibraryService.LoadAsync(file);
        library.SetManualStatus(artifactPath, "待复测");
        library.SetRating(artifactPath, 5);
        await library.SaveAsync();

        var backup = file + ".bak";
        Assert(File.Exists(backup), "safe library save must preserve a .bak file");
        var previous = await LibraryService.LoadAsync(backup);
        var oldItem = previous.ByPath(artifactPath);
        Assert(oldItem is not null && oldItem.ManualStatus == "通过" && oldItem.Rating == 2,
            ".bak must contain the last-known-good library before replacement");

        var current = await LibraryService.LoadAsync(file);
        var newItem = current.ByPath(artifactPath);
        Assert(newItem is not null && newItem.ManualStatus == "待复测" && newItem.Rating == 5,
            "safe save must commit the new library after backup creation");
    }

    private static async Task V371RootReconciliationRebuildsManagedLibrary()
    {
        var dir = TempDir();
        var root = Path.Combine(dir, "FreeCam");
        var buildDir = Path.Combine(root, "30_Experiment", "LookAt", "Probe1");
        var resultDir = Path.Combine(root, "40_Result", "LookAt", "Probe1");
        var testingDir = Path.Combine(root, "01_Testing", "FreeCam_R40.4.0_LookAt_Probe1");
        Directory.CreateDirectory(buildDir);
        Directory.CreateDirectory(resultDir);
        Directory.CreateDirectory(testingDir);

        var buildPath = Path.Combine(buildDir, "FreeCam_R40.4.0_LookAt_Probe1.zip");
        var resultPath = Path.Combine(resultDir, "FreeCam_R40.4.0_LookAt_Probe1_Result.zip");
        using (ZipFile.Open(buildPath, ZipArchiveMode.Create)) { }
        using (ZipFile.Open(resultPath, ZipArchiveMode.Create)) { }

        var libraryFile = Path.Combine(dir, "library.json");
        var library = await LibraryService.LoadAsync(libraryFile);
        var type = typeof(LibraryService).Assembly.GetType("FreeCamManager.Core.Services.LibraryRebuildService");
        Assert(type is not null, "LibraryRebuildService must exist");
        var instance = Activator.CreateInstance(type!, library, new ManifestService(), new HashService(), null);
        Assert(instance is not null, "LibraryRebuildService constructor failed");
        var method = type!.GetMethod("ReconcileAsync");
        Assert(method is not null, "LibraryRebuildService.ReconcileAsync is missing");
        var task = method!.Invoke(instance, new object?[] { root, CancellationToken.None }) as Task;
        Assert(task is not null, "ReconcileAsync did not return a Task");
        await task!;

        var build = library.ByPath(buildPath);
        var result = library.ByPath(resultPath);
        Assert(build is not null && build.Category == "Experiment",
            "existing 30_Experiment build must be rebuilt into the library");
        Assert(result is not null && result.Category == "Result" && result.ArtifactType == "Result",
            "existing 40_Result package must be rebuilt as Result");
        Assert(build is not null && string.Equals(build.TestingPath, testingDir, StringComparison.OrdinalIgnoreCase),
            "rebuild must reconnect matching 01_Testing workspace");
        Assert(build is not null && string.Equals(build.ResultPath, resultPath, StringComparison.OrdinalIgnoreCase),
            "rebuild must reconnect matching Result package");
        Assert(File.Exists(buildPath) && File.Exists(resultPath),
            "root reconciliation must index files in place without moving them");
        Assert(File.Exists(libraryFile), "root reconciliation must persist the rebuilt library");
    }

    private static async Task V371RootReconciliationPreservesManualMetadata()
    {
        var dir = TempDir();
        var root = Path.Combine(dir, "FreeCam");
        var buildDir = Path.Combine(root, "20_Feature", "Camera", "Test1");
        Directory.CreateDirectory(buildDir);
        var buildPath = Path.Combine(buildDir, "FreeCam_R40.4.0_Camera_Test1.zip");
        using (ZipFile.Open(buildPath, ZipArchiveMode.Create)) { }

        var libraryFile = Path.Combine(dir, "library.json");
        var seeded = new Artifact
        {
            Path = buildPath,
            RelativePath = Path.GetRelativePath(root, buildPath),
            Name = Path.GetFileName(buildPath),
            Category = "Feature",
            Feature = "Camera",
            Stage = "Test1",
            ManualStatus = "通过",
            Rating = 5,
            Notes = "人工备注",
            Protected = true
        };
        var seedJson = JsonSerializer.Serialize(new { items = new[] { seeded } }, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(libraryFile, seedJson, new UTF8Encoding(false));
        var library = await LibraryService.LoadAsync(libraryFile);

        var type = typeof(LibraryService).Assembly.GetType("FreeCamManager.Core.Services.LibraryRebuildService");
        Assert(type is not null, "LibraryRebuildService must exist");
        var instance = Activator.CreateInstance(type!, library, new ManifestService(), new HashService(), null);
        var task = type!.GetMethod("ReconcileAsync")!.Invoke(instance, new object?[] { root, CancellationToken.None }) as Task;
        Assert(task is not null, "ReconcileAsync did not return a Task");
        await task!;

        var item = library.ByPath(buildPath);
        Assert(item is not null && item.ManualStatus == "通过" && item.Rating == 5
            && item.Notes == "人工备注" && item.Protected,
            "root reconciliation must never erase existing manual conclusion/rating/notes/lock metadata");
    }

    private static Task V371RecoveryWiringContract()
    {
        var sourceRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "FreeCamManager"));
        var app = File.ReadAllText(Path.Combine(sourceRoot, "App.xaml.cs"));
        var watcher = File.ReadAllText(Path.Combine(sourceRoot, "..", "FreeCamManager.Core", "Services", "InboxWatcherService.cs"));
        var library = File.ReadAllText(Path.Combine(sourceRoot, "..", "FreeCamManager.Core", "Services", "LibraryService.cs"));

        var legacyJsonStartup = app.Contains("System.Text.Json.JsonException", StringComparison.Ordinal)
            && app.Contains("LibraryService.CreateEmpty(AppPaths.LibraryFile)", StringComparison.Ordinal);
        var sqliteStartup = app.Contains("ProductionManagerSqliteLibrarySession.OpenAsync", StringComparison.Ordinal)
            && app.Contains("LibraryService.CreatePersistent", StringComparison.Ordinal);
        Assert(legacyJsonStartup || sqliteStartup,
            "startup must wire either the V3.7 JSON recovery path or the V3.8 SQLite recovery path");
        Assert(app.Contains("LibraryRebuildService", StringComparison.Ordinal)
            && app.Contains("ReconcileAsync(settings.RootDir", StringComparison.Ordinal),
            "startup must reconcile already-classified files from the configured FreeCam root");
        Assert(watcher.Contains("manual && _rebuild is not null", StringComparison.Ordinal)
            && watcher.Contains("_rebuild.ReconcileAsync(cfg.RootDir", StringComparison.Ordinal),
            "manual Refresh must reconcile the managed root as well as scan the inbox");
        Assert(library.Contains(".bak", StringComparison.Ordinal)
            && library.Contains("flushToDisk: true", StringComparison.Ordinal)
            && library.Contains("ValidateLibraryFileAsync", StringComparison.Ordinal),
            "library persistence must validate temp JSON, flush to disk, and keep a backup");
        return Task.CompletedTask;
    }

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "FreeCamManagerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static async Task Run(string name, Func<Task> test)
    {
        Console.WriteLine($"START {name}");
        Console.Out.Flush();
        try
        {
            var task = test();
            var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(30)));
            if (completed != task) throw new TimeoutException($"test exceeded 30 seconds: {name}");
            await task;
            Console.WriteLine($"PASS {name}");
            Console.Out.Flush();
            _passed++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAIL {name}: {ex}");
            Console.Out.Flush();
            _failed++;
        }
    }



    private sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public int TermsRequests { get; private set; }
        public bool AllRequestsBypassCache { get; private set; } = true;
        public bool SawGitHubManifestApi { get; private set; }
        public bool SawGitHubJsonAccept { get; private set; }
        public bool SawRawManagerManifest { get; private set; }
        public bool SawUserAgent { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsolutePath.EndsWith("FilenameTerms.json", StringComparison.OrdinalIgnoreCase) == true)
                TermsRequests++;
            if (string.Equals(request.RequestUri?.Host, "api.github.com", StringComparison.OrdinalIgnoreCase)
                && request.RequestUri?.AbsolutePath.EndsWith("/contents/manager/update.json", StringComparison.OrdinalIgnoreCase) == true)
            {
                SawGitHubManifestApi = true;
                SawGitHubJsonAccept |= request.Headers.Accept.Any(x => string.Equals(x.MediaType, "application/vnd.github+json", StringComparison.OrdinalIgnoreCase));
            }
            if (string.Equals(request.RequestUri?.Host, "raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase)
                && request.RequestUri?.AbsolutePath.EndsWith("/manager/update.json", StringComparison.OrdinalIgnoreCase) == true)
            {
                SawRawManagerManifest = true;
            }
            SawUserAgent |= request.Headers.UserAgent.Count > 0;
            var hasCacheBust = !string.IsNullOrWhiteSpace(request.RequestUri?.Query) && request.RequestUri!.Query.Contains("_=", StringComparison.Ordinal);
            var hasNoCache = request.Headers.TryGetValues("Cache-Control", out var cacheHeaders)
                && cacheHeaders.Any(x => x.Contains("no-cache", StringComparison.OrdinalIgnoreCase));
            AllRequestsBypassCache &= hasCacheBust && hasNoCache;
            return Task.FromResult(responder(request));
        }
    }

    private sealed class DelayedManifestHandler(string payload) : HttpMessageHandler
    {
        private int _requestCount;
        public int RequestCount => _requestCount;
        public bool SawRawManagerManifest { get; private set; }
        public bool AllRequestsBypassCache { get; private set; } = true;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            SawRawManagerManifest |= string.Equals(request.RequestUri?.Host, "raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase)
                && request.RequestUri?.AbsolutePath.EndsWith("/manager/update.json", StringComparison.OrdinalIgnoreCase) == true;
            var hasCacheBust = !string.IsNullOrWhiteSpace(request.RequestUri?.Query)
                && request.RequestUri!.Query.Contains("_=", StringComparison.Ordinal);
            var hasNoCache = request.Headers.TryGetValues("Cache-Control", out var cacheHeaders)
                && cacheHeaders.Any(x => x.Contains("no-cache", StringComparison.OrdinalIgnoreCase));
            AllRequestsBypassCache &= hasCacheBust && hasNoCache;
            await Task.Delay(80, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
