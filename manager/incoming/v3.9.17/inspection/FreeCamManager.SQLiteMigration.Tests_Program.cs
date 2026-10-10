using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FreeCamManager.Core.Models;
using FreeCamManager.Core.Services;
using FreeCamManager.SQLiteMigration.Core;

var tests = new List<(string Name, Func<Task> Run)>
{
    ("Read-only JSON reader loads primary without mutation", TestReadOnlyPrimaryAsync),
    ("Read-only JSON reader falls back to backup without restoring primary", TestBackupFallbackAsync),
    ("Read-only JSON reader refuses corrupt source without backup", TestCorruptSourceRefusalAsync),
    ("SQLite staged migration preserves complete artifact snapshot", TestRoundTripAsync),
    ("Repeated migration is idempotent", TestIdempotentAsync),
    ("Injected pre-promotion failure preserves previous database", TestPrePromotionFailureAsync),
    ("Production write paths are rejected", TestSafetyBoundaryAsync),
    ("MigrationPaths default production directory follows AppPaths.LibraryFile", TestMigrationPathsDefaultProductionDirectory),
    ("Manager SQLite session persists manual metadata across reopen", TestManagerSessionPersistenceAsync),
    ("Manager SQLite session restores newest validated backup", TestManagerSessionBackupRecoveryAsync),
    ("Manager SQLite session never mutates production JSON", TestManagerSessionProductionJsonReadOnlyAsync),
    ("LibraryService external persistence receives complete snapshot", TestLibraryServicePersistentAdapterAsync),
    ("RC1 production file services operate inside sandbox", TestProductionFileServicesAsync),
    ("Manager SQLite session retains at most ten rolling backups", TestManagerSessionBackupRetentionAsync),
    ("Manager SQLite session falls back to JSON export after DB generations fail", TestManagerSessionJsonExportRecoveryAsync),
    ("SQLite-backed Organizer move survives reopen", TestSqliteOrganizerPersistenceAsync),
    ("V3.8 production storage boundary allows Manager data and rejects FreeCam", TestProductionBoundaryAsync),
    ("V3.8 first migration preserves legacy JSON and creates rollback evidence", TestProductionFirstMigrationAsync),
    ("V3.8 production session restores backup and retains newest ten", TestProductionRecoveryAndRetentionAsync),
    ("V3.9.17 SQLite delta preserves updates, deletes and recovery evidence", TestProductionDeltaSessionAsync),
    ("V3.8 startup survives corrupt DB/backups/recovery/legacy sources", TestProductionAllSourcesCorruptCreatesWritableNewDatabaseAsync),
    ("V3.8 rollback writes latest SQLite snapshot to legacy JSON atomically", TestProductionRollbackAsync),
};

var passed = 0;
foreach (var (name, run) in tests)
{
    try
    {
        await run();
        Console.WriteLine($"PASS: {name}");
        passed++;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"FAIL: {name}\n{ex}");
    }
}
Console.WriteLine($"SQLite Migration Tests: {passed} passed, {tests.Count - passed} failed");
return passed == tests.Count ? 0 : 1;

static async Task TestReadOnlyPrimaryAsync()
{
    using var env = TestEnvironment.Create();
    var item = SampleArtifact("primary.zip", "人工保留", 5, "primary note");
    WriteLibrary(env.Primary, [item]);
    var beforeBytes = await File.ReadAllBytesAsync(env.Primary);
    var beforeWrite = File.GetLastWriteTimeUtc(env.Primary);
    await Task.Delay(20);

    var reader = new ReadOnlyLibraryJsonReader();
    var snapshot = await reader.ReadAsync(env.Primary);

    Equal(Path.GetFullPath(env.Primary), snapshot.SourceFile, "primary source");
    Equal(1, snapshot.Items.Count, "item count");
    Equal(ArtifactSnapshotFingerprint.Compute([item]), ArtifactSnapshotFingerprint.Compute(snapshot.Items), "fingerprint");
    SequenceEqual(beforeBytes, await File.ReadAllBytesAsync(env.Primary), "primary bytes changed");
    Equal(beforeWrite, File.GetLastWriteTimeUtc(env.Primary), "primary timestamp changed");
}

