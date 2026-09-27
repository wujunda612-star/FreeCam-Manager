using System.Windows.Input;
using FreeCamManager.Core.Models;
using FreeCamManager.Core.Services;
using FreeCamManager.Services;

namespace FreeCamManager.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    public string AppDisplayVersion { get; } = AppVersionService.FormatDisplay(AppVersionService.FromAssembly(typeof(MainWindowViewModel).Assembly));
    public string WindowTitle => $"FreeCam 可视化管理器 {AppDisplayVersion}";

    private object _currentPage;
    private string _statusText = "就绪";

    public MainWindowViewModel(
        AppSettings settings,
        SettingsService settingsService,
        LibraryService library,
        OrganizerService organizer,
        ClassificationService classification,
        UserDialogService dialogs,
        ThemeService theme,
        ExtractionService extraction,
        TestResultService results,
        FilenameAliasService filenameAliases,
        Func<Task<bool>> scanInboxNow,
        Action<AppSettings>? onSettingsSaved = null,
        TermsLocalState? localTermsState = null,
        Func<Task<TermsUpdateResult>>? checkTermsUpdate = null,
        Func<Task<ManagerUpdateCheckResult>>? checkManagerUpdate = null,
        Func<ManagerUpdateManifest, Task<bool>>? installManagerUpdate = null)
    {
        Func<IReadOnlyList<Artifact>> snapshot = library.Snapshot;
        Action<string> status = value => StatusText = value;
        Func<Task> refresh = RefreshAllAsync;

        Home = new HomeViewModel(snapshot, classification, settings, filenameAliases);
        var workspace = new TestWorkspaceService(extraction);
        Development = new DevelopmentViewModel(snapshot, library, organizer, classification, dialogs, workspace, results, refresh,
            () => settings.RootDir, () => SettingsService.TestingRoot(settings), () => SettingsService.ResultRoot(settings), status,
            settings, settingsService, filenameAliases, scanInboxNow);
        Stable = new StableViewModel(snapshot, organizer, dialogs, refresh, status);
        History = new HistoryViewModel(snapshot, library, organizer, classification, dialogs, refresh, status, settings, settingsService, filenameAliases);

        Settings = new SettingsViewModel(settings, settingsService, theme, filenameAliases, status, cfg =>
        {
            organizer.Root = cfg.RootDir;
            organizer.StableBackup = cfg.StableBackupDir;
            RefreshFilenameDisplay();
            onSettingsSaved?.Invoke(cfg);
        }, RefreshFilenameDisplay, localTermsState, checkTermsUpdate, checkManagerUpdate,
        installManagerUpdate is null ? null : async manifest =>
        {
            if (!dialogs.Confirm("更新 FreeCam Manager", $"将更新到 {manifest.DisplayVersion}。\n\nManager 会先下载源码，在本机自动编译；只有编译成功才会替换当前程序，然后自动重启。\n\n继续吗？"))
                return false;
            return await installManagerUpdate(manifest);
        });

        _currentPage = Home;
        NavigateCommand = new RelayCommand(p => Navigate(p?.ToString() ?? "首页"));
        RefreshCommand = new AsyncRelayCommand(_ => RefreshAllAsync());
    }

    public HomeViewModel Home { get; }
    public DevelopmentViewModel Development { get; }
    public StableViewModel Stable { get; }
    public HistoryViewModel History { get; }
    public SettingsViewModel Settings { get; }
    public ICommand NavigateCommand { get; }
    public ICommand RefreshCommand { get; }

    public object CurrentPage { get => _currentPage; private set => SetProperty(ref _currentPage, value); }
    public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }

    private void RefreshFilenameDisplay()
    {
        Home.Refresh();
        Development.Refresh();
        History.Refresh();
    }

    public async Task RefreshAllAsync()
    {
        await Task.Yield();
        Home.Refresh();
        Development.Refresh();
        Stable.Refresh();
        History.Refresh();
    }

    public void NotifyTermsSync(TermsUpdateResult result)
    {
        Settings.NotifyTermsSync(result);
        if (result.Updated) RefreshFilenameDisplay();
    }

    public void NotifyManagerUpdate(ManagerUpdateCheckResult result)
    {
        Settings.NotifyManagerUpdate(result);
    }

    private void Navigate(string name)
    {
        CurrentPage = name switch
        {
            "开发中" => Development,
            "稳定版" => Stable,
            "历史" => History,
            "设置" => Settings,
            _ => Home
        };
    }

}
