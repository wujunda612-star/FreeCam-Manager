from __future__ import annotations

import argparse
import re
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

    reg_anchor = '        await Run("V3.6 single-instance startup contract", V36SingleInstanceStartupContract);\n'
    registrations = reg_anchor + (
        '        await Run("V3.7 matched Result ZIP beats raw log", V37MatchedResultZipBeatsRawLog);\n'
        '        await Run("V3.7 tested raw log upgrades when Result ZIP appears", V37RefreshUpgradesRawLogToResultZip);\n'
        '        await Run("V3.7 tested status click opens Result location without breaking drag", V37TestedStatusClickContract);\n'
    )
    program = replace_once(program, reg_anchor, registrations, "V3.7 test registration")

    methods = r'''
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

'''
    program = replace_once(program, "    private static string TempDir()\n", methods + "    private static string TempDir()\n", "TempDir method")
    program_path.write_text(program, encoding="utf-8")

    validator = r'''from pathlib import Path
import sys

root = Path(__file__).resolve().parent
app = root / "FreeCamManager"
core = root / "FreeCamManager.Core"
tests = root / "FreeCamManager.Tests" / "Program.cs"
errors = []

def need(cond, msg):
    if not cond:
        errors.append(msg)

csproj = (app / "FreeCamManager.csproj").read_text(encoding="utf-8")
result_service = (core / "Services" / "TestResultService.cs").read_text(encoding="utf-8")
refresh_service = (core / "Services" / "TestStatusRefreshService.cs").read_text(encoding="utf-8")
row = (app / "ViewModels" / "ArtifactRowViewModel.cs").read_text(encoding="utf-8")
view = (app / "Views" / "DevelopmentView.xaml").read_text(encoding="utf-8")
code = (app / "Views" / "DevelopmentView.xaml.cs").read_text(encoding="utf-8")
testsrc = tests.read_text(encoding="utf-8")

need("<Version>3.7.0</Version>" in csproj, "app version must be 3.7.0")
expected_pos = result_service.find("var expected = FindExpectedResultZip(resultRoot, expectedName);")
raw_pos = result_service.find("var currentRaw = FindCurrentWorkspaceRawEvidence(testingPath, build);")
need(expected_pos >= 0 and raw_pos >= 0 and expected_pos < raw_pos,
     "matched 40_Result ZIP must be resolved before current raw log")
need('TestResultService.IsPreferredResultZip(item.ResultPath)' in refresh_service,
     "tested raw evidence must remain eligible for refresh/upgrade")
need('ResolvePreferredDragPath(current, ResolveTestingPath(), _resultRoot())' in row,
     "Result open/drag path must be re-resolved against current files")
need('PreviewMouseLeftButtonUp="ResultBadge_PreviewMouseLeftButtonUp"' in view,
     "tested status badge click handler is missing")
need("_resultDragStarted" in code and "ResultBadge_PreviewMouseLeftButtonUp" in code
     and "OpenResultCommand.CanExecute" in code and "OpenResultCommand.Execute" in code,
     "tested status click/drag separation contract is missing")
need("V3.7 matched Result ZIP beats raw log" in testsrc, "V3.7 Result priority regression must be registered")
need("V3.7 tested raw log upgrades when Result ZIP appears" in testsrc, "V3.7 refresh upgrade regression must be registered")
need("V3.7 tested status click opens Result location without breaking drag" in testsrc, "V3.7 click regression must be registered")

if errors:
    print("FAIL: V3.7 contract")
    for e in errors:
        print(" -", e)
    sys.exit(1)
print("PASS: V3.7 contract")
'''
    (root / "validate-v37.py").write_text(validator, encoding="utf-8")