static async Task TestBackupFallbackAsync()
{
    using var env = TestEnvironment.Create();
    await File.WriteAllBytesAsync(env.Primary, new byte[4096]);
    var primaryBefore = await File.ReadAllBytesAsync(env.Primary);
    var primaryWrite = File.GetLastWriteTimeUtc(env.Primary);
    var item = SampleArtifact("backup.zip", "待测试", 3, "backup note");
    WriteLibrary(env.Backup, [item]);

    var reader = new ReadOnlyLibraryJsonReader();
    var snapshot = await reader.ReadAsync(env.Primary);

    Equal(Path.GetFullPath(env.Backup), snapshot.SourceFile, "backup source");
    SequenceEqual(primaryBefore, await File.ReadAllBytesAsync(env.Primary), "corrupt primary was modified");
    Equal(primaryWrite, File.GetLastWriteTimeUtc(env.Primary), "corrupt primary timestamp changed");
    Equal(ArtifactSnapshotFingerprint.Compute([item]), ArtifactSnapshotFingerprint.Compute(snapshot.Items), "backup fingerprint");
}

static async Task TestCorruptSourceRefusalAsync()
{
    using var env = TestEnvironment.Create();
    await File.WriteAllBytesAsync(env.Primary, new byte[1024]);
    var reader = new ReadOnlyLibraryJsonReader();
    await ThrowsAsync<InvalidDataException>(() => reader.ReadAsync(env.Primary));
}

static async Task TestRoundTripAsync()
{
    using var env = TestEnvironment.Create();
    var items = new[]
    {
        SampleArtifact("one.zip", "人工保留", 5, "note one"),
        SampleArtifact("two.zip", "已废弃", 2, "note two")
    };
    WriteLibrary(env.Primary, items);
    var source = await new ReadOnlyLibraryJsonReader().ReadAsync(env.Primary);
    var paths = new MigrationPaths(env.ExperimentRoot, env.ProductionRoot, env.FreeCamRoot);
    var coordinator = new LibraryMigrationCoordinator(paths, new MigrationSafetyBoundary(paths));
    var report = await coordinator.MigrateAsync(source);

    True(report.IntegrityOk, "integrity not ok");
    Equal(source.SnapshotFingerprint, report.SnapshotFingerprint, "report fingerprint");
    var store = new SqliteMigrationStore(paths.DatabaseFile, new MigrationSafetyBoundary(paths));
    var loaded = await store.LoadSnapshotAsync();
    Equal(source.SnapshotFingerprint, ArtifactSnapshotFingerprint.Compute(loaded), "sqlite fingerprint");
}

static async Task TestIdempotentAsync()
{
    using var env = TestEnvironment.Create();
    var item = SampleArtifact("same.zip", "待测试", 4, "same note");
    WriteLibrary(env.Primary, [item]);
    var source = await new ReadOnlyLibraryJsonReader().ReadAsync(env.Primary);
    var paths = new MigrationPaths(env.ExperimentRoot, env.ProductionRoot, env.FreeCamRoot);
    var safety = new MigrationSafetyBoundary(paths);
    var coordinator = new LibraryMigrationCoordinator(paths, safety);
    var first = await coordinator.MigrateAsync(source);
    var firstHash = FileSha256(paths.DatabaseFile);
    var second = await coordinator.MigrateAsync(source);
    var store = new SqliteMigrationStore(paths.DatabaseFile, safety);
    var loaded = await store.LoadSnapshotAsync();

    Equal(first.SnapshotFingerprint, second.SnapshotFingerprint, "idempotent report fingerprint");
    Equal(source.SnapshotFingerprint, ArtifactSnapshotFingerprint.Compute(loaded), "idempotent loaded fingerprint");
    True(File.Exists(paths.DatabaseFile), "database missing");
    True(!string.IsNullOrWhiteSpace(firstHash), "first db hash empty");
}

static async Task TestPrePromotionFailureAsync()
{
    using var env = TestEnvironment.Create();
    var paths = new MigrationPaths(env.ExperimentRoot, env.ProductionRoot, env.FreeCamRoot);
    var safety = new MigrationSafetyBoundary(paths);
    var coordinator = new LibraryMigrationCoordinator(paths, safety);

    var oldItem = SampleArtifact("old.zip", "人工保留", 5, "old");
    WriteLibrary(env.Primary, [oldItem]);
    var oldSource = await new ReadOnlyLibraryJsonReader().ReadAsync(env.Primary);
    await coordinator.MigrateAsync(oldSource);
    var oldFingerprint = oldSource.SnapshotFingerprint;

    var newItem = SampleArtifact("new.zip", "已废弃", 1, "new");
    WriteLibrary(env.Primary, [newItem]);
    var newSource = await new ReadOnlyLibraryJsonReader().ReadAsync(env.Primary);
    await ThrowsAsync<IOException>(() => coordinator.MigrateAsync(newSource, MigrationFault.BeforePromotion));

    var store = new SqliteMigrationStore(paths.DatabaseFile, safety);
    var loaded = await store.LoadSnapshotAsync();
    Equal(oldFingerprint, ArtifactSnapshotFingerprint.Compute(loaded), "old db was not preserved");
}

