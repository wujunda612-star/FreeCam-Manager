from pathlib import Path
from zipfile import ZipFile
import json
import sys

package = Path(sys.argv[1])
with ZipFile(package) as zf:
    def read(path):
        return zf.read(path).decode('utf-8-sig')
    app = read('src-wpf/FreeCamManager/App.xaml.cs')
    csproj = read('src-wpf/FreeCamManager/FreeCamManager.csproj')
    manifest = json.loads(read('BUILD_MANIFEST.json'))
    timing = read('src-wpf/FreeCamManager/Services/StartupTimingRecorder.cs')
    version_service = read('src-wpf/FreeCamManager.Core/Services/AppVersionService.cs')
    regression = read('src-wpf/FreeCamManager.Tests/Program.cs')

errors = []
def check(ok, message):
    if not ok:
        errors.append(message)

check('<Version>3.9.6</Version>' in csproj, 'Assembly version must be 3.9.6')
check(manifest.get('Version') == 'V3.9.6', 'Manifest Version must be V3.9.6')
onstartup = app.split('protected override async void OnStartup', 1)[-1].split('private async Task RunStartupMaintenanceAsync', 1)[0]
show_idx = onstartup.find('window.Show();')
render_idx = onstartup.find('window.ContentRendered +=')
launch_idx = onstartup.find('_startupMaintenanceTask = Task.Run(')
check(show_idx >= 0 and render_idx >= 0 and render_idx < launch_idx < show_idx,
      'Maintenance must launch only inside first ContentRendered handler, not before Show')
check('if (maintenanceLaunched) return;' in onstartup,
      'ContentRendered must launch maintenance at most once')
check('maintenanceStarted.TrySetResult(true)' in onstartup,
      'Manual scan gate must be released only after maintenance is scheduled')
check('await maintenanceStarted.Task.WaitAsync(_shutdown.Token)' in onstartup
      and 'await _startupMaintenanceTask.ConfigureAwait(false)' in onstartup,
      'Manual scans must wait for startup reconciliation')
check('await maintenance.ConfigureAwait(false)' in onstartup
      and 'await watcher.StartAsync(_shutdown.Token)' in onstartup
      and onstartup.find('await maintenance.ConfigureAwait(false)') < onstartup.find('await watcher.StartAsync(_shutdown.Token)'),
      'Automatic inbox watcher must wait for startup reconciliation')
prefix = onstartup[:render_idx] if render_idx >= 0 else onstartup
check('await libraryRebuild.ReconcileAsync(' not in prefix
      and 'await new PathRebaseService().RepairLibraryPathsAsync(' not in prefix,
      'Blocking root repair/rebuild must not run before first window render')
check('private async Task RunStartupMaintenanceAsync(' in app
      and 'RepairLibraryPathsAsync(_library!' in app
      and 'libraryRebuild.ReconcileAsync(settings.RootDir, ct)' in app
      and 'organizer.RepairStableCandidateDirectoriesAsync(ct)' in app
      and 'discardCleanup.CleanupDueAsync(DateTimeOffset.Now, ct)' in app,
      'All four existing maintenance stages must still run in background')
check('_startupMaintenanceTask?.GetAwaiter().GetResult()' in app,
      'Exit must await cancellation/completion of background maintenance')
check('STARTUP_MAINTENANCE_DONE' in app
      and 'startupTiming.Flush("BACKGROUND_MAINTENANCE_DONE")' in app,
      'Background maintenance must log completion and flush timing diagnostics')
check('lock (_sync)' in timing and timing.find('lock (_sync)') < timing.find('_lastElapsedMs = elapsedMs'),
      'Timing recorder needs synchronized delta updates when used on a background thread')

check('return $"V{normalized.Major}.{normalized.Minor}.{normalized.Build}";' in version_service,
      'AppVersionService must display V3.x.x without Fix suffix')
check('V396FirstFrameStartupContract' in regression and 'V396ThreePartVersionDisplay' in regression,
      'The final source package must ship startup and numeric-version regression tests')

if errors:
    print('V3.9.6 startup contract FAIL:')
    for error in errors: print(' - ' + error)
    sys.exit(1)
print('V3.9.6 startup contract PASS')
