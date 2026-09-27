from pathlib import Path
from zipfile import ZipFile
import json
import sys

pkg = Path(sys.argv[1])
with ZipFile(pkg) as z:
    read=lambda p:z.read(p).decode("utf-8-sig")
    service=read("src-wpf/FreeCamManager.Core/Services/TestResultService.cs")
    tests=read("src-wpf/FreeCamManager.Tests/Program.cs")
    proj=read("src-wpf/FreeCamManager/FreeCamManager.csproj")
    manifest=json.loads(read("BUILD_MANIFEST.json"))

errors=[]
def require(ok,message):
    if not ok: errors.append(message)

require("<Version>3.9.7</Version>" in proj,"Version is not 3.9.7")
require(manifest.get("Version")=="V3.9.7","Manifest is not V3.9.7")
require("NormalizePhaseTokens(actualStem)" in service and "NormalizePhaseTokens(expectedStem)" in service,
        "Phase3-tolerant result basename matching is missing")
require("HasConflictingForBuildId(path, build.BuildId)" in service,
        "Explicit conflicting ForBuildId must reject mismatched current results")
require('Regex.Replace(stem, @" \\(\\d+\\)$"' in service,
        "Numbered ZIP filename normalization is malformed")
require(service.find('if (IsPreferredResultZip(stored) && !IsExcludedEvidencePath(stored!, testingPath)) return stored;')
        < service.find('var currentRaw = FindCurrentWorkspaceRawEvidence(testingPath, build);'),
        "Linked ZIP must be tested before raw log fallback")
require("V397Phase3IndexResultZipBeatsLog" in tests and "V397StoredResultZipBeatsLog" in tests,
        "Test suite must include WW index Result ZIP regression")
if errors:
    print("V3.9.7 contract FAIL:")
    for item in errors: print(" -",item)
    raise SystemExit(1)
print("V3.9.7 contract PASS")