static Task TestMigrationPathsDefaultProductionDirectory()
{
    var paths = MigrationPaths.CreateDefault();
    var expected = Path.GetFullPath(Path.GetDirectoryName(AppPaths.LibraryFile)
        ?? throw new InvalidOperationException($"Cannot resolve data directory from {AppPaths.LibraryFile}."));
    Equal(expected, paths.ProductionDataDirectory, "default production data directory");
    return Task.CompletedTask;
}

static Task TestSafetyBoundaryAsync()
{
    using var env = TestEnvironment.Create();
    var paths = new MigrationPaths(env.ExperimentRoot, env.ProductionRoot, env.FreeCamRoot);
    var safety = new MigrationSafetyBoundary(paths);
    safety.EnsureExperimentWriteAllowed(Path.Combine(env.ExperimentRoot, "ok.db"));
    Throws<UnauthorizedAccessException>(() => safety.EnsureExperimentWriteAllowed(Path.Combine(env.ProductionRoot, "library.json")));
    Throws<UnauthorizedAccessException>(() => safety.EnsureExperimentWriteAllowed(Path.Combine(env.FreeCamRoot, "20_Feature", "x.zip")));
    return Task.CompletedTask;
}


static async Task TestManagerSessionPersistenceAsync()
{
    using var env = TestEnvironment.Create();
    var sourceItem = SampleArtifact("session.zip", "待复测", 2, "before");
    WriteLibrary(env.Primary, [sourceItem]);
    var paths = new MigrationPaths(env.ExperimentRoot, env.ProductionRoot, env.FreeCamRoot);

    var first = await ManagerSqliteLibrarySession.OpenAsync(paths, env.Primary);
    var library = FreeCamManager.Core.Services.LibraryService.CreatePersistent(first.Items, first.SaveAsync);
    var path = sourceItem.Path;
    True(library.SetManualStatus(path, "通过"), "manual status mutation failed");
    True(library.SetRating(path, 5), "rating mutation failed");
    True(library.SetNotes(path, "sqlite persisted note"), "notes mutation failed");
    var toggled = library.ToggleProtected(path);
    True(toggled.Found && !toggled.Locked, "protected toggle failed");
    await library.SaveAsync();

    var second = await ManagerSqliteLibrarySession.OpenAsync(paths, env.Primary);
    var got = second.Items.Single(x => string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase));
    Equal("通过", got.ManualStatus, "manual status not persisted");
    Equal(5, got.Rating, "rating not persisted");
    Equal("sqlite persisted note", got.Notes, "notes not persisted");
    True(!got.Protected, "lock state not persisted");
    Equal("MainDatabase", second.RecoverySource, "clean reopen should use main database");
}

static async Task TestManagerSessionBackupRecoveryAsync()
{
    using var env = TestEnvironment.Create();
    var item = SampleArtifact("recover.zip", "待复测", 2, "v0");
    WriteLibrary(env.Primary, [item]);
    var paths = new MigrationPaths(env.ExperimentRoot, env.ProductionRoot, env.FreeCamRoot);
    var first = await ManagerSqliteLibrarySession.OpenAsync(paths, env.Primary);
    var v1 = first.Items.Select(x => x.Clone()).ToList();
    v1[0].Notes = "v1";
    await first.SaveAsync(v1);
    var v2 = v1.Select(x => x.Clone()).ToList();
    v2[0].Notes = "v2";
    await first.SaveAsync(v2);
    True(Directory.EnumerateFiles(paths.BackupsDirectory, "library-*.db").Any(), "rolling backup missing");

    await File.WriteAllBytesAsync(paths.DatabaseFile, new byte[8192]);
    var recovered = await ManagerSqliteLibrarySession.OpenAsync(paths, env.Primary);
    Equal("SqliteBackup", recovered.RecoverySource, "corrupt main did not recover backup");
    True(recovered.Items.Count == 1, "recovered item count invalid");
}

