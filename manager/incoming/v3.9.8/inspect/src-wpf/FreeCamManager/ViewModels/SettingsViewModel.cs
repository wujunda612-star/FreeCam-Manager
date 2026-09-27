using System.IO;
using System.Diagnostics;
using System.Collections.ObjectModel;
using System.Windows.Input;
using FreeCamManager.Core.Models;
using FreeCamManager.Core.Services;
using FreeCamManager.Services;
using Microsoft.Win32;

namespace FreeCamManager.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly SettingsService _settingsService;
    private readonly ThemeService _theme;
    private readonly FilenameAliasService _filenameAliases;
    private readonly Action<string> _statusSink;
    private readonly Action<AppSettings>? _onSaved;
    private readonly Action? _onFilenameDisplayChanged;
    private readonly Func<Task<TermsUpdateResult>>? _checkTermsUpdate;
    private readonly Func<Task<ManagerUpdateCheckResult>>? _checkManagerUpdate;
    private readonly Func<ManagerUpdateManifest, Task<bool>>? _installManagerUpdate;
    private readonly AsyncRelayCommand _installManagerUpdateCommand;
    private string _rootDir;
    private string _inboxDir;
    private string _stableBackupDir;
    private string _logDir;
    private int _scanSeconds;
    private string _selectedTheme;
    private bool _managerLoggingEnabled;
    private string _selectedDiscardDeletePolicy;
    private bool _hideFreeCamPrefix;
    private bool _showFilenameAliases;
    private bool _showFeatureAliases;
    private bool _showStageAliases;
    private string _termsSummary = "版本：内置 · 0 条";
    private string _termsLastCheckedText = "上次检查：尚未";
    private string _termsSyncStatusText = "GitHub 自动同步：每 5 分钟检查";
    private string _managerCurrentVersionText;
    private string _managerLatestVersionText = "线上版本：尚未检查";
    private string _managerUpdateLastCheckedText = "上次检查：尚未";
    private string _managerUpdateStatusText = "GitHub 自动检查：每 30 分钟";
    private ManagerUpdateManifest? _availableManagerUpdate;
    private bool _loading = true;
    private string _rulePreviewName = "WW36_Phase3_GICamera_Test3.1_Result.zip";
    private string _rulePreviewBuildType = "";
    private string _rulePreviewBranch = "";
    private string _rulePreviewText = "输入文件名后点击“预览匹配”；Manifest 条件可填写构建类型和分支。";

    public SettingsViewModel(AppSettings settings, SettingsService settingsService, ThemeService theme, FilenameAliasService filenameAliases,
        Action<string> statusSink, Action<AppSettings>? onSaved = null, Action? onFilenameDisplayChanged = null,
        TermsLocalState? localTermsState = null, Func<Task<TermsUpdateResult>>? checkTermsUpdate = null,
        Func<Task<ManagerUpdateCheckResult>>? checkManagerUpdate = null,
        Func<ManagerUpdateManifest, Task<bool>>? installManagerUpdate = null)
    {
        _settings = settings; _settingsService = settingsService; _theme = theme; _filenameAliases = filenameAliases;
        _statusSink = statusSink; _onSaved = onSaved; _onFilenameDisplayChanged = onFilenameDisplayChanged; _checkTermsUpdate = checkTermsUpdate;
        _checkManagerUpdate = checkManagerUpdate; _installManagerUpdate = installManagerUpdate;
        _managerCurrentVersionText = $"当前版本：{AppVersionService.FormatDisplay(AppVersionService.FromAssembly(typeof(SettingsViewModel).Assembly))}";
        _rootDir = settings.RootDir; _inboxDir = settings.InboxDir; _stableBackupDir = settings.StableBackupDir;
        _logDir = settings.LogDir; _scanSeconds = settings.ScanSeconds; _selectedTheme = ThemeLabel(settings.Theme);
        _managerLoggingEnabled = settings.ManagerLoggingEnabled;
        _selectedDiscardDeletePolicy = PolicyLabel(settings.DiscardAutoDeleteDays);
        _hideFreeCamPrefix = settings.HideFreeCamPrefix;
        _showFilenameAliases = settings.ShowFilenameAliases;
        _showFeatureAliases = settings.ShowFeatureAliases;
        _showStageAliases = settings.ShowStageAliases;
        if (localTermsState is not null) _termsSummary = $"版本：{localTermsState.Version} · {localTermsState.TermCount} 条";
        LaunchRules = new ObservableCollection<FileRule>((settings.LaunchRules ?? RuleDefaults.Launch()).Select(x => x.Clone()));
        DragRules = new ObservableCollection<FileRule>((settings.DragRules ?? RuleDefaults.Drag()).Select(x => x.Clone()));
        ClassificationRules = new ObservableCollection<FileRule>((settings.ClassificationRules ?? RuleDefaults.Classification()).Select(x => x.Clone()));
        AddLaunchRuleCommand = new RelayCommand(_ => LaunchRules.Add(new FileRule { Pattern = "Start*.cmd" }));
        AddDragRuleCommand = new RelayCommand(_ => DragRules.Add(new FileRule { Pattern = "*_Result.zip" }));
        AddClassificationRuleCommand = new RelayCommand(_ => ClassificationRules.Add(new FileRule { Pattern = "*.zip", Category = "新分类", Directory = "70_自定义" }));
        MoveLaunchUpCommand = new RelayCommand(item => Move(LaunchRules, item as FileRule, -1));
        MoveLaunchDownCommand = new RelayCommand(item => Move(LaunchRules, item as FileRule, 1));
        RemoveLaunchCommand = new RelayCommand(item => Remove(LaunchRules, item as FileRule));
        MoveDragUpCommand = new RelayCommand(item => Move(DragRules, item as FileRule, -1));
        MoveDragDownCommand = new RelayCommand(item => Move(DragRules, item as FileRule, 1));
        RemoveDragCommand = new RelayCommand(item => Remove(DragRules, item as FileRule));
        MoveClassificationUpCommand = new RelayCommand(item => Move(ClassificationRules, item as FileRule, -1));
        MoveClassificationDownCommand = new RelayCommand(item => Move(ClassificationRules, item as FileRule, 1));
        RemoveClassificationCommand = new RelayCommand(item => Remove(ClassificationRules, item as FileRule));
        ResetLaunchCommand = new RelayCommand(_ => Reset(LaunchRules, RuleDefaults.Launch()));
        ResetDragCommand = new RelayCommand(_ => Reset(DragRules, RuleDefaults.Drag()));
        ResetClassificationCommand = new RelayCommand(_ => Reset(ClassificationRules, RuleDefaults.Classification()));
        PreviewRulesCommand = new RelayCommand(_ => PreviewRules());
        ChooseRootCommand = new RelayCommand(_ => ChooseFolder("选择 FreeCam 根目录", value => RootDir = value, RootDir));
        ChooseInboxCommand = new RelayCommand(_ => ChooseFolder("选择收件箱目录", value => InboxDir = value, InboxDir));
        ChooseBackupCommand = new RelayCommand(_ => ChooseFolder("选择 Stable Backup（稳定版备份盘）", value => StableBackupDir = value, StableBackupDir));
        ChooseLogsCommand = new RelayCommand(_ => ChooseFolder("选择日志目录", value => LogDir = value, LogDir));
        OpenFilenameTermsCommand = new AsyncRelayCommand(_ => OpenFilenameTermsAsync());
        ReloadFilenameTermsCommand = new AsyncRelayCommand(_ => ReloadFilenameTermsAsync());
        CheckTermsUpdateCommand = new AsyncRelayCommand(_ => CheckTermsUpdateAsync(), _ => _checkTermsUpdate is not null);
        CheckManagerUpdateCommand = new AsyncRelayCommand(_ => CheckManagerUpdateAsync(), _ => _checkManagerUpdate is not null);
        _installManagerUpdateCommand = new AsyncRelayCommand(_ => InstallManagerUpdateAsync(), _ => _installManagerUpdate is not null && _availableManagerUpdate is not null);
        InstallManagerUpdateCommand = _installManagerUpdateCommand;
        SaveCommand = new AsyncRelayCommand(_ => SaveAsync());
        _loading = false;
    }

    public ObservableCollection<FileRule> LaunchRules { get; }
    public ObservableCollection<FileRule> DragRules { get; }
    public ObservableCollection<FileRule> ClassificationRules { get; }
    public IReadOnlyList<string> ClassificationMatchFields { get; } = ["文件名", "构建类型", "分支", "产物类型", "发布状态"];
    public string RulePreviewName { get => _rulePreviewName; set => SetProperty(ref _rulePreviewName, value); }
    public string RulePreviewBuildType { get => _rulePreviewBuildType; set => SetProperty(ref _rulePreviewBuildType, value); }
    public string RulePreviewBranch { get => _rulePreviewBranch; set => SetProperty(ref _rulePreviewBranch, value); }
    public string RulePreviewText { get => _rulePreviewText; private set => SetProperty(ref _rulePreviewText, value); }
    public ICommand AddLaunchRuleCommand { get; }
    public ICommand AddDragRuleCommand { get; }
    public ICommand AddClassificationRuleCommand { get; }
    public ICommand MoveLaunchUpCommand { get; }
    public ICommand MoveLaunchDownCommand { get; }
    public ICommand RemoveLaunchCommand { get; }
    public ICommand MoveDragUpCommand { get; }
    public ICommand MoveDragDownCommand { get; }
    public ICommand RemoveDragCommand { get; }
    public ICommand MoveClassificationUpCommand { get; }
    public ICommand MoveClassificationDownCommand { get; }
    public ICommand RemoveClassificationCommand { get; }
    public ICommand ResetLaunchCommand { get; }
    public ICommand ResetDragCommand { get; }
    public ICommand ResetClassificationCommand { get; }
    public ICommand PreviewRulesCommand { get; }

    public IReadOnlyList<string> Themes { get; } = ["跟随系统", "深色模式", "浅色模式"];
    public IReadOnlyList<string> DiscardDeletePolicies { get; } = ["不自动删除", "1天", "3天", "7天", "30天"];
    public string RootDir { get => _rootDir; set => SetProperty(ref _rootDir, value); }
    public string InboxDir { get => _inboxDir; set => SetProperty(ref _inboxDir, value); }
    public string StableBackupDir { get => _stableBackupDir; set => SetProperty(ref _stableBackupDir, value); }
    public string LogDir { get => _logDir; set => SetProperty(ref _logDir, value); }
    public int ScanSeconds { get => _scanSeconds; set => SetProperty(ref _scanSeconds, Math.Clamp(value, 1, 60)); }
    public string SelectedDiscardDeletePolicy
    {
        get => _selectedDiscardDeletePolicy;
        set
        {
            if (!SetProperty(ref _selectedDiscardDeletePolicy, value)) return;
            _settings.DiscardAutoDeleteDays = PolicyDays(value);
        }
    }
    public bool HideFreeCamPrefix
    {
        get => _hideFreeCamPrefix;
        set
        {
            if (!SetProperty(ref _hideFreeCamPrefix, value)) return;
            _settings.HideFreeCamPrefix = value;
            if (!_loading) _onFilenameDisplayChanged?.Invoke();
        }
    }
    public bool ShowFilenameAliases
    {
        get => _showFilenameAliases;
        set
        {
            if (!SetProperty(ref _showFilenameAliases, value)) return;
            _settings.ShowFilenameAliases = value;
            if (!_loading) _onFilenameDisplayChanged?.Invoke();
        }
    }
    public bool ShowFeatureAliases
    {
        get => _showFeatureAliases;
        set
        {
            if (!SetProperty(ref _showFeatureAliases, value)) return;
            _settings.ShowFeatureAliases = value;
            if (!_loading) _onFilenameDisplayChanged?.Invoke();
        }
    }
    public bool ShowStageAliases
    {
        get => _showStageAliases;
        set
        {
            if (!SetProperty(ref _showStageAliases, value)) return;
            _settings.ShowStageAliases = value;
            if (!_loading) _onFilenameDisplayChanged?.Invoke();
        }
    }
    public string TermsSummary { get => _termsSummary; private set => SetProperty(ref _termsSummary, value); }
    public string TermsLastCheckedText { get => _termsLastCheckedText; private set => SetProperty(ref _termsLastCheckedText, value); }
    public string TermsSyncStatusText { get => _termsSyncStatusText; private set => SetProperty(ref _termsSyncStatusText, value); }
    public string ManagerCurrentVersionText { get => _managerCurrentVersionText; private set => SetProperty(ref _managerCurrentVersionText, value); }
    public string ManagerLatestVersionText { get => _managerLatestVersionText; private set => SetProperty(ref _managerLatestVersionText, value); }
    public string ManagerUpdateLastCheckedText { get => _managerUpdateLastCheckedText; private set => SetProperty(ref _managerUpdateLastCheckedText, value); }
    public string ManagerUpdateStatusText { get => _managerUpdateStatusText; private set => SetProperty(ref _managerUpdateStatusText, value); }
    public bool HasManagerUpdate => _availableManagerUpdate is not null;

    public bool ManagerLoggingEnabled
    {
        get => _managerLoggingEnabled;
        set
        {
            if (!SetProperty(ref _managerLoggingEnabled, value)) return;
            if (_loading) return;
            _settings.ManagerLoggingEnabled = value;
            _ = SaveLoggingToggleAsync();
        }
    }

    public string SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (!SetProperty(ref _selectedTheme, value)) return;
            _theme.ApplyMode(ThemeMode(value));
        }
    }

    public ICommand ChooseRootCommand { get; }
    public ICommand ChooseInboxCommand { get; }
    public ICommand ChooseBackupCommand { get; }
    public ICommand ChooseLogsCommand { get; }
    public ICommand OpenFilenameTermsCommand { get; }
    public ICommand ReloadFilenameTermsCommand { get; }
    public ICommand CheckTermsUpdateCommand { get; }
    public ICommand CheckManagerUpdateCommand { get; }
    public ICommand InstallManagerUpdateCommand { get; }
    public ICommand SaveCommand { get; }

    private async Task SaveAsync()
    {
        try
        {
            FileRuleEngine.Validate(LaunchRules.ToList(), "启动");
            FileRuleEngine.Validate(DragRules.ToList(), "拖拽");
            FileRuleEngine.Validate(ClassificationRules.ToList(), "分类");
        }
        catch (Exception ex)
        {
            _statusSink("规则未保存：" + ex.Message);
            return;
        }
        _settings.LaunchRules = LaunchRules.Select(x => x.Clone()).ToList();
        _settings.DragRules = DragRules.Select(x => x.Clone()).ToList();
        _settings.ClassificationRules = ClassificationRules.Select(x => x.Clone()).ToList();
        _settings.RootDir = RootDir.Trim();
        _settings.InboxDir = InboxDir.Trim();
        _settings.StableBackupDir = StableBackupDir.Trim();
        _settings.LogDir = LogDir.Trim();
        _settings.ScanSeconds = Math.Clamp(ScanSeconds, 1, 60);
        _settings.Theme = ThemeMode(SelectedTheme);
        _settings.ManagerLoggingEnabled = ManagerLoggingEnabled;
        _settings.DiscardAutoDeleteDays = PolicyDays(SelectedDiscardDeletePolicy);
        _settings.HideFreeCamPrefix = HideFreeCamPrefix;
        _settings.ShowFilenameAliases = ShowFilenameAliases;
        _settings.ShowFeatureAliases = ShowFeatureAliases;
        _settings.ShowStageAliases = ShowStageAliases;
        try
        {
            await _settingsService.SaveAsync(AppPaths.SettingsFile, _settings);
            _settingsService.EnsureDirectories(_settings);
            _onSaved?.Invoke(_settings);
            _statusSink("设置及三套自定义规则已保存，新文件按新规则处理；已有归档不移动");
        }
        catch (Exception ex) { _statusSink("设置保存失败: " + ex.Message); }
    }

    private async Task OpenFilenameTermsAsync()
    {
        try
        {
            await _filenameAliases.EnsureFileAsync();
            Process.Start(new ProcessStartInfo(_filenameAliases.FilePath) { UseShellExecute = true });
            _statusSink("已打开中文术语表");
        }
        catch (Exception ex)
        {
            _statusSink("打开术语表失败: " + ex.Message);
        }
    }

    private async Task ReloadFilenameTermsAsync()
    {
        try
        {
            var count = await _filenameAliases.ReloadAsync();
            _onFilenameDisplayChanged?.Invoke();
            _statusSink($"术语表已重新加载：{count} 条");
        }
        catch (Exception ex)
        {
            _statusSink("术语表重新加载失败: " + ex.Message);
        }
    }

    private async Task CheckTermsUpdateAsync()
    {
        if (_checkTermsUpdate is null) return;
        TermsSyncStatusText = "正在检查 GitHub 术语表…";
        var result = await _checkTermsUpdate();
        NotifyTermsSync(result);
        _statusSink(result.Error is null ? result.Message : $"{result.Message}：{result.Error}");
    }

    public void NotifyTermsSync(TermsUpdateResult result)
    {
        if (result.Status != TermsUpdateStatus.Failed)
            TermsSummary = $"版本：{result.Version} · {result.TermCount} 条";
        TermsLastCheckedText = $"上次检查：{result.CheckedAt.LocalDateTime:MM-dd HH:mm:ss}";
        TermsSyncStatusText = result.Status switch
        {
            TermsUpdateStatus.Updated => "GitHub 自动同步：已更新并立即生效",
            TermsUpdateStatus.UpToDate => "GitHub 自动同步：当前已是最新",
            TermsUpdateStatus.Failed => "GitHub 自动同步：检查失败，继续使用本地术语表",
            _ => "GitHub 自动同步：每 5 分钟检查"
        };
    }

    private async Task CheckManagerUpdateAsync()
    {
        if (_checkManagerUpdate is null) return;
        ManagerUpdateStatusText = "正在检查软件更新…";
        var result = await _checkManagerUpdate();
        NotifyManagerUpdate(result);
        _statusSink(result.Error is null ? result.Message : $"{result.Message}：{result.Error}");
    }

    private async Task InstallManagerUpdateAsync()
    {
        if (_installManagerUpdate is null || _availableManagerUpdate is null) return;
        var manifest = _availableManagerUpdate;
        ManagerUpdateStatusText = $"正在准备 {manifest.DisplayVersion} 更新…";
        _statusSink($"正在下载并准备 {manifest.DisplayVersion} 更新…");
        var started = await _installManagerUpdate(manifest);
        if (!started)
        {
            ManagerUpdateStatusText = "更新未启动；继续使用当前版本";
            _statusSink("更新未启动；当前版本保持不变");
        }
    }

    public void NotifyManagerUpdate(ManagerUpdateCheckResult result)
    {
        ManagerUpdateLastCheckedText = $"上次检查：{result.CheckedAt.LocalDateTime:MM-dd HH:mm:ss}";
        if (result.Status == ManagerUpdateStatus.UpdateAvailable && result.Manifest is not null)
        {
            _availableManagerUpdate = result.Manifest;
            ManagerLatestVersionText = $"线上版本：{result.Manifest.DisplayVersion}";
            ManagerUpdateStatusText = "发现新版本，可一键更新并自动编译";
        }
        else if (result.Status == ManagerUpdateStatus.Failed)
        {
            _availableManagerUpdate = null;
            ManagerLatestVersionText = "线上版本：—";
            ManagerUpdateStatusText = "GitHub 自动检查失败；继续使用当前版本";
        }
        else
        {
            _availableManagerUpdate = null;
            var onlineText = !string.IsNullOrWhiteSpace(result.Manifest?.DisplayVersion)
                ? result.Manifest!.DisplayVersion
                : AppVersionService.FormatDisplay(result.LatestVersion ?? result.CurrentVersion);
            ManagerLatestVersionText = $"线上版本：{onlineText}";
            ManagerUpdateStatusText = result.LatestVersion is not null && result.CurrentVersion > result.LatestVersion
                ? "当前版本高于线上发布版"
                : "GitHub 自动检查：当前已是最新";
        }
        OnPropertyChanged(nameof(HasManagerUpdate));
        _installManagerUpdateCommand.RaiseCanExecuteChanged();
    }

    private async Task SaveLoggingToggleAsync()
    {
        try
        {
            await _settingsService.SaveAsync(AppPaths.SettingsFile, _settings);
            _onSaved?.Invoke(_settings);
            _statusSink(ManagerLoggingEnabled ? "Manager 日志记录已开启" : "Manager 日志记录已关闭");
        }
        catch (Exception ex)
        {
            _statusSink("Manager 日志设置保存失败: " + ex.Message);
        }
    }

    private static void Move(ObservableCollection<FileRule> rules, FileRule? rule, int delta)
    {
        if (rule is null) return;
        var index = rules.IndexOf(rule);
        var target = index + delta;
        if (index >= 0 && target >= 0 && target < rules.Count) rules.Move(index, target);
    }

    private static void Remove(ObservableCollection<FileRule> rules, FileRule? rule)
    {
        if (rule is not null) rules.Remove(rule);
    }

    private static void Reset(ObservableCollection<FileRule> target, IReadOnlyList<FileRule> defaults)
    {
        target.Clear();
        foreach (var rule in defaults) target.Add(rule.Clone());
    }

    private void PreviewRules()
    {
        try
        {
            var name = System.IO.Path.GetFileName(RulePreviewName.Trim());
            var launch = LaunchRules.FirstOrDefault(r => r.Enabled && FileRuleEngine.Matches(r.Pattern, name));
            var drag = DragRules.FirstOrDefault(r => r.Enabled && FileRuleEngine.Matches(r.Pattern, name));
            var artifact = new ManifestService().InspectFilename(name);
            artifact.Name = name;
            if (!string.IsNullOrWhiteSpace(RulePreviewBuildType)) artifact.BuildType = RulePreviewBuildType.Trim();
            if (!string.IsNullOrWhiteSpace(RulePreviewBranch)) artifact.Branch = RulePreviewBranch.Trim();
            var classification = new ClassificationService(() => ClassificationRules.ToList()).Plan(artifact);
            RulePreviewText = $"启动：{launch?.Pattern ?? "无匹配"}　拖拽：{drag?.Pattern ?? "无匹配"}\n分类：{(classification.Category == "Unknown" ? "留在收件箱" : classification.Category + " → " + classification.RelativeDirectory)}";
        }
        catch (Exception ex) { RulePreviewText = "预览失败：" + ex.Message; }
    }

    private static string ThemeLabel(string mode) => mode.ToLowerInvariant() switch
    {
        "light" => "浅色模式",
        "dark" => "深色模式",
        _ => "跟随系统"
    };

    private static string ThemeMode(string label) => label switch
    {
        "浅色模式" => "light",
        "深色模式" => "dark",
        _ => "system"
    };

    private static int PolicyDays(string value) => value switch { "1天" => 1, "3天" => 3, "7天" => 7, "30天" => 30, _ => 0 };
    private static string PolicyLabel(int days) => days switch { 1 => "1天", 3 => "3天", 7 => "7天", 30 => "30天", _ => "不自动删除" };

    private static void ChooseFolder(string title, Action<string> apply, string initial)
    {
        var dialog = new OpenFolderDialog { Title = title, Multiselect = false };
        if (!string.IsNullOrWhiteSpace(initial) && Directory.Exists(initial)) dialog.InitialDirectory = initial;
        if (dialog.ShowDialog() == true) apply(dialog.FolderName);
    }
}
