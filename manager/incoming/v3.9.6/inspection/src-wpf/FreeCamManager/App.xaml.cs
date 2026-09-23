using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using FreeCamManager.Core.Models;
using FreeCamManager.Core.Services;
using FreeCamManager.Services;
using FreeCamManager.SQLiteMigration.Core;
using FreeCamManager.ViewModels;

namespace FreeCamManager;

public partial class App : Application
{
    private static readonly Stopwatch StartupClock = Stopwatch.StartNew();
    private static readonly Version CurrentVersion = AppVersionService.FromAssembly(typeof(App).Assembly);
    private static readonly string Version = AppVersionService.FormatDisplay(CurrentVersion);
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _watcherTask;
    private Task? _termsUpdateTask;
    private Task? _managerUpdateTask;
    private Task? _updateCleanupTask;
    private RuntimeLogController? _log;
    private LibraryService? _library;
    private ProductionManagerSqliteLibrarySession? _sqliteSession;
    private MainWindowViewModel? _mainViewModel;
    private FilenameAliasService? _filenameAliases;
    private TermsUpdateService? _termsUpdater;
    private ManagerUpdateService? _managerUpdater;
    private SingleInstanceService? _singleInstance;
    private ThemeService? _theme;
    private readonly HashSet<string> _reportedUiErrors = new(StringComparer.Ordinal);