def apply_production(source_root: Path) -> None:
    root = source_root / "src-wpf"

    csproj_path = root / "FreeCamManager" / "FreeCamManager.csproj"
    csproj = csproj_path.read_text(encoding="utf-8")
    csproj = replace_once(csproj, "<Version>3.6.0</Version>", "<Version>3.7.0</Version>", "project version")
    csproj_path.write_text(csproj, encoding="utf-8")

    result_path = root / "FreeCamManager.Core" / "Services" / "TestResultService.cs"
    result = result_path.read_text(encoding="utf-8")
    old_order = '''        var currentRaw = FindCurrentWorkspaceRawEvidence(testingPath, build);
        if (!string.IsNullOrWhiteSpace(currentRaw)) return currentRaw;

        var expected = FindExpectedResultZip(resultRoot, expectedName);
        if (!string.IsNullOrWhiteSpace(expected)) return expected;
'''
    new_order = '''        // A completed matching Result ZIP is the canonical deliverable. It must
        // upgrade/beat a raw log that may have been discovered a moment earlier.
        var expected = FindExpectedResultZip(resultRoot, expectedName);
        if (!string.IsNullOrWhiteSpace(expected)) return expected;

        var currentRaw = FindCurrentWorkspaceRawEvidence(testingPath, build);
        if (!string.IsNullOrWhiteSpace(currentRaw)) return currentRaw;
'''
    result = replace_once(result, old_order, new_order, "Result ZIP priority")
    result_path.write_text(result, encoding="utf-8")

    refresh_path = root / "FreeCamManager.Core" / "Services" / "TestStatusRefreshService.cs"
    refresh = refresh_path.read_text(encoding="utf-8")
    old_skip = '''            if (item.TestStatus == "已测试" && !string.IsNullOrWhiteSpace(item.ResultPath) && File.Exists(item.ResultPath) &&
                !TestResultService.IsReferenceEvidencePath(item.ResultPath, item.TestingPath)) continue;'''
    new_skip = '''            // A raw log means the test is complete, but it is not necessarily the
            // final deliverable. Keep rescanning until a preferred Result ZIP appears.
            if (item.TestStatus == "已测试" && TestResultService.IsPreferredResultZip(item.ResultPath) &&
                !TestResultService.IsReferenceEvidencePath(item.ResultPath, item.TestingPath)) continue;'''
    refresh = replace_once(refresh, old_skip, new_skip, "tested evidence refresh gate")
    refresh_path.write_text(refresh, encoding="utf-8")

    row_path = root / "FreeCamManager" / "ViewModels" / "ArtifactRowViewModel.cs"
    row = row_path.read_text(encoding="utf-8")
    old_resolve = '''    private async Task<string> ResolveResultPathAsync()
    {
        var existing = ExistingResultPath();
        if (!string.IsNullOrWhiteSpace(existing)) return existing;
        var testing = ResolveTestingPath();
        if (string.IsNullOrWhiteSpace(testing) || !Directory.Exists(testing)) return "";
        var current = _library.ByPath(Path) ?? _artifact;
        var evidence = await _results.FindEvidenceAsync(testing, current);
        if (evidence is null) return "";
        _library.SetTestEvidence(Path, evidence.Path, _root(), evidence.LastWriteTimeUtc.ToString("O"));
        await _library.SaveAsync();
        _artifact.ResultPath = evidence.Path;
        _artifact.ResultRelativePath = PathRebaseService.TryMakeRelative(_root(), evidence.Path);
        _artifact.TestStatus = "已测试";
        OnPropertyChanged(nameof(ResultPath)); OnPropertyChanged(nameof(HasResult)); OnPropertyChanged(nameof(ResultHint)); OnPropertyChanged(nameof(TestStatus));
        return evidence.Path;
    }
'''
    new_resolve = '''    private async Task<string> ResolveResultPathAsync()
    {
        var testing = ResolveTestingPath();
        var current = _library.ByPath(Path) ?? _artifact;

        // Re-resolve at click time as well as drag time so a Result ZIP that was
        // created after the first log automatically supersedes the cached log.
        var preferred = _results.ResolvePreferredDragPath(current, testing, _resultRoot());
        if (!string.IsNullOrWhiteSpace(preferred) && File.Exists(preferred))
        {
            if (!string.Equals(current.ResultPath, preferred, StringComparison.OrdinalIgnoreCase))
            {
                var testedAt = File.GetLastWriteTimeUtc(preferred).ToString("O");
                _library.SetTestEvidence(Path, preferred, _root(), testedAt);
                await _library.SaveAsync();
                _artifact.ResultPath = preferred;
                _artifact.ResultRelativePath = PathRebaseService.TryMakeRelative(_root(), preferred);
                _artifact.TestStatus = "已测试";
                OnPropertyChanged(nameof(ResultPath)); OnPropertyChanged(nameof(HasResult)); OnPropertyChanged(nameof(ResultHint)); OnPropertyChanged(nameof(TestStatus));
            }
            return preferred;
        }

        var existing = ExistingResultPath();
        if (!string.IsNullOrWhiteSpace(existing)) return existing;
        if (string.IsNullOrWhiteSpace(testing) || !Directory.Exists(testing)) return "";
        var evidence = await _results.FindEvidenceAsync(testing, current);
        if (evidence is null) return "";
        _library.SetTestEvidence(Path, evidence.Path, _root(), evidence.LastWriteTimeUtc.ToString("O"));
        await _library.SaveAsync();
        _artifact.ResultPath = evidence.Path;
        _artifact.ResultRelativePath = PathRebaseService.TryMakeRelative(_root(), evidence.Path);
        _artifact.TestStatus = "已测试";
        OnPropertyChanged(nameof(ResultPath)); OnPropertyChanged(nameof(HasResult)); OnPropertyChanged(nameof(ResultHint)); OnPropertyChanged(nameof(TestStatus));
        return evidence.Path;
    }
'''
    row = replace_once(row, old_resolve, new_resolve, "click-time Result resolver")
    row_path.write_text(row, encoding="utf-8")

    view_path = root / "FreeCamManager" / "Views" / "DevelopmentView.xaml"
    view = view_path.read_text(encoding="utf-8")
    old_badge = '''                                <controls:StatusBadge Grid.Column="4" Text="{Binding TestStatus}" VerticalAlignment="Center" ToolTip="{Binding ResultHint}"
                                                      PreviewMouseLeftButtonDown="ResultBadge_PreviewMouseLeftButtonDown" PreviewMouseMove="ResultBadge_PreviewMouseMove"/>'''
    new_badge = '''                                <controls:StatusBadge Grid.Column="4" Text="{Binding TestStatus}" VerticalAlignment="Center" ToolTip="{Binding ResultHint}"
                                                      PreviewMouseLeftButtonDown="ResultBadge_PreviewMouseLeftButtonDown" PreviewMouseMove="ResultBadge_PreviewMouseMove"
                                                      PreviewMouseLeftButtonUp="ResultBadge_PreviewMouseLeftButtonUp"/>'''
    view = replace_once(view, old_badge, new_badge, "status badge click handler")
    view_path.write_text(view, encoding="utf-8")

    code_path = root / "FreeCamManager" / "Views" / "DevelopmentView.xaml.cs"
    code = code_path.read_text(encoding="utf-8")
    old_fields = '''    private Point _dragStart;
    private ArtifactRowViewModel? _dragRow;
'''
    new_fields = '''    private Point _dragStart;
    private ArtifactRowViewModel? _dragRow;
    private bool _resultDragStarted;
'''
    code = replace_once(code, old_fields, new_fields, "drag state field")

    old_down = '''    private void ResultBadge_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(this);
        _dragRow = (sender as FrameworkElement)?.DataContext as ArtifactRowViewModel;
    }
'''
    new_down = '''    private void ResultBadge_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(this);
        _dragRow = (sender as FrameworkElement)?.DataContext as ArtifactRowViewModel;
        _resultDragStarted = false;
    }
'''
    code = replace_once(code, old_down, new_down, "drag mouse down")

    old_drag_line = '''        var path = _dragRow.GetDraggableResultPath();
        _dragRow = null;
'''
    new_drag_line = '''        var path = _dragRow.GetDraggableResultPath();
        _resultDragStarted = true;
        _dragRow = null;
'''
    code = replace_once(code, old_drag_line, new_drag_line, "drag started marker")

    up_method = r'''
    private void ResultBadge_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var row = (sender as FrameworkElement)?.DataContext as ArtifactRowViewModel;
        _dragRow = null;
        if (_resultDragStarted)
        {
            _resultDragStarted = false;
            e.Handled = true;
            return;
        }

        if (row is null || !string.Equals(row.TestStatus, "已测试", StringComparison.Ordinal)) return;
        if (row.OpenResultCommand.CanExecute(null)) row.OpenResultCommand.Execute(null);
        e.Handled = true;
    }

'''
    code = replace_once(code, "    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject", up_method + "    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject", "status click method anchor")
    code_path.write_text(code, encoding="utf-8")

    v36_path = root / "validate-v36.py"
    v36 = v36_path.read_text(encoding="utf-8")
    if "import re\n" not in v36:
        v36 = v36.replace("from pathlib import Path\n", "from pathlib import Path\nimport re\n", 1)
    old_version_check = 'need("<Version>3.6.0</Version>" in csproj, "app version must be 3.6.0")'
    new_version_check = (
        "version_match = re.search(r'<Version>(\\d+)\\.(\\d+)\\.(\\d+)</Version>', csproj)\n"
        "need(version_match is not None and tuple(map(int, version_match.groups())) >= (3, 6, 0), 'app version must be >= 3.6.0')"
    )
    v36 = replace_once(v36, old_version_check, new_version_check, "validate-v36 version floor")
    v36_path.write_text(v36, encoding="utf-8")


def clean_source(source_root: Path) -> None:
    for p in sorted(source_root.rglob("*"), key=lambda x: len(x.parts), reverse=True):
        if p.is_dir() and p.name in {"bin", "obj", "BuildOutput", ".tools"}:
            shutil.rmtree(p, ignore_errors=True)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("mode", choices=["add-tests", "apply-production", "clean"])
    parser.add_argument("--source-root", required=True, type=Path)
    args = parser.parse_args()

    if args.mode == "add-tests":
        add_tests(args.source_root)
    elif args.mode == "apply-production":
        apply_production(args.source_root)
    else:
        clean_source(args.source_root)


if __name__ == "__main__":
    main()
