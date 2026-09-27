from pathlib import Path
import json, sys
root = Path(__file__).resolve().parent
app = (root/'FreeCamManager'/'App.xaml.cs').read_text(encoding='utf-8-sig')
proj = (root/'FreeCamManager'/'FreeCamManager.csproj').read_text(encoding='utf-8-sig')
manifest = json.loads((root/'BUILD_MANIFEST.json').read_text(encoding='utf-8'))
checks = [
    ('official exe name', '<AssemblyName>FreeCam_Manager</AssemblyName>' in proj),
    ('version 3.8.0', '<Version>3.8.0</Version>' in proj),
    ('sqlite startup', 'ProductionManagerSqliteLibrarySession.OpenAsync' in app),
    ('official v3.8 label', 'V3.8：SQLite 主库已启用' in app),
    ('manager updater enabled', '_managerUpdater = new ManagerUpdateService' in app),
    ('background update loop', 'ManagerUpdateLoopAsync' in app),
    ('path collision repair', 'MergePathCollision' in (root/'FreeCamManager.Core'/'Services'/'LibraryService.cs').read_text(encoding='utf-8')),
    ('stable manifest', manifest.get('Version') == 'V3.8' and manifest.get('ReleaseState') == 'Stable'),
]
bad = [name for name, ok in checks if not ok]
for name, ok in checks:
    print(('PASS' if ok else 'FAIL') + ': ' + name)
if bad:
    sys.exit(1)
print(f'V3.8 FINAL CONTRACT: {len(checks)}/{len(checks)} passed')
