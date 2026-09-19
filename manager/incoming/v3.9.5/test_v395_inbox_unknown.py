from pathlib import Path
import sys
import zipfile

pkg = Path(sys.argv[1])
if not pkg.exists():
    raise SystemExit(f'missing package: {pkg}')

with zipfile.ZipFile(pkg) as zf:
    def read(name):
        return zf.read(name).decode('utf-8-sig')

    organizer = read('src-wpf/FreeCamManager.Core/Services/OrganizerService.cs')
    watcher = read('src-wpf/FreeCamManager.Core/Services/InboxWatcherService.cs')
    csproj = read('src-wpf/FreeCamManager/FreeCamManager.csproj')
    manifest = read('BUILD_MANIFEST.json')

errors = []

if '<Version>3.9.5</Version>' not in csproj:
    errors.append('project version is not 3.9.5')
if '"Version": "V3.9.5"' not in manifest:
    errors.append('BUILD_MANIFEST version is not V3.9.5')

unknown_guard = 'if (decision.Category == "Unknown")'
if unknown_guard not in organizer:
    errors.append('OrganizerService has no Unknown keep-in-place guard')
else:
    guard_pos = organizer.index(unknown_guard)
    hash_pos = organizer.find('hash.FileSha256Async')
    move_pos = organizer.find('MoveUniqueAsync(path, Path.Combine(Root, decision.RelativeDirectory)')
    if hash_pos != -1 and guard_pos > hash_pos:
        errors.append('OrganizerService classifies Unknown only after hashing/duplicate routing')
    if move_pos != -1 and guard_pos > move_pos:
        errors.append('OrganizerService Unknown guard occurs after move routing')

required_organizer = [
    'a.Category = "Unknown";',
    'a.Status = "未识别，保留在收件箱";',
    'return a;'
]
for token in required_organizer:
    if token not in organizer:
        errors.append(f'OrganizerService missing: {token}')

required_watcher = [
    'SCAN_UNRECOGNIZED_KEEP_INBOX',
    '未识别，已保留在收件箱',
    'RememberUnrecognized',
    'IsRememberedUnrecognized'
]
for token in required_watcher:
    if token not in watcher:
        errors.append(f'InboxWatcherService missing: {token}')

watcher_guard = watcher.find('if (decision.Category == "Unknown")')
extract_pos = watcher.find('_extraction.ShouldExtract(inspect)')
process_pos = watcher.find('_organizer.ProcessAsync(path')
if watcher_guard == -1:
    errors.append('InboxWatcherService has no Unknown decision guard')
else:
    if extract_pos != -1 and watcher_guard > extract_pos:
        errors.append('Unknown ZIP may be extracted before watcher guard')
    if process_pos != -1 and watcher_guard > process_pos:
        errors.append('Unknown file may be organized before watcher guard')

if errors:
    print('V3.9.5 inbox-unknown contract FAIL')
    for e in errors:
        print(' -', e)
    raise SystemExit(1)

print('V3.9.5 inbox-unknown contract PASS')
