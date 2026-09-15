from pathlib import Path
import json
import sys

root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path('.')


def read(rel: str):
    p = root / rel
    return p, p.read_text(encoding='utf-8-sig')


def write(p: Path, text: str):
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_text(text, encoding='utf-8')


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise SystemExit(f'{label}: expected 1 occurrence, found {count}')
    return text.replace(old, new, 1)


# 1) Add an in-memory startup timing recorder. It does not touch disk until Flush,
# so pre-window timings are not distorted by per-stage file I/O.
profiler = r'''using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace FreeCamManager.Services;

public sealed class StartupTimingRecorder
{
    private sealed record Entry(string Name, double ElapsedMs, double DeltaMs, DateTimeOffset At, string Detail);

    private readonly Stopwatch _clock;
    private readonly string _version;
    private readonly string _sessionId = Guid.NewGuid().ToString("N")[..12];
    private readonly List<Entry> _entries = [];
    private readonly object _sync = new();
    private double _lastElapsedMs;
    private int _flushedCount;
    private bool _headerWritten;

    public StartupTimingRecorder(Stopwatch clock, string dataDirectory, string version)
    {
        _clock = clock;
        _version = version;
        FilePath = Path.Combine(dataDirectory, "Logs", "STARTUP_TIMING.log");
    }

    public string FilePath { get; }

    public void Mark(string name, params (string Key, object? Value)[] fields)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        var elapsedMs = _clock.Elapsed.TotalMilliseconds;
        var deltaMs = elapsedMs - _lastElapsedMs;
        _lastElapsedMs = elapsedMs;
        var detail = fields.Length == 0
            ? ""
            : string.Join(";", fields.Select(x => $"{x.Key}={Sanitize(x.Value)}"));
        lock (_sync)
            _entries.Add(new Entry(name.Trim(), elapsedMs, deltaMs, DateTimeOffset.Now, detail));
    }

    public void Flush(string reason)
    {
        try
        {
            List<Entry> pending;
            lock (_sync)
            {
                if (_flushedCount >= _entries.Count) return;
                pending = _entries.Skip(_flushedCount).ToList();
                _flushedCount = _entries.Count;
            }

            var parent = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);

            var sb = new StringBuilder();
            if (!_headerWritten)
            {
                sb.AppendLine();
                sb.AppendLine($"=== STARTUP_SESSION session={_sessionId} version={_version} started={DateTimeOffset.Now:O} ===");
                _headerWritten = true;
            }
            foreach (var entry in pending)
            {
                sb.Append(entry.At.ToString("O", CultureInfo.InvariantCulture));
                sb.Append(" | mark=").Append(entry.Name);
                sb.Append(" | elapsed_ms=").Append(entry.ElapsedMs.ToString("F1", CultureInfo.InvariantCulture));
                sb.Append(" | delta_ms=").Append(entry.DeltaMs.ToString("F1", CultureInfo.InvariantCulture));
                if (!string.IsNullOrWhiteSpace(entry.Detail)) sb.Append(" | ").Append(entry.Detail);
                sb.AppendLine();
            }
            sb.AppendLine($"--- FLUSH reason={Sanitize(reason)} ---");
            File.AppendAllText(FilePath, sb.ToString(), new UTF8Encoding(false));
        }
        catch
        {
            // Diagnostics must never prevent Manager startup.
        }
    }

    private static string Sanitize(object? value) => (value?.ToString() ?? "")
        .Replace("\r", " ", StringComparison.Ordinal)
        .Replace("\n", " ", StringComparison.Ordinal)
        .Replace("|", "/", StringComparison.Ordinal);
}
'''
write(root / 'src-wpf/FreeCamManager/Services/StartupTimingRecorder.cs', profiler)

