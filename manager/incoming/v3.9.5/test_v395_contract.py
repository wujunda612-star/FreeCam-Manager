from pathlib import Path
import json
import sys
import zipfile

package = Path(sys.argv[1])
with zipfile.ZipFile(package) as zf:
    def read(name: str) -> str:
        return zf.read(name).decode('utf-8-sig')

    classification = read('src-wpf/FreeCamManager.Core/Services/ClassificationService.cs')
    organizer = read('src-wpf/FreeCamManager.Core/Services/OrganizerService.cs')
    watcher = read('src-wpf/FreeCamManager.Core/Services/InboxWatcherService.cs')
    tests = read('src-wpf/FreeCamManager.Tests/Program.cs')
    project = read('src-wpf/FreeCamManager/FreeCamManager.csproj')
    manifest = json.loads(read('BUILD_MANIFEST.json'))

assert '<Version>3.9.5</Version>' in project
assert manifest.get('Version') == 'V3.9.5'
assert manifest.get('Base') == 'FreeCam_Manager_V3.9.4'

assert 'return new("Unknown", "");' in classification
assert 'return new("Unknown", "90_Unknown");' not in classification
assert 'Path.Combine("90_Unknown", "Stable_Candidate", version)' in classification

unknown_guard = organizer.index('if (decision.Category == "Unknown")')
duplicate_lookup = organizer.index('var existing = library.ByHash(a.Sha256);')
assert unknown_guard < duplicate_lookup, 'unknown must be rejected before duplicate routing/hash archive behavior'
assert 'a.Status = "未识别，保留在收件箱";' in organizer
assert 'return a;' in organizer[unknown_guard:duplicate_lookup]

zip_guard = watcher.index('if (decision.Category == "Unknown")')
extract_call = watcher.index('_extraction.ExtractToTestingAsync')
assert zip_guard < extract_call, 'unknown ZIP must be skipped before auto extraction'
assert 'if (processedArtifact.Category == "Unknown")' in watcher
assert 'SCAN_UNRECOGNIZED_KEEP_INBOX' in watcher
assert 'MarkUnrecognized(path);' in watcher
assert 'if (previous.Unrecognized) return false;' in watcher

assert 'V3.9.5 unrecognized files stay in inbox' in tests
assert 'V395UnrecognizedFilesStayInInbox' in tests
assert 'unrecognized inbox file must stay in inbox' in tests
assert 'unrecognized zip must not be auto-extracted' in tests
assert 'OrganizerService must not move an unrecognized file' in tests

print('V3.9.5 unrecognized-inbox contract PASS')
