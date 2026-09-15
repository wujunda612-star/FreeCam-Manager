from pathlib import Path
import sys, zipfile

package = Path(sys.argv[1]) if len(sys.argv) > 1 else Path('manager/packages/FreeCam_Manager_V3.9.3_Source.zip')
if not package.exists():
    raise SystemExit(f'package not found: {package}')

with zipfile.ZipFile(package) as zf:
    names = set(zf.namelist())
    app = zf.read('src-wpf/FreeCamManager/App.xaml.cs').decode('utf-8-sig')
    csproj = zf.read('src-wpf/FreeCamManager/FreeCamManager.csproj').decode('utf-8-sig')
    profiler_name = 'src-wpf/FreeCamManager/Services/StartupTimingRecorder.cs'
    profiler = zf.read(profiler_name).decode('utf-8-sig') if profiler_name in names else ''

errors = []

if '<Version>3.9.4</Version>' not in csproj:
    errors.append('project version is not 3.9.4')
if profiler_name not in names:
    errors.append('StartupTimingRecorder.cs is missing')

for required in [
    'Stopwatch',
    'STARTUP_TIMING.log',
    'public void Mark(',
    'public void Flush(',
    'elapsed_ms',
]:
    if required not in profiler:
        errors.append(f'profiler missing contract token: {required}')

for mark in [
    'ON_STARTUP_ENTER',
    'SETTINGS_READY',
    'LOG_READY',
    'SQLITE_READY',
    'TERMS_READY',
    'PATH_REBASE_DONE',
    'LIBRARY_REBUILD_DONE',
    'STABLE_REPAIR_DONE',
    'DISCARD_CLEANUP_DONE',
    'VIEWMODEL_READY',
    'REFRESH_ALL_DONE',
    'WINDOW_SHOW_CALL',
    'WINDOW_SHOW_RETURN',
    'FIRST_RENDER',
    'APP_READY',
]:
    if f'Mark("{mark}"' not in app:
        errors.append(f'App startup mark missing: {mark}')

if 'ContentRendered +=' not in app:
    errors.append('FIRST_RENDER must be captured from Window.ContentRendered')
if 'startupTiming.Flush(' not in app:
    errors.append('startup timing log is never flushed')
if 'STARTUP_FATAL' in app and 'startupTiming.Mark("STARTUP_FATAL"' not in app:
    errors.append('fatal startup path must record STARTUP_FATAL timing')

if errors:
    print('V3.9.4 startup diagnostics contract FAILED:')
    for e in errors:
        print(' -', e)
    raise SystemExit(1)

print('V3.9.4 startup diagnostics contract PASS')