static async Task TestManagerSessionProductionJsonReadOnlyAsync()
{
    using var env = TestEnvironment.Create();
    var item = SampleArtifact("readonly.zip", "通过", 4, "production");
    WriteLibrary(env.Primary, [item]);
    var before = await File.ReadAllBytesAsync(env.Primary);
    var beforeTime = File.GetLastWriteTimeUtc(env.Primary);
    await Task.Delay(20);
    var paths = new MigrationPaths(env.ExperimentRoot, env.ProductionRoot, env.FreeCamRoot);
    var session = await ManagerSqliteLibrarySession.OpenAsync(paths, env.Primary);
    var changed = session.Items.Select(x => x.Clone()).ToList();
    changed[0].Notes = "experiment only";
    await session.SaveAsync(changed);
    SequenceEqual(before, await File.ReadAllBytesAsync(env.Primary), "production JSON bytes changed");
    Equal(beforeTime, File.GetLastWriteTimeUtc(env.Primary), "production JSON timestamp changed");
}

static async Task TestLibraryServicePersistentAdapterAsync()
{
    var item = SampleArtifact("adapter.zip", "", 0, "before");
    IReadOnlyList<Artifact>? captured = null;
    var library = FreeCamManager.Core.Services.LibraryService.CreatePersistent([item], (items, _) =>
    {
        captured = items.Select(x => x.Clone()).ToList();
        return Task.CompletedTask;
    });
    library.SetManualStatus(item.Path, "通过");
    library.SetRating(item.Path, 3);
    library.SetNotes(item.Path, "adapter note");
    await library.SaveAsync();
    True(captured is not null && captured.Count == 1, "persistent adapter did not receive snapshot");
    Equal("通过", captured![0].ManualStatus, "adapter manual status mismatch");
    Equal(3, captured[0].Rating, "adapter rating mismatch");
    Equal("adapter note", captured[0].Notes, "adapter notes mismatch");
}


