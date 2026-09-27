from pathlib import Path
import sys

root = Path(__file__).resolve().parent
app = root / "FreeCamManager"
errors = []

def need(cond, msg):
    if not cond:
        errors.append(msg)

csproj = (app / "FreeCamManager.csproj").read_text(encoding="utf-8")
development = (app / "ViewModels" / "DevelopmentViewModel.cs").read_text(encoding="utf-8")
row = (app / "ViewModels" / "ArtifactRowViewModel.cs").read_text(encoding="utf-8")
tests = (root / "FreeCamManager.Tests" / "Program.cs").read_text(encoding="utf-8")

need("<Version>3.7.1</Version>" in csproj, "app version must be 3.7.1")
need("RefreshFromArtifact" in development and "Items.Move(" in development,
     "Development refresh must reuse/reorder existing row view models")
need("Items.Clear();" not in development,
     "Development refresh must not clear the whole collection during background refresh")
need("public void RefreshFromArtifact(Artifact artifact)" in row,
     "Artifact row in-place refresh method is missing")
need("V3.7 Fix1 background refresh preserves manual conclusion interaction" in tests,
     "V3.7 Fix1 regression test must be registered")

if errors:
    print("FAIL: V3.7 Fix1 contract")
    for e in errors:
        print(" -", e)
    sys.exit(1)
print("PASS: V3.7 Fix1 contract")
