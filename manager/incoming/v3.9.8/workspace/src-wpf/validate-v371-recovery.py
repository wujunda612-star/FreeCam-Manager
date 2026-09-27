from pathlib import Path
import sys

root = Path(__file__).resolve().parent
app = root / "FreeCamManager"
core = root / "FreeCamManager.Core"
errors = []

def need(cond, msg):
    if not cond:
        errors.append(msg)

appsrc = (app / "App.xaml.cs").read_text(encoding="utf-8")
library = (core / "Services" / "LibraryService.cs").read_text(encoding="utf-8")
watcher = (core / "Services" / "InboxWatcherService.cs").read_text(encoding="utf-8")
rebuild_path = core / "Services" / "LibraryRebuildService.cs"
rebuild = rebuild_path.read_text(encoding="utf-8") if rebuild_path.exists() else ""
tests = (root / "FreeCamManager.Tests" / "Program.cs").read_text(encoding="utf-8")

need('file + ".bak"' in library or '_file + ".bak"' in library,
     "LibraryService must keep a .bak last-known-good copy")
need("flushToDisk: true" in library and "ValidateLibraryFileAsync" in library,
     "LibraryService safe-save validation/durable flush contract is missing")
need("CreateEmpty(string file)" in library,
     "LibraryService must support a writable empty library after corruption")
need(rebuild_path.exists() and "public sealed class LibraryRebuildService" in rebuild,
     "LibraryRebuildService is missing")
need('new[] { "10_Stable", "20_Feature", "30_Experiment", "40_Result", "80_Archive", "90_Unknown" }' in rebuild,
     "rebuild service must scan all managed classification roots")
need("System.Text.Json.JsonException" in appsrc and "LibraryService.CreateEmpty(AppPaths.LibraryFile)" in appsrc,
     "startup corrupt-library recovery contract is missing")
need("ReconcileAsync(settings.RootDir" in appsrc,
     "startup root reconciliation is missing")
need("manual && _rebuild is not null" in watcher and "_rebuild.ReconcileAsync(cfg.RootDir" in watcher,
     "manual refresh root reconciliation is missing")
for name in [
    "V3.7 Fix1 corrupted library restores from valid backup",
    "V3.7 Fix1 safe save keeps last-known-good backup",
    "V3.7 Fix1 root reconciliation rebuilds managed library without moving files",
    "V3.7 Fix1 root reconciliation preserves existing manual metadata",
    "V3.7 Fix1 startup and manual refresh wire recovery reconciliation",
]:
    need(name in tests, f"missing regression test registration: {name}")

if errors:
    print("FAIL: V3.7 Fix1 recovery contract")
    for e in errors:
        print(" -", e)
    sys.exit(1)
print("PASS: V3.7 Fix1 recovery contract")