static async Task TestProductionFileServicesAsync()
{
    using var env = TestEnvironment.Create();
    var lib = FreeCamManager.Core.Services.LibraryService.CreateInMemory();
    var inbox = Path.Combine(env.FreeCamRoot, "00_Downloa");
    Directory.CreateDirectory(inbox);

    var organizerInput = Path.Combine(inbox, "FreeCam_R40.4.0_RC1Service_Probe1.zip");
    CreateFixtureZip(organizerInput, "RC1-SERVICE-ORGANIZER", "RC1Service", "Probe1");
    var organizer = new FreeCamManager.Core.Services.OrganizerService(
        env.FreeCamRoot,
        "",
        lib,
        new FreeCamManager.Core.Services.ManifestService(),
        new FreeCamManager.Core.Services.ClassificationService(),
        new FreeCamManager.Core.Services.HashService());
    var processed = await organizer.ProcessAsync(organizerInput);
    True(File.Exists(processed.Path), "RC1 Organizer did not write inside sandbox");
    True(Path.GetFullPath(processed.Path).StartsWith(Path.GetFullPath(env.FreeCamRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "RC1 Organizer escaped sandbox");

    var workspaceInput = Path.Combine(env.FreeCamRoot, "30_Experiment", "FreeCam_R40.4.0_RC1Workspace_Probe1.zip");
    CreateFixtureZip(workspaceInput, "RC1-SERVICE-WORKSPACE", "RC1Workspace", "Probe1");
    var workspace = new FreeCamManager.Core.Services.TestWorkspaceService(new FreeCamManager.Core.Services.ExtractionService());
    var prepared = await workspace.PrepareAsync(workspaceInput, Path.Combine(env.FreeCamRoot, "01_Testing"));
    True(Directory.Exists(prepared.TestingPath), "RC1 TestWorkspace did not extract inside sandbox");
    True(Path.GetFullPath(prepared.TestingPath).StartsWith(Path.GetFullPath(env.FreeCamRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "RC1 TestWorkspace escaped sandbox");
}


static async Task TestManagerSessionBackupRetentionAsync()
{
    using var env = TestEnvironment.Create();
    var item = SampleArtifact("retention.zip", "待复测", 2, "v0");
    WriteLibrary(env.Primary, [item]);
    var paths = new MigrationPaths(env.ExperimentRoot, env.ProductionRoot, env.FreeCamRoot);
    var session = await ManagerSqliteLibrarySession.OpenAsync(paths, env.Primary);
    var items = session.Items.Select(x => x.Clone()).ToList();
    for (var i = 1; i <= 14; i++)
    {
        items[0].Notes = "v" + i;
        await session.SaveAsync(items);
        await Task.Delay(2);
    }
    var backups = Directory.EnumerateFiles(paths.BackupsDirectory, "library-*.db").ToList();
    True(backups.Count <= 10 && backups.Count > 0, $"rolling backup retention invalid: {backups.Count}");
}

static async Task TestManagerSessionJsonExportRecoveryAsync()
{
    using var env = TestEnvironment.Create();
    var item = SampleArtifact("export-recover.zip", "通过", 5, "source");
    WriteLibrary(env.Primary, [item]);
    var paths = new MigrationPaths(env.ExperimentRoot, env.ProductionRoot, env.FreeCamRoot);
    var session = await ManagerSqliteLibrarySession.OpenAsync(paths, env.Primary);
    var changed = session.Items.Select(x => x.Clone()).ToList();
    changed[0].Notes = "export wins";
    await session.SaveAsync(changed);
    await session.SaveAsync(changed);
    True(File.Exists(session.ExportFile), "JSON export missing before recovery test");

    await File.WriteAllBytesAsync(paths.DatabaseFile, new byte[8192]);
    foreach (var backup in Directory.EnumerateFiles(paths.BackupsDirectory, "library-*.db"))
        await File.WriteAllBytesAsync(backup, new byte[4096]);

    var recovered = await ManagerSqliteLibrarySession.OpenAsync(paths, env.Primary);
    Equal("JsonExport", recovered.RecoverySource, "did not use JSON export fallback");
    Equal("export wins", recovered.Items.Single().Notes, "JSON export recovery lost latest metadata");
}

static async Task TestSqliteOrganizerPersistenceAsync()
{
    using var env = TestEnvironment.Create();
    WriteLibrary(env.Primary, []);
    var paths = new MigrationPaths(env.ExperimentRoot, env.ProductionRoot, env.FreeCamRoot);
    var session = await ManagerSqliteLibrarySession.OpenAsync(paths, env.Primary);
    var library = FreeCamManager.Core.Services.LibraryService.CreatePersistent(session.Items, session.SaveAsync);
    var inbox = Path.Combine(env.FreeCamRoot, "00_Downloa");
    Directory.CreateDirectory(inbox);
    var input = Path.Combine(inbox, "FreeCam_R40.4.0_Test3Unit_Probe1.zip");
    CreateFixtureZip(input, "T3-UNIT-ORGANIZER", "Test3Unit", "Probe1");
    var organizer = new FreeCamManager.Core.Services.OrganizerService(
        env.FreeCamRoot,
        Path.Combine(env.Root, "StableBackup"),
        library,
        new FreeCamManager.Core.Services.ManifestService(),
        new FreeCamManager.Core.Services.ClassificationService(),
        new FreeCamManager.Core.Services.HashService());

    var processed = await organizer.ProcessAsync(input);
    True(processed.Category == "Experiment", $"organizer category={processed.Category}");
    True(File.Exists(processed.Path), "organizer destination missing");
    True(!File.Exists(input), "organizer source was not moved");
    True(Path.GetFullPath(processed.Path).StartsWith(Path.GetFullPath(env.FreeCamRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "organizer escaped test root");

    var reopened = await ManagerSqliteLibrarySession.OpenAsync(paths, env.Primary);
    var got = reopened.Items.Single(x => string.Equals(x.BuildId, "T3-UNIT-ORGANIZER", StringComparison.OrdinalIgnoreCase));
    Equal(processed.Path, got.Path, "organizer path not persisted to SQLite");
    Equal("Experiment", got.Category, "organizer category not persisted to SQLite");
}


static Task TestProductionBoundaryAsync()
{
    using var env = TestEnvironment.Create();
    var paths = new ProductionStoragePaths(env.ProductionRoot, env.FreeCamRoot);
    var safety = new ProductionDataSafetyBoundary(paths);
    safety.EnsureWriteAllowed(paths.DatabaseFile);
    safety.EnsureWriteAllowed(paths.SqliteBackupsDirectory);
    Throws<UnauthorizedAccessException>(() => safety.EnsureWriteAllowed(paths.LegacyLibraryFile));
    Throws<UnauthorizedAccessException>(() => safety.EnsureWriteAllowed(Path.Combine(env.FreeCamRoot, "30_Experiment", "x.zip")));
    Throws<UnauthorizedAccessException>(() => safety.EnsureWriteAllowed(Path.Combine(env.Root, "Outside", "library.db")));
    return Task.CompletedTask;
}

static async Task TestProductionFirstMigrationAsync()
{
    using var env = TestEnvironment.Create();
    var item = SampleArtifact("prod-first.zip", "通过", 5, "legacy-note");
    WriteLibrary(env.Primary, [item]);
    var beforeBytes = await File.ReadAllBytesAsync(env.Primary);
    var beforeTime = File.GetLastWriteTimeUtc(env.Primary);
    var paths = new ProductionStoragePaths(env.ProductionRoot, env.FreeCamRoot);

    var session = await ProductionManagerSqliteLibrarySession.OpenAsync(paths);

    Equal("LegacyJson", session.RecoverySource, "first migration source");
    Equal(ArtifactSnapshotFingerprint.Compute([item]), ArtifactSnapshotFingerprint.Compute(session.Items), "first migration fingerprint");
    SequenceEqual(beforeBytes, await File.ReadAllBytesAsync(env.Primary), "legacy JSON bytes changed during first migration");
    Equal(beforeTime, File.GetLastWriteTimeUtc(env.Primary), "legacy JSON timestamp changed during first migration");
    True(File.Exists(paths.DatabaseFile), "production library.db missing");
    True(File.Exists(paths.RecoveryExportFile), "production recovery export missing");
    True(!string.IsNullOrWhiteSpace(session.MigrationBackupDirectory) && Directory.Exists(session.MigrationBackupDirectory), "pre-migration backup directory missing");
    var backedLegacy = Path.Combine(session.MigrationBackupDirectory, "library.json");
    True(File.Exists(backedLegacy), "pre-migration legacy JSON copy missing");
    SequenceEqual(beforeBytes, await File.ReadAllBytesAsync(backedLegacy), "pre-migration legacy backup bytes mismatch");
}

static async Task TestProductionRecoveryAndRetentionAsync()
{
    using var env = TestEnvironment.Create();
    WriteLibrary(env.Primary, [SampleArtifact("prod-recover.zip", "待复测", 2, "v0")]);
    var paths = new ProductionStoragePaths(env.ProductionRoot, env.FreeCamRoot);
    var session = await ProductionManagerSqliteLibrarySession.OpenAsync(paths);
    var items = session.Items.Select(x => x.Clone()).ToList();
    for (var i = 1; i <= 14; i++)
    {
        items[0].Notes = "prod-v" + i;
        await session.SaveAsync(items);
        await Task.Delay(2);
    }
    var backups = Directory.EnumerateFiles(paths.SqliteBackupsDirectory, "library-*.db").ToList();
    True(backups.Count is > 0 and <= 10, $"production backup retention invalid: {backups.Count}");

    await File.WriteAllBytesAsync(paths.DatabaseFile, new byte[8192]);
    var recovered = await ProductionManagerSqliteLibrarySession.OpenAsync(paths);
    Equal("SqliteBackup", recovered.RecoverySource, "production backup recovery source");
    True(recovered.Items.Count == 1, "production backup recovery item count");
}

static async Task TestProductionDeltaSessionAsync()
{
    using var env = TestEnvironment.Create();
    var paths = new ProductionStoragePaths(env.ProductionRoot, env.FreeCamRoot);
    var session = await ProductionManagerSqliteLibrarySession.OpenAsync(paths);

    var original = SampleArtifact("R40.5.0_W37.zip", "通过", 4, "release manual notes");
    var removable = SampleArtifact("old-deleted.zip", "已废弃", 1, "preserve until deletion");
    await session.SaveAsync([original, removable]);

    var added = SampleArtifact("WW37_GIBloom_Probe2.91.zip", "待测试", 0, "new import");
    await session.SaveAsync([original, removable, added]);

    var updated = original.Clone();
    updated.ManualStatus = "待复测";
    updated.Rating = 5;
    updated.Notes = "edited without loss";
    var final = new[] { updated, added };
    await session.SaveAsync(final);
    // A no-op save must not duplicate backup work or rewrite the DB.
    var beforeBackups = Directory.EnumerateFiles(paths.SqliteBackupsDirectory, "library-*.db").Count();
    await session.SaveAsync(final);
    var afterBackups = Directory.EnumerateFiles(paths.SqliteBackupsDirectory, "library-*.db").Count();
    Equal(beforeBackups, afterBackups, "no-op save incorrectly created a database backup");

    var reopened = await ProductionManagerSqliteLibrarySession.OpenAsync(paths);
    var rows = reopened.Items;
    Equal(2, rows.Count, "SQLite delta count");
    True(!rows.Any(x => x.Path.Equals(removable.Path, StringComparison.OrdinalIgnoreCase)),
        "deleted row survived delta");
    var preserved = rows.Single(x => x.Path.Equals(original.Path, StringComparison.OrdinalIgnoreCase));
    Equal("待复测", preserved.ManualStatus, "delta manual status");
    Equal(5, preserved.Rating, "delta rating");
    Equal("edited without loss", preserved.Notes, "delta notes");
    Equal(ArtifactSnapshotFingerprint.Compute(final), ArtifactSnapshotFingerprint.Compute(rows),
        "full snapshot differs from delta output");
    var json = await new ReadOnlyLibraryJsonReader().ReadAsync(paths.RecoveryExportFile);
    Equal(ArtifactSnapshotFingerprint.Compute(final), json.SnapshotFingerprint, "delta recovery JSON mismatch");
    True(beforeBackups >= 2, "rolling backups were lost");
}

static async Task TestProductionAllSourcesCorruptCreatesWritableNewDatabaseAsync()
{
    using var env = TestEnvironment.Create();
    var paths = new ProductionStoragePaths(env.ProductionRoot, env.FreeCamRoot);
    Directory.CreateDirectory(paths.DataDirectory);
    Directory.CreateDirectory(paths.SqliteBackupsDirectory);
    Directory.CreateDirectory(paths.RecoveryDirectory);

    await File.WriteAllBytesAsync(paths.DatabaseFile, new byte[8192]);
    await File.WriteAllBytesAsync(Path.Combine(paths.SqliteBackupsDirectory, "library-corrupt.db"), new byte[4096]);
    await File.WriteAllTextAsync(paths.RecoveryExportFile, "{not-json", new UTF8Encoding(false));
    await File.WriteAllBytesAsync(paths.LegacyLibraryFile, new byte[2048]);
    await File.WriteAllBytesAsync(paths.LegacyBackupFile, new byte[2048]);

    var session = await ProductionManagerSqliteLibrarySession.OpenAsync(paths);
    Equal("NewDatabase", session.RecoverySource, "all-corrupt startup recovery source");
    Equal(0, session.Items.Count, "all-corrupt startup should create an empty writable index");
    True(File.Exists(paths.DatabaseFile), "new SQLite database missing after all-corrupt recovery");

    var changed = new[] { SampleArtifact("recovered-write.zip", "待测试", 4, "after-recovery") };
    await session.SaveAsync(changed);
    var reopened = await ProductionManagerSqliteLibrarySession.OpenAsync(paths);
    Equal(1, reopened.Items.Count, "new database was not writable after all-corrupt recovery");
    Equal("after-recovery", reopened.Items.Single().Notes, "recovered database did not persist metadata");
}

static async Task TestProductionRollbackAsync()
{
    using var env = TestEnvironment.Create();
    var old = SampleArtifact("rollback.zip", "待测试", 1, "legacy-old");
    WriteLibrary(env.Primary, [old]);
    var paths = new ProductionStoragePaths(env.ProductionRoot, env.FreeCamRoot);
    var session = await ProductionManagerSqliteLibrarySession.OpenAsync(paths);
    var changed = session.Items.Select(x => x.Clone()).ToList();
    changed[0].ManualStatus = "通过";
    changed[0].Rating = 5;
    changed[0].Notes = "sqlite-latest";
    await session.SaveAsync(changed);

    var rollback = await new LegacyJsonRollbackService().RestoreLegacyJsonAsync(paths);
    True(File.Exists(rollback.LegacyLibraryFile), "rollback legacy library missing");
    True(!string.IsNullOrWhiteSpace(rollback.BackupFile) && File.Exists(rollback.BackupFile), "rollback previous legacy backup missing");
    var parsed = await new ReadOnlyLibraryJsonReader().ReadAsync(paths.LegacyLibraryFile);
    Equal(ArtifactSnapshotFingerprint.Compute(changed), parsed.SnapshotFingerprint, "rollback snapshot fingerprint");
    Equal("sqlite-latest", parsed.Items.Single().Notes, "rollback lost latest SQLite metadata");
    True(!File.Exists(paths.DatabaseFile), "rollback left active library.db behind");
    True(!File.Exists(paths.RecoveryExportFile), "rollback left active recovery export behind");
    True(!Directory.Exists(paths.SqliteBackupsDirectory), "rollback left stale SQLite backups active");
    True(Directory.Exists(rollback.SqliteArchiveDirectory), "rollback SQLite archive missing");

    // Simulate continuing work in V3.7 after rollback, then returning to RC1.
    var legacyAfterRollback = parsed.Items.Select(x => x.Clone()).ToList();
    legacyAfterRollback[0].Notes = "v37-after-rollback";
    WriteLibrary(paths.LegacyLibraryFile, legacyAfterRollback);
    var reopenedRc = await ProductionManagerSqliteLibrarySession.OpenAsync(paths);
    Equal("LegacyJson", reopenedRc.RecoverySource, "RC1 did not re-import newer V3.7 JSON after rollback");
    Equal("v37-after-rollback", reopenedRc.Items.Single().Notes, "RC1 resurrected stale pre-rollback SQLite state");
}

static void CreateFixtureZip(string path, string buildId, string feature, string stage)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    using var stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
    using var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create);
    var entry = archive.CreateEntry("BUILD_MANIFEST.json");
    using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
    var manifest = new
    {
        SchemaVersion = 3,
        Project = "FreeCam",
        version = "R40.4.0",
        buildName = $"R40.4.0_{feature}_{stage}",
        Base = "R40.4.0",
        Branch = "experiment/sqlite-test3",
        Feature = feature,
        BuildType = "Probe",
        ArtifactType = "Runtime",
        Stage = stage,
        BuildId = buildId,
        Commit = "LOCAL-TEST3",
        ReleaseState = "Experiment"
    };
    writer.Write(JsonSerializer.Serialize(manifest));
}

static Artifact SampleArtifact(string name, string manual, int rating, string notes) => new()
{
    Path = Path.Combine("D:\\FreeCam", "30_Experiment", name),
    RelativePath = Path.Combine("30_Experiment", name),
    Name = name,
    SchemaVersion = 3,
    Project = "FreeCam",
    Version = "R40.4.0",
    BuildName = Path.GetFileNameWithoutExtension(name),
    Base = "R40.4.0",
    Branch = "experiment/sqlite-migration",
    Feature = "SQLite迁移",
    BuildType = "Probe",
    ArtifactType = "Package",
    Stage = "Test1",
    BuildId = "BUILD-" + name,
    ParentBuildId = "PARENT",
    ForBuildId = "FOR",
    Commit = "LOCAL",
    SourceMode = "FullSource",
    SourceState = "Present",
    ReleaseState = "Experiment",
    BuildDate = "2026-09-14",
    ManifestFound = true,
    ManifestName = "BUILD_MANIFEST.json",
    Sha256 = new string('a', 64),
    Size = 123456,
    Category = "实验",
    Status = "已测试",
    TestStatus = "已测试",
    ManualStatus = manual,
    TestingPath = Path.Combine("D:\\FreeCam", "01_Testing", Path.GetFileNameWithoutExtension(name)),
    TestingRelativePath = Path.Combine("01_Testing", Path.GetFileNameWithoutExtension(name)),
    ResultPath = Path.Combine("D:\\FreeCam", "40_Result", Path.GetFileNameWithoutExtension(name) + "_Result.zip"),
    ResultRelativePath = Path.Combine("40_Result", Path.GetFileNameWithoutExtension(name) + "_Result.zip"),
    LastTestedAt = "2026-09-14T02:00:00+08:00",
    TestStartedAt = "2026-09-14T01:50:00+08:00",
    Tags = ["tagA", "tagB"],
    Notes = notes,
    Favorite = rating > 0,
    Rating = rating,
    Protected = true,
    PairedBuildId = "PAIR",
    ImportedAt = "2026-09-14T01:00:00+08:00",
    DuplicateOf = "",
    AutoDeleteAt = ""
};

static void WriteLibrary(string path, IEnumerable<Artifact> items)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    var json = JsonSerializer.Serialize(new { items = items.ToArray() }, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(path, json, new UTF8Encoding(false));
}

static string FileSha256(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
static void True(bool value, string message) { if (!value) throw new Exception(message); }
static void Equal<T>(T expected, T actual, string message) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"{message}: expected={expected}, actual={actual}"); }
static void SequenceEqual(byte[] expected, byte[] actual, string message) { if (!expected.AsSpan().SequenceEqual(actual)) throw new Exception(message); }
static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception($"Expected {typeof(T).Name}"); }
static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new Exception($"Expected {typeof(T).Name}"); }

sealed class TestEnvironment : IDisposable
{
    public string Root { get; }
    public string ProductionRoot => Path.Combine(Root, "ProductionData");
    public string ExperimentRoot => Path.Combine(Root, "ExperimentData");
    public string FreeCamRoot => Path.Combine(Root, "FreeCam");
    public string Primary => Path.Combine(ProductionRoot, "library.json");
    public string Backup => Primary + ".bak";
    private TestEnvironment(string root) => Root = root;
    public static TestEnvironment Create()
    {
        var root = Path.Combine(Path.GetTempPath(), "FreeCamSQLiteMigrationTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "ProductionData"));
        Directory.CreateDirectory(Path.Combine(root, "ExperimentData"));
        Directory.CreateDirectory(Path.Combine(root, "FreeCam"));
        return new TestEnvironment(root);
    }
    public void Dispose() { try { Directory.Delete(Root, true); } catch { } }
}
