from pathlib import Path
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

need("<Version>3.7.0</Version>" in csproj or "<Version>3.7.1</Version>" in csproj, "app version must be >= 3.7.0")
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