# 2) Instrument the existing startup chain without changing its behavior/order.
p, text = read('src-wpf/FreeCamManager/App.xaml.cs')
text = replace_once(
    text,
    'public partial class App : Application\n{\n    private static readonly Version CurrentVersion = AppVersionService.FromAssembly(typeof(App).Assembly);',
    'public partial class App : Application\n{\n    private static readonly Stopwatch StartupClock = Stopwatch.StartNew();\n    private static readonly Version CurrentVersion = AppVersionService.FromAssembly(typeof(App).Assembly);',
    'startup clock',
)
text = replace_once(
    text,
    '    protected override async void OnStartup(StartupEventArgs e)\n    {\n        _singleInstance = new SingleInstanceService();',
    '    protected override async void OnStartup(StartupEventArgs e)\n    {\n        var startupTiming = new StartupTimingRecorder(StartupClock, AppPaths.DataDirectory, Version);\n        startupTiming.Mark("ON_STARTUP_ENTER");\n        _singleInstance = new SingleInstanceService();',
    'startup recorder init',
)
text = replace_once(
    text,
    '            stage = "初始化诊断日志";',
    '            startupTiming.Mark("SETTINGS_READY");\n\n            stage = "初始化诊断日志";',
    'settings timing',
)
text = replace_once(
    text,
    '            _log.Event("APP_START", ("root", settings.RootDir), ("inbox", settings.InboxDir), ("theme", settings.Theme), ("log_dir", actualLogDir), ("logging_enabled", settings.ManagerLoggingEnabled));',
    '            _log.Event("APP_START", ("root", settings.RootDir), ("inbox", settings.InboxDir), ("theme", settings.Theme), ("log_dir", actualLogDir), ("logging_enabled", settings.ManagerLoggingEnabled));\n            startupTiming.Mark("LOG_READY", ("manager_logging", settings.ManagerLoggingEnabled));',
    'log timing',
)
text = replace_once(
    text,
    '                ("migration_backup", _sqliteSession.MigrationBackupDirectory));',
    '                ("migration_backup", _sqliteSession.MigrationBackupDirectory));\n            startupTiming.Mark("SQLITE_READY", ("items", _sqliteSession.Items.Count), ("source", _sqliteSession.RecoverySource));',
    'sqlite timing',
)
text = replace_once(
    text,
    '            var discardCleanup = new DiscardCleanupService(_library, organizer, result, () => settings.RootDir, () => SettingsService.ResultRoot(settings));',
    '            var discardCleanup = new DiscardCleanupService(_library, organizer, result, () => settings.RootDir, () => SettingsService.ResultRoot(settings));\n            startupTiming.Mark("SERVICES_READY");',
    'services timing',
)
text = replace_once(
    text,
    '            _managerUpdater = new ManagerUpdateService(AppPaths.UpdateDirectory);',
    '            _managerUpdater = new ManagerUpdateService(AppPaths.UpdateDirectory);\n            startupTiming.Mark("TERMS_READY", ("term_count", filenameTermCount));',
    'terms timing',
)
text = replace_once(
    text,
    '            stage = "校验并重建版本库索引";',
    '            startupTiming.Mark("PATH_REBASE_DONE");\n\n            stage = "校验并重建版本库索引";',
    'path rebase timing',
)
text = replace_once(
    text,
    '            stage = "修复稳定版候选目录";',
    '            startupTiming.Mark("LIBRARY_REBUILD_DONE");\n\n            stage = "修复稳定版候选目录";',
    'library rebuild timing',
)
text = replace_once(
    text,
    '            stage = "清理到期已废弃版本";',
    '            startupTiming.Mark("STABLE_REPAIR_DONE");\n            stage = "清理到期已废弃版本";',
    'stable repair timing',
)
text = replace_once(
    text,
    '            var dialogs = new UserDialogService();',
    '            startupTiming.Mark("DISCARD_CLEANUP_DONE");\n\n            var dialogs = new UserDialogService();',
    'discard cleanup timing',
)
text = replace_once(
    text,
    '            theme.ApplyMode(settings.Theme);',
    '            theme.ApplyMode(settings.Theme);\n            startupTiming.Mark("THEME_READY");',
    'theme timing',
)
text = replace_once(
    text,
    '            var watcher = new InboxWatcherService(() => settings, _library, organizer, manifest, classification, extraction, testRefresh, _log, discardCleanup, libraryRebuild);',
    '            var watcher = new InboxWatcherService(() => settings, _library, organizer, manifest, classification, extraction, testRefresh, _log, discardCleanup, libraryRebuild);\n            startupTiming.Mark("WATCHER_READY");',
    'watcher timing',
)
text = replace_once(
    text,
    '                manifest => StageAndLaunchManagerUpdateAsync(manifest));',
    '                manifest => StageAndLaunchManagerUpdateAsync(manifest));\n            startupTiming.Mark("VIEWMODEL_READY");',
    'viewmodel timing',
)
text = replace_once(
    text,
    '            await _mainViewModel.RefreshAllAsync();',
    '            await _mainViewModel.RefreshAllAsync();\n            startupTiming.Mark("REFRESH_ALL_DONE");',
    'refresh timing',
)
text = replace_once(
    text,
    '            var window = new MainWindow { DataContext = _mainViewModel };\n            MainWindow = window;\n            window.Show();',
    '            var window = new MainWindow { DataContext = _mainViewModel };\n            startupTiming.Mark("WINDOW_CONSTRUCTED");\n            MainWindow = window;\n            window.ContentRendered += (_, _) =>\n            {\n                startupTiming.Mark("FIRST_RENDER");\n                startupTiming.Flush("FIRST_RENDER");\n            };\n            startupTiming.Mark("WINDOW_SHOW_CALL");\n            window.Show();\n            startupTiming.Mark("WINDOW_SHOW_RETURN");\n            startupTiming.Flush("WINDOW_SHOW_RETURN");',
    'window timing',
)
text = replace_once(
    text,
    '            _log?.Event("APP_READY");\n            _updateCleanupTask = Task.Run(() => CleanupSuccessfulUpdatesAsync(_shutdown.Token));',
    '            _log?.Event("APP_READY");\n            startupTiming.Mark("APP_READY");\n            startupTiming.Flush("APP_READY");\n            _updateCleanupTask = Task.Run(() => CleanupSuccessfulUpdatesAsync(_shutdown.Token));',
    'app ready timing',
)
text = replace_once(
    text,
    '        catch (Exception ex)\n        {\n            _log?.Event("STARTUP_FATAL", ("stage", stage), ("error", ex.ToString()));',
    '        catch (Exception ex)\n        {\n            startupTiming.Mark("STARTUP_FATAL", ("stage", stage), ("error_type", ex.GetType().Name));\n            startupTiming.Flush("STARTUP_FATAL");\n            _log?.Event("STARTUP_FATAL", ("stage", stage), ("error", ex.ToString()));',
    'fatal timing',
)
write(p, text)

# 3) Version/build metadata.
p, text = read('src-wpf/FreeCamManager/FreeCamManager.csproj')
text = replace_once(text, '<Version>3.9.3</Version>', '<Version>3.9.4</Version>', 'project version')
write(p, text)

manifest_path = root / 'BUILD_MANIFEST.json'
if manifest_path.exists():
    manifest = json.loads(manifest_path.read_text(encoding='utf-8-sig'))
else:
    manifest = {}
manifest.update({
    'Version': 'V3.9.4',
    'BuildName': 'FreeCam_Manager_V3.9.4',
    'Base': 'FreeCam_Manager_V3.9.3',
    'Branch': 'feature/manager-v3.9.4-startup-diagnostics',
    'Feature': 'Cold Startup Timing Diagnostics',
    'Stage': 'Diagnostic',
    'BuildId': 'MANAGER-V394-STARTUP-20260915',
})
manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')

print('Applied FreeCam Manager V3.9.4 startup diagnostics patch')