    protected override async void OnStartup(StartupEventArgs e)
    {
        var startupTiming = new StartupTimingRecorder(StartupClock, AppPaths.DataDirectory, Version);
        startupTiming.Mark("ON_STARTUP_ENTER");
        _singleInstance = new SingleInstanceService();
        if (!_singleInstance.TryAcquirePrimary())
        {
            SingleInstanceService.SignalPrimaryInstance();
            Shutdown();
            return;
        }

        base.OnStartup(e);
        var stage = "初始化";
        var startupWarnings = new List<string>();
        try
        {
            stage = $"读取设置：{AppPaths.SettingsFile}";
            var settingsService = new SettingsService();
            AppSettings settings;
            try
            {
                settings = await settingsService.LoadOrCreateAsync(AppPaths.SettingsFile);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                settings = settingsService.CreateDefault();
                startupWarnings.Add($"无法读取设置文件，已临时使用默认设置：{ex.Message}");
            }

            startupTiming.Mark("SETTINGS_READY");

            stage = "初始化诊断日志";
            var fallbackLogDir = System.IO.Path.Combine(AppPaths.DataDirectory, "Logs");
            _log = new RuntimeLogController(Version, fallbackLogDir);
            var actualLogDir = "";
            if (settings.ManagerLoggingEnabled)
            {
                if (!_log.Configure(true, settings.LogDir, out actualLogDir))
                    startupWarnings.Add("诊断日志目录暂时不可写；本次会话将继续运行，但不会生成 Manager 日志。");
                else if (!string.Equals(actualLogDir, settings.LogDir, StringComparison.OrdinalIgnoreCase))
                    startupWarnings.Add($"配置的日志目录不可写，本次日志已改存到：{actualLogDir}");
            }

            _log.Event("APP_START", ("root", settings.RootDir), ("inbox", settings.InboxDir), ("theme", settings.Theme), ("log_dir", actualLogDir), ("logging_enabled", settings.ManagerLoggingEnabled));
            startupTiming.Mark("LOG_READY", ("manager_logging", settings.ManagerLoggingEnabled));

            stage = $"打开 SQLite 版本库：{AppPaths.LibraryDatabaseFile}";
            var sqlitePaths = new ProductionStoragePaths(AppPaths.DataDirectory, settings.RootDir);
            _sqliteSession = await ProductionManagerSqliteLibrarySession.OpenAsync(sqlitePaths);
            _library = LibraryService.CreatePersistent(_sqliteSession.Items, _sqliteSession.SaveAsync);
            var sqliteSourceText = _sqliteSession.RecoverySource switch
            {
                "MainDatabase" => "SQLite 主库",
                "SqliteBackup" => "SQLite 安全备份",
                "JsonRecovery" => "JSON 灾难恢复导出",
                "LegacyJson" => "V3.7 library.json 首次迁移",
                "LegacyJsonBackup" => "V3.7 library.json.bak 首次迁移",
                _ => "新建 SQLite 数据库"
            };
            startupWarnings.Add($"V3.8：SQLite 主库已启用（来源：{sqliteSourceText}）。旧 library.json 不会在正常运行中被覆盖。");
            if (!string.IsNullOrWhiteSpace(_sqliteSession.MigrationBackupDirectory))
                startupWarnings.Add($"首次迁移安全备份：{_sqliteSession.MigrationBackupDirectory}");
            _log?.Event("SQLITE_V38_LIBRARY_OPEN",
                ("database", _sqliteSession.DatabaseFile),
                ("source", _sqliteSession.RecoverySource),
                ("items", _sqliteSession.Items.Count),
                ("migration_backup", _sqliteSession.MigrationBackupDirectory));
            startupTiming.Mark("SQLITE_READY", ("items", _sqliteSession.Items.Count), ("source", _sqliteSession.RecoverySource));

            stage = "创建应用服务";
            var manifest = new ManifestService();
            var classification = new ClassificationService();
            var hash = new HashService();
            var extraction = new ExtractionService();
            var result = new TestResultService(manifest);
            var testRefresh = new TestStatusRefreshService(_library, result, () => settings.RootDir);
            var organizer = new OrganizerService(settings.RootDir, settings.StableBackupDir, _library, manifest, classification, hash);
            var libraryRebuild = new LibraryRebuildService(_library, manifest, hash, _log);
            var discardCleanup = new DiscardCleanupService(_library, organizer, result, () => settings.RootDir, () => SettingsService.ResultRoot(settings));
            startupTiming.Mark("SERVICES_READY");
            _filenameAliases = new FilenameAliasService(AppPaths.FilenameTermsFile);
            var filenameAliases = _filenameAliases;
            var filenameTermCount = 0;
            try
            {
                filenameTermCount = await filenameAliases.EnsureAndReloadAsync();
                _log?.Event("FILENAME_TERMS_LOADED", ("count", filenameTermCount), ("file", filenameAliases.FilePath));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException)
            {
                filenameTermCount = filenameAliases.Count;
                startupWarnings.Add($"文件名术语表加载失败，已使用内置基础词表：{ex.Message}");
                _log?.Event("FILENAME_TERMS_FALLBACK", ("file", filenameAliases.FilePath), ("error", ex.Message));
            }
            _termsUpdater = new TermsUpdateService(AppPaths.FilenameTermsFile, AppPaths.FilenameTermsVersionFile);
            var localTermsState = await _termsUpdater.ReadLocalStateAsync(filenameTermCount);
            _managerUpdater = new ManagerUpdateService(AppPaths.UpdateDirectory);
            startupTiming.Mark("TERMS_READY", ("term_count", filenameTermCount));

            stage = "重定位版本库文件路径";
            try
            {
                var rebasedPaths = await new PathRebaseService().RepairLibraryPathsAsync(_library, settings.RootDir);
                if (rebasedPaths > 0)
                {
                    startupWarnings.Add($"已按当前 FreeCam 根目录自动修复 {rebasedPaths} 个旧路径记录。");
                    _log?.Event("LIBRARY_PATHS_REBASED", ("count", rebasedPaths), ("root", settings.RootDir));
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                startupWarnings.Add($"旧路径自动重定位暂时无法完成：{ex.Message}");
                _log?.Event("LIBRARY_PATH_REBASE_SKIPPED", ("error", ex.Message));
            }

            startupTiming.Mark("PATH_REBASE_DONE");

            stage = "校验并重建版本库索引";
            try
            {
                var rebuilt = await libraryRebuild.ReconcileAsync(settings.RootDir);
                if (rebuilt.Added > 0 || rebuilt.TestingLinked > 0 || rebuilt.ResultsPaired > 0)
                    startupWarnings.Add($"已从现有 FreeCam 目录恢复索引：新增 {rebuilt.Added} 项，测试目录关联 {rebuilt.TestingLinked} 项，Result 关联 {rebuilt.ResultsPaired} 项。");
                if (rebuilt.Failed > 0)
                    startupWarnings.Add($"版本库重建有 {rebuilt.Failed} 个文件暂时无法识别，可稍后点击刷新重试。");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                startupWarnings.Add($"现有 FreeCam 目录索引重建暂时无法完成：{ex.Message}");
                _log?.Event("LIBRARY_REBUILD_STARTUP_ERROR", ("error", ex.Message));
            }

            startupTiming.Mark("LIBRARY_REBUILD_DONE");

            stage = "修复稳定版候选目录";
            try
            {
                var repairedCandidates = await organizer.RepairStableCandidateDirectoriesAsync();
                if (repairedCandidates > 0)
                {
                    startupWarnings.Add($"已自动纠正 {repairedCandidates} 个 Stable（稳定版）候选记录/目录。");
                    _log?.Event("STABLE_CANDIDATE_REPAIRED", ("count", repairedCandidates));
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                startupWarnings.Add($"Stable 候选目录自动纠正暂时无法完成：{ex.Message}");
                _log?.Event("STABLE_CANDIDATE_REPAIR_SKIPPED", ("error", ex.Message));
            }
            startupTiming.Mark("STABLE_REPAIR_DONE");
            stage = "清理到期已废弃版本";
            try
            {
                var discardResult = await discardCleanup.CleanupDueAsync(DateTimeOffset.Now);
                if (discardResult.Deleted > 0)
                    startupWarnings.Add($"已自动删除 {discardResult.Deleted} 个到期的已废弃版本；Result / 日志已保留。");
                if (discardResult.Failed > 0)
                    startupWarnings.Add($"有 {discardResult.Failed} 个到期的已废弃版本自动删除失败，可稍后重试。");
                if (discardResult.Deleted > 0 || discardResult.Failed > 0 || discardResult.SkippedProtected > 0)
                    _log?.Event("DISCARD_CLEANUP_STARTUP", ("deleted", discardResult.Deleted), ("skipped_locked", discardResult.SkippedProtected), ("failed", discardResult.Failed));
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                startupWarnings.Add($"到期已废弃版本自动清理暂时无法完成：{ex.Message}");
                _log?.Event("DISCARD_CLEANUP_STARTUP_ERROR", ("error", ex.Message));
            }

            startupTiming.Mark("DISCARD_CLEANUP_DONE");

            var dialogs = new UserDialogService();
            _theme = new ThemeService();
            var theme = _theme;
            theme.ApplyMode(settings.Theme);
            startupTiming.Mark("THEME_READY");

            stage = "创建收件箱监控";
            var watcher = new InboxWatcherService(() => settings, _library, organizer, manifest, classification, extraction, testRefresh, _log, discardCleanup, libraryRebuild);
            startupTiming.Mark("WATCHER_READY");

            stage = "创建主界面";
            _mainViewModel = new MainWindowViewModel(settings, settingsService, _library, organizer, classification, dialogs, theme, extraction, result,
                filenameAliases, () => watcher.ScanNowAsync(), cfg => ApplyRuntimeSettings(cfg), localTermsState,
                () => CheckTermsUpdateAndApplyAsync(CancellationToken.None),
                () => CheckManagerUpdateAsync(CancellationToken.None),
                manifest => StageAndLaunchManagerUpdateAsync(manifest));
            startupTiming.Mark("VIEWMODEL_READY");
            watcher.StatusChanged += (_, text) => Dispatch(() => _mainViewModel.StatusText = text);
            watcher.LibraryChanged += (_, _) => DispatchAsync(_mainViewModel.RefreshAllAsync);
            await _mainViewModel.RefreshAllAsync();
            startupTiming.Mark("REFRESH_ALL_DONE");

            var window = new MainWindow { DataContext = _mainViewModel };
            startupTiming.Mark("WINDOW_CONSTRUCTED");
            MainWindow = window;
            window.ContentRendered += (_, _) =>
            {
                startupTiming.Mark("FIRST_RENDER");
                startupTiming.Flush("FIRST_RENDER");
            };
            startupTiming.Mark("WINDOW_SHOW_CALL");
            window.Show();
            startupTiming.Mark("WINDOW_SHOW_RETURN");
            startupTiming.Flush("WINDOW_SHOW_RETURN");
            _singleInstance.StartListening(() => Dispatch(BringPrimaryWindowToFront));

            if (startupWarnings.Count > 0)
            {
                _mainViewModel.StatusText = string.Join(" · ", startupWarnings);
                _log?.Event("STARTUP_WARNING", ("message", string.Join(" | ", startupWarnings)));
            }

            stage = "启动收件箱监控";
            _watcherTask = Task.Run(async () =>
            {
                try { await watcher.StartAsync(_shutdown.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
                catch (Exception ex) { _log?.Event("WATCHER_FATAL", ("error", ex.ToString())); }
            });

            _termsUpdateTask = Task.Run(() => TermsUpdateLoopAsync(_shutdown.Token));
            _managerUpdateTask = Task.Run(() => ManagerUpdateLoopAsync(_shutdown.Token));

            DispatcherUnhandledException += OnDispatcherUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
            _log?.Event("APP_READY");
            startupTiming.Mark("APP_READY");
            startupTiming.Flush("APP_READY");
            _updateCleanupTask = Task.Run(() => CleanupSuccessfulUpdatesAsync(_shutdown.Token));
        }
        catch (Exception ex)
        {
            startupTiming.Mark("STARTUP_FATAL", ("stage", stage), ("error_type", ex.GetType().Name));
            startupTiming.Flush("STARTUP_FATAL");
            _log?.Event("STARTUP_FATAL", ("stage", stage), ("error", ex.ToString()));
            MessageBox.Show($"FreeCam Manager 启动失败\n\n阶段：{stage}\n类型：{ex.GetType().Name}\n错误：{ex.Message}", "FreeCam Manager", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _shutdown.Cancel();
        try { _watcherTask?.GetAwaiter().GetResult(); } catch (OperationCanceledException) { } catch (Exception ex) { _log?.Event("WATCHER_STOP_ERROR", ("error", ex.Message)); }
        try { _termsUpdateTask?.GetAwaiter().GetResult(); } catch (OperationCanceledException) { } catch (Exception ex) { _log?.Event("TERMS_UPDATE_STOP_ERROR", ("error", ex.Message)); }
        try { _managerUpdateTask?.GetAwaiter().GetResult(); } catch (OperationCanceledException) { } catch (Exception ex) { _log?.Event("MANAGER_UPDATE_STOP_ERROR", ("error", ex.Message)); }
        try { _updateCleanupTask?.GetAwaiter().GetResult(); } catch (OperationCanceledException) { } catch (Exception ex) { _log?.Event("MANAGER_UPDATE_CLEANUP_STOP_ERROR", ("error", ex.Message)); }
        try { _library?.SaveAsync().GetAwaiter().GetResult(); } catch (Exception ex) { _log?.Event("LIBRARY_SAVE_EXIT_ERROR", ("error", ex.Message)); }
        if (_log is not null)
        {
            try { _log.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { }
        }
        _termsUpdater?.Dispose();
        _managerUpdater?.Dispose();
        _theme?.Dispose();
        _singleInstance?.Dispose();
        _shutdown.Dispose();
        base.OnExit(e);
    }


    private async Task CleanupSuccessfulUpdatesAsync(CancellationToken ct)
    {
        try
        {
            // Give the detached bootstrap/updater enough time to flush its final diagnostics
            // before removing successful-update staging files.
            await Task.Delay(TimeSpan.FromSeconds(3), ct).ConfigureAwait(false);
            var result = ManagerUpdateCleanupService.CleanupSuccessfulUpdates(AppPaths.UpdateDirectory);
            if (result.CleanedDirectories > 0 || result.FailedDirectories > 0)
            {
                _log?.Event("MANAGER_UPDATE_CLEANUP",
                    ("cleaned", result.CleanedDirectories),
                    ("removed", result.RemovedEntries),
                    ("failed", result.FailedDirectories));
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log?.Event("MANAGER_UPDATE_CLEANUP_ERROR", ("error", ex.Message));
        }
    }

    private async Task TermsUpdateLoopAsync(CancellationToken ct)
    {
        try
        {
            await CheckTermsUpdateAndApplyAsync(ct).ConfigureAwait(false);
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
                await CheckTermsUpdateAndApplyAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _log?.Event("TERMS_UPDATE_LOOP_ERROR", ("error", ex.ToString()));
        }
    }

    private async Task<TermsUpdateResult> CheckTermsUpdateAndApplyAsync(CancellationToken ct)
    {
        if (_termsUpdater is null || _filenameAliases is null)
            return new TermsUpdateResult(TermsUpdateStatus.Failed, "未知", 0, DateTimeOffset.Now, "术语更新服务尚未初始化", "not initialized");

        var result = await _termsUpdater.CheckForUpdateAsync(ct).ConfigureAwait(false);
        if (result.Updated)
        {
            try
            {
                var count = await _filenameAliases.ReloadAsync(ct).ConfigureAwait(false);
                result = result with { TermCount = count };
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException)
            {
                result = new TermsUpdateResult(TermsUpdateStatus.Failed, result.Version, result.TermCount, result.CheckedAt,
                    "在线术语已下载，但重新加载失败；继续使用当前内存术语表", ex.Message);
            }
        }

        _log?.Event("TERMS_UPDATE_CHECK", ("status", result.Status.ToString()), ("version", result.Version), ("count", result.TermCount), ("error", result.Error ?? ""));
        Dispatch(() => _mainViewModel?.NotifyTermsSync(result));
        return result;
    }

    private async Task ManagerUpdateLoopAsync(CancellationToken ct)
    {
        try
        {
            var initial = await CheckManagerUpdateAsync(ct).ConfigureAwait(false);
            Dispatch(() => _mainViewModel?.NotifyManagerUpdate(initial));

            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(30));
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                var result = await CheckManagerUpdateAsync(ct).ConfigureAwait(false);
                Dispatch(() => _mainViewModel?.NotifyManagerUpdate(result));
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _log?.Event("MANAGER_UPDATE_LOOP_ERROR", ("error", ex.ToString()));
        }
    }

    private async Task<ManagerUpdateCheckResult> CheckManagerUpdateAsync(CancellationToken ct)
    {
        if (_managerUpdater is null)
        {
            return new ManagerUpdateCheckResult(
                ManagerUpdateStatus.Failed, CurrentVersion, null, DateTimeOffset.Now,
                "软件更新服务尚未初始化", null, "not initialized");
        }

        var result = await _managerUpdater.CheckAsync(CurrentVersion, ct).ConfigureAwait(false);
        _log?.Event("MANAGER_UPDATE_CHECK", ("status", result.Status.ToString()),
            ("current", AppVersionService.FormatDisplay(result.CurrentVersion)),
            ("latest", result.LatestVersion is null ? "" : AppVersionService.FormatDisplay(result.LatestVersion)),
            ("error", result.Error ?? ""));
        return result;
    }

    private async Task<bool> StageAndLaunchManagerUpdateAsync(ManagerUpdateManifest manifest)
    {
        if (_managerUpdater is null) return false;
        try
        {
            if (_mainViewModel is not null)
                _mainViewModel.StatusText = $"正在下载 {manifest.DisplayVersion} 源码并准备更新…";
            var staged = await _managerUpdater.StageAsync(manifest).ConfigureAwait(true);
            var currentExe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(currentExe) || !File.Exists(currentExe))
                currentExe = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(currentExe) || !File.Exists(currentExe))
                throw new InvalidOperationException("无法确定当前 Manager EXE 路径");

            // Launch through a locally-generated .cmd bootstrap instead of attaching the updater
            // PowerShell process directly to this WPF process. The bootstrap writes diagnostics
            // before PowerShell starts, so handoff failures are visible even when the script
            // itself never reaches its first logging statement.
            // The bootstrap updater must come from the currently running Manager, not from the
            // downloaded target package. Otherwise a broken updater inside the target package can
            // prevent an older Manager from ever upgrading to it.
            var updaterPath = EmbeddedManagerUpdater.WriteTo(staged.StagingDirectory);
            var bootstrapPath = Path.Combine(staged.StagingDirectory, "Run_Manager_Update.cmd");
            var bootstrapLog = Path.Combine(staged.StagingDirectory, "UPDATE_BOOTSTRAP.log");
            var handoffFile = Path.Combine(staged.StagingDirectory, "UPDATE_HANDOFF.txt");
            var bootstrap = string.Join(Environment.NewLine, new[]
            {
                "@echo off",
                "setlocal",
                $"echo [%date% %time%] bootstrap start> {CmdQuote(bootstrapLog)}",
                $"echo updater={CmdEscapeValue(updaterPath)}>> {CmdQuote(bootstrapLog)}",
                $"echo currentExe={CmdEscapeValue(currentExe)}>> {CmdQuote(bootstrapLog)}",
                $"echo source={CmdEscapeValue(staged.SourceRoot)}>> {CmdQuote(bootstrapLog)}",
                $"powershell.exe -NoProfile -ExecutionPolicy Bypass -File {CmdQuote(updaterPath)} -OldPid {Environment.ProcessId} -CurrentExe {CmdQuote(currentExe)} -StagedSourceRoot {CmdQuote(staged.SourceRoot)} -TargetVersion {CmdQuote(manifest.DisplayVersion)} -Restart >> {CmdQuote(bootstrapLog)} 2>&1",
                "set \"RC=%ERRORLEVEL%\"",
                $"echo [%date% %time%] powershell exitCode=%RC%>> {CmdQuote(bootstrapLog)}",
                "exit /b %RC%",
                ""
            });
            File.WriteAllText(bootstrapPath, bootstrap, new System.Text.UTF8Encoding(false));
            File.WriteAllText(handoffFile, string.Join(Environment.NewLine, new[]
            {
                $"time={DateTimeOffset.Now:O}",
                $"target={manifest.DisplayVersion}",
                $"oldPid={Environment.ProcessId}",
                $"currentExe={currentExe}",
                $"source={staged.SourceRoot}",
                $"updater={updaterPath}",
                $"bootstrap={bootstrapPath}"
            }), new System.Text.UTF8Encoding(false));

            var psi = new ProcessStartInfo
            {
                FileName = bootstrapPath,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = staged.StagingDirectory
            };

            var process = Process.Start(psi) ?? throw new InvalidOperationException("无法启动独立更新器");
            _log?.Event("MANAGER_UPDATE_HANDOFF", ("pid", process.Id), ("target", manifest.DisplayVersion),
                ("source", staged.SourceRoot), ("exe", currentExe), ("bootstrap", bootstrapPath));

            var statusFile = Path.Combine(staged.StagingDirectory, "UPDATE_STATUS.json");
            var handoffReady = false;
            for (var i = 0; i < 50; i++)
            {
                if (File.Exists(statusFile))
                {
                    handoffReady = true;
                    break;
                }
                await Task.Delay(100).ConfigureAwait(true);
            }
            if (!handoffReady)
                throw new InvalidOperationException($"更新器未进入执行阶段；请查看：{bootstrapLog}");

            _log?.Event("MANAGER_UPDATE_HANDOFF_READY", ("pid", process.Id), ("status", statusFile));
            if (_mainViewModel is not null)
                _mainViewModel.StatusText = $"{manifest.DisplayVersion} 更新器已接管，Manager 即将退出…";
            await Dispatcher.InvokeAsync(() => Shutdown());
            return true;
        }
        catch (Exception ex)
        {
            _log?.Event("MANAGER_UPDATE_STAGE_ERROR", ("target", manifest.DisplayVersion), ("error", ex.ToString()));
            if (_mainViewModel is not null)
                _mainViewModel.StatusText = $"更新准备失败：{ex.Message}";
            return false;
        }
    }

    private static string CmdEscapeValue(string value)
        => value.Replace("%", "%%", StringComparison.Ordinal);

    private static string CmdQuote(string value)
        => "\"" + CmdEscapeValue(value).Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private void BringPrimaryWindowToFront()
    {
        var window = MainWindow;
        if (window is null) return;
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        if (!window.IsVisible) window.Show();
        window.Activate();
        var handle = new WindowInteropHelper(window).Handle;
        if (handle != IntPtr.Zero) SetForegroundWindow(handle);
        window.Topmost = true;
        window.Topmost = false;
        window.Focus();
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private void ApplyRuntimeSettings(AppSettings settings)
    {
        if (_log is null) return;
        if (!_log.Configure(settings.ManagerLoggingEnabled, settings.LogDir, out var usedDirectory))
        {
            if (_mainViewModel is not null) _mainViewModel.StatusText = "Manager 日志开启失败：日志目录不可写。";
            return;
        }
        _log.Event("LOG_SETTING_CHANGED", ("enabled", settings.ManagerLoggingEnabled), ("directory", usedDirectory));
    }

    private void Dispatch(Action action)
    {
        if (Dispatcher.CheckAccess()) action();
        else _ = Dispatcher.InvokeAsync(action, DispatcherPriority.Background);
    }

    private void DispatchAsync(Func<Task> action)
    {
        if (Dispatcher.CheckAccess()) { _ = action(); return; }
        _ = Dispatcher.InvokeAsync(new Action(() => _ = action()), DispatcherPriority.Background);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var detail = e.Exception.ToString();
        var signature = $"{e.Exception.GetType().FullName}|{e.Exception.Message}";
        _log?.Event("UI_UNHANDLED", ("error", detail));

        if (_reportedUiErrors.Add(signature))
        {
            var message = e.Exception.InnerException is null
                ? e.Exception.Message
                : $"{e.Exception.Message}\n\n内部错误：{e.Exception.InnerException.Message}";
            MessageBox.Show("界面发生异常，日志已经记录：\n\n" + message, "FreeCam Manager", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        else if (_mainViewModel is not null)
        {
            _mainViewModel.StatusText = "相同的界面异常已记录，已阻止重复弹窗。";
        }

        e.Handled = true;
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _log?.Event("TASK_UNOBSERVED", ("error", e.Exception.ToString()));
        e.SetObserved();
    }
}
