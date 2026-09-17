from pathlib import Path
import sys

root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path('.')
program = root / 'src-wpf/FreeCamManager.Tests/Program.cs'
text = program.read_text(encoding='utf-8-sig')

run_anchor = '        await Run("Inbox two-scan stability and manager classification", InboxWatcherStability);\n'
run_insert = run_anchor + '        await Run("V3.9.5 unrecognized files stay in inbox", V395UnrecognizedFilesStayInInbox);\n'
if text.count(run_anchor) != 1:
    raise SystemExit('run anchor not unique')
text = text.replace(run_anchor, run_insert, 1)

method_anchor = '\n\n    private static async Task V32ManualRefreshImmediateScan()\n'
method = r'''

    private static async Task V395UnrecognizedFilesStayInInbox()
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

        var unknown = Path.Combine(inbox, "random_unrecognized_payload.zip");
        using (var zip = ZipFile.Open(unknown, ZipArchiveMode.Create))
        {
            var start = zip.CreateEntry("Start.cmd");
            await using var writer = new StreamWriter(start.Open());
            await writer.WriteAsync("echo unknown");
        }
        File.SetLastWriteTimeUtc(unknown, DateTime.UtcNow.AddSeconds(-10));
        await watcher.ScanOnceAsync();
        var changed = await watcher.ScanOnceAsync();

        Assert(File.Exists(unknown), "V3.9.5 unrecognized inbox file must stay in inbox");
        Assert(!File.Exists(Path.Combine(root, "90_Unknown", Path.GetFileName(unknown))), "unrecognized file must not be moved to 90_Unknown");
        Assert(!Directory.Exists(Path.Combine(root, "01_Testing", "random_unrecognized_payload")), "unrecognized zip must not be auto-extracted");
        Assert(!library.Snapshot().Any(x => string.Equals(x.Name, Path.GetFileName(unknown), StringComparison.OrdinalIgnoreCase)), "unrecognized inbox file must not be indexed as a managed artifact");
        Assert(!changed, "leaving an unrecognized file in inbox is not a library change");

        var direct = Path.Combine(inbox, "another_unknown_payload.zip");
        using (var zip = ZipFile.Open(direct, ZipArchiveMode.Create))
        {
            var note = zip.CreateEntry("notes.txt");
            await using var writer = new StreamWriter(note.Open());
            await writer.WriteAsync("unknown");
        }
        var returned = await organizer.ProcessAsync(direct);
        Assert(File.Exists(direct), "OrganizerService must not move an unrecognized file");
        Assert(returned.Category == "Unknown" && string.Equals(returned.Path, direct, StringComparison.OrdinalIgnoreCase), "OrganizerService must return unrecognized artifact at original path");
        Assert(!library.Snapshot().Any(x => string.Equals(x.Name, Path.GetFileName(direct), StringComparison.OrdinalIgnoreCase)), "OrganizerService must not index an unrecognized file");

        var unknownDecision = classification.Plan(new Artifact { Name = "totally_unknown.bin", Path = Path.Combine(inbox, "totally_unknown.bin") });
        Assert(unknownDecision.Category == "Unknown" && string.IsNullOrEmpty(unknownDecision.RelativeDirectory), "Unknown classification must not route to 90_Unknown");
    }
'''
if text.count(method_anchor) != 1:
    raise SystemExit('method anchor not unique')
text = text.replace(method_anchor, method + method_anchor, 1)
program.write_text(text, encoding='utf-8')
print('Applied V3.9.5 regression test only')
