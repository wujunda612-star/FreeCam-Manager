from __future__ import annotations

import argparse
import shutil
from pathlib import Path


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{label}: expected exactly one anchor, found {count}")
    return text.replace(old, new, 1)


def add_tests(source_root: Path) -> None:
    root = source_root / "src-wpf"
    program_path = root / "FreeCamManager.Tests" / "Program.cs"
    program = program_path.read_text(encoding="utf-8")

    reg_anchor = '        await Run("V3.7 tested status click opens Result location without breaking drag", V37TestedStatusClickContract);\n'
    registration = reg_anchor + '        await Run("V3.7 Fix1 background refresh preserves manual conclusion interaction", V371BackgroundRefreshPreservesManualConclusionInteraction);\n'
    program = replace_once(program, reg_anchor, registration, "V3.7 Fix1 test registration")

    method = r'''
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

'''
    program = replace_once(program, "    private static string TempDir()\n", method + "    private static string TempDir()\n", "TempDir method")
    program_path.write_text(program, encoding="utf-8")

    validator = r'''from pathlib import Path
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
'''
    (root / "validate-v371.py").write_text(validator, encoding="utf-8")


def apply_production(source_root: Path) -> None:
    root = source_root / "src-wpf"

    csproj_path = root / "FreeCamManager" / "FreeCamManager.csproj"
    csproj = csproj_path.read_text(encoding="utf-8")
    csproj = replace_once(csproj, "<Version>3.7.0</Version>", "<Version>3.7.1</Version>", "project version")
    csproj_path.write_text(csproj, encoding="utf-8")

    dev_path = root / "FreeCamManager" / "ViewModels" / "DevelopmentViewModel.cs"
    dev = dev_path.read_text(encoding="utf-8")
    old_refresh = '''        Items.Clear();
        foreach (var a in query) Items.Add(CreateRow(a));
        SelectedRow = Items.FirstOrDefault(x => string.Equals(x.Path, selectedPath, StringComparison.OrdinalIgnoreCase));
        RaiseCounts();
'''
    new_refresh = '''        // Reconcile rows in place. Clearing the collection destroys the active
        // CompactPickerControl, so a background scan used to close/reset an open
        // manual-conclusion picker while the user was choosing a value.
        var desired = query.ToList();
        for (var i = 0; i < desired.Count; i++)
        {
            var artifact = desired[i];
            var existing = Items.FirstOrDefault(x => string.Equals(x.Path, artifact.Path, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                Items.Insert(i, CreateRow(artifact));
                continue;
            }

            existing.RefreshFromArtifact(artifact);
            var currentIndex = Items.IndexOf(existing);
            if (currentIndex != i) Items.Move(currentIndex, i);
        }
        while (Items.Count > desired.Count) Items.RemoveAt(Items.Count - 1);

        SelectedRow = Items.FirstOrDefault(x => string.Equals(x.Path, selectedPath, StringComparison.OrdinalIgnoreCase));
        RaiseCounts();
'''
    dev = replace_once(dev, old_refresh, new_refresh, "Development in-place refresh")
    dev_path.write_text(dev, encoding="utf-8")

    row_path = root / "FreeCamManager" / "ViewModels" / "ArtifactRowViewModel.cs"
    row = row_path.read_text(encoding="utf-8")
    anchor = '''    public string GetDraggableResultPath()
    {
'''
    method = '''    public void RefreshFromArtifact(Artifact artifact)
    {
        if (!string.Equals(artifact.Path, Path, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Cannot refresh a row from a different artifact path.", nameof(artifact));

        var manualStatus = string.IsNullOrWhiteSpace(artifact.ManualStatus) ? "未标记" : artifact.ManualStatus;
        var rating = Math.Clamp(artifact.Rating, 0, 5);
        var isProtected = artifact.Protected;
        _artifact = artifact.Clone();

        if (!string.Equals(_manualStatus, manualStatus, StringComparison.Ordinal))
        {
            _manualStatus = manualStatus;
            OnPropertyChanged(nameof(ManualStatus));
        }
        if (_rating != rating)
        {
            _rating = rating;
            OnPropertyChanged(nameof(Rating));
        }
        if (_isProtected != isProtected)
        {
            _isProtected = isProtected;
            OnPropertyChanged(nameof(IsProtected));
        }

        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(Base));
        OnPropertyChanged(nameof(Feature));
        OnPropertyChanged(nameof(Stage));
        OnPropertyChanged(nameof(DisplayFeature));
        OnPropertyChanged(nameof(DisplayStage));
        OnPropertyChanged(nameof(CategoryLabel));
        OnPropertyChanged(nameof(TestStatus));
        OnPropertyChanged(nameof(Notes));
        OnPropertyChanged(nameof(FileNameToolTip));
        OnPropertyChanged(nameof(FeatureToolTip));
        OnPropertyChanged(nameof(StageToolTip));
        OnPropertyChanged(nameof(NotesToolTip));
        OnPropertyChanged(nameof(IsExtracted));
        OnPropertyChanged(nameof(BuildId));
        OnPropertyChanged(nameof(Commit));
        OnPropertyChanged(nameof(TestingPath));
        OnPropertyChanged(nameof(ResultPath));
        OnPropertyChanged(nameof(HasResult));
        OnPropertyChanged(nameof(ResultHint));
        OnPropertyChanged(nameof(TimeDisplay));
        OnPropertyChanged(nameof(IsDuplicate));
        OnPropertyChanged(nameof(IsStable));
    }

'''
    row = replace_once(row, anchor, method + anchor, "row in-place refresh method")
    row_path.write_text(row, encoding="utf-8")

    v37_validator_path = root / "validate-v37.py"
    v37_validator = v37_validator_path.read_text(encoding="utf-8")
    old_check = 'need("<Version>3.7.0</Version>" in csproj, "app version must be 3.7.0")'
    new_check = 'need("<Version>3.7.0</Version>" in csproj or "<Version>3.7.1</Version>" in csproj, "app version must be >= 3.7.0")'
    v37_validator = replace_once(v37_validator, old_check, new_check, "V3.7 forward-compatible version check")
    v37_validator_path.write_text(v37_validator, encoding="utf-8")


def clean(source_root: Path) -> None:
    for name in ["bin", "obj", "BuildOutput"]:
        for p in source_root.rglob(name):
            if p.is_dir():
                shutil.rmtree(p, ignore_errors=True)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("command", choices=["add-tests", "apply-production", "clean"])
    parser.add_argument("--source-root", required=True, type=Path)
    args = parser.parse_args()
    if args.command == "add-tests":
        add_tests(args.source_root)
    elif args.command == "apply-production":
        apply_production(args.source_root)
    else:
        clean(args.source_root)


if __name__ == "__main__":
    main()
