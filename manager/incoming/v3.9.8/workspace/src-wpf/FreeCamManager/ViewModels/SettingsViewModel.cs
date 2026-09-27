using System.IO;
using System.Collections.ObjectModel;
using System.Diagnostics;
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
    private EditableRuleRow? _selectedLaunchRule;
    private EditableRuleRow? _selectedDragRule;
    private EditableRuleRow? _selectedClassificationRule;
    private string _previewName = "WW36_Phase3_GICamera_Test3.1.zip";
    private string _previewSummary = "输入文件名后，点击预览。";
    public IReadOnlyList<string> RuleFields { get; } =
        ["FileName", "ArtifactType", "BuildType", "Branch", "ReleaseState", "StableLike", "FeatureLike", "ExperimentLike"];
    public ObservableCollection<EditableRuleRow> LaunchRules { get; } = [];
    public ObservableCollection<EditableRuleRow> DragRules { get; } = [];
    public ObservableCollection<EditableRuleRow> ClassificationRules { get; } = [];
    public EditableRuleRow? SelectedLaunchRule { get => _selectedLaunchRule; set => SetProperty(ref _selectedLaunchRule, value); }
    public EditableRuleRow? SelectedDragRule { get => _selectedDragRule; set => SetProperty(ref _selectedDragRule, value); }
    public EditableRuleRow? SelectedClassificationRule { get => _selectedClassificationRule; set => SetProperty(ref _selectedClassificationRule, value); }
    public string PreviewName { get => _previewName; set => SetProperty(ref _previewName, value); }
    public string PreviewSummary { get => _previewSummary; private set => SetProperty(ref _previewSummary, value); }


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
        ReplaceRules(LaunchRules, settings.LaunchRules ?? RuleDefaults.Launch());
        ReplaceRules(DragRules, settings.DragRules ?? RuleDefaults.Drag());
        ReplaceRules(ClassificationRules, settings.ClassificationRules ?? RuleDefaults.Classification());
        CreateRuleCommands();
        if (localTermsState is not null) _termsSummary = $"版本：{localTermsState.Version} · {localTermsState.TermCount} 条";
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

    public ICommand AddLaunchRuleCommand { get; private set; } = null!;
    public ICommand RemoveLaunchRuleCommand { get; private set; } = null!;
    public ICommand MoveLaunchUpCommand { get; private set; } = null!;
    public ICommand MoveLaunchDownCommand { get; private set; } = null!;
    public ICommand ResetLaunchRulesCommand { get; private set; } = null!;
    public ICommand AddDragRuleCommand { get; private set; } = null!;
    public ICommand RemoveDragRuleCommand { get; private set; } = null!;
    public ICommand MoveDragUpCommand { get; private set; } = null!;
    public ICommand MoveDragDownCommand { get; private set; } = null!;
    public ICommand ResetDragRulesCommand { get; private set; } = null!;
    public ICommand AddClassificationRuleCommand { get; private set; } = null!;
    public ICommand RemoveClassificationRuleCommand { get; private set; } = null!;
    public ICommand MoveClassificationUpCommand { get; private set; } = null!;
    public ICommand MoveClassificationDownCommand { get; private set; } = null!;
    public ICommand ResetClassificationRulesCommand { get; private set; } = null!;
    public ICommand PreviewLaunchCommand { get; private set; } = null!;
    public ICommand PreviewDragCommand { get; private set; } = null!;
    public ICommand PreviewClassificationCommand { get; private set; } = null!;
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
        var issue = ValidateRules();
        if (!string.IsNullOrWhiteSpace(issue))
        {
            _statusSink("自定义规则无法保存：" + issue);
            PreviewSummary = "检查规则：" + issue;
            return;
        }
        _settings.LaunchRules = LaunchRules.Select(x => x.ToPatternRule()).ToList();
        _settings.DragRules = DragRules.Select(x => x.ToPatternRule()).ToList();
        _settings.ClassificationRules = ClassificationRules.Select(x => x.ToClassificationRule()).ToList();
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
            _statusSink("设置已保存；Manager 日志设置已立即生效");
        }
        catch (Exception ex) { _statusSink("设置保存失败: " + ex.Message); }
    }

    private void CreateRuleCommands()
    {
        AddLaunchRuleCommand = new RelayCommand(_ => Add(LaunchRules, new EditableRuleRow("Start*.cmd"), x => SelectedLaunchRule = x));
        RemoveLaunchRuleCommand = new RelayCommand(_ => Remove(LaunchRules, SelectedLaunchRule, x => SelectedLaunchRule = x));
        MoveLaunchUpCommand = new RelayCommand(_ => Move(LaunchRules, SelectedLaunchRule, -1));
        MoveLaunchDownCommand = new RelayCommand(_ => Move(LaunchRules, SelectedLaunchRule, +1));
        ResetLaunchRulesCommand = new RelayCommand(_ => ReplaceRules(LaunchRules, RuleDefaults.Launch()));
        AddDragRuleCommand = new RelayCommand(_ => Add(DragRules, new EditableRuleRow("*_Result.zip"), x => SelectedDragRule = x));
        RemoveDragRuleCommand = new RelayCommand(_ => Remove(DragRules, SelectedDragRule, x => SelectedDragRule = x));
        MoveDragUpCommand = new RelayCommand(_ => Move(DragRules, SelectedDragRule, -1));
        MoveDragDownCommand = new RelayCommand(_ => Move(DragRules, SelectedDragRule, +1));
        ResetDragRulesCommand = new RelayCommand(_ => ReplaceRules(DragRules, RuleDefaults.Drag()));
        AddClassificationRuleCommand = new RelayCommand(_ => Add(ClassificationRules,
            new EditableRuleRow("*.zip", "FileName", "我的分类", "70_Custom"), x => SelectedClassificationRule = x));
        RemoveClassificationRuleCommand = new RelayCommand(_ => Remove(ClassificationRules, SelectedClassificationRule,
            x => SelectedClassificationRule = x));
        MoveClassificationUpCommand = new RelayCommand(_ => Move(ClassificationRules, SelectedClassificationRule, -1));
        MoveClassificationDownCommand = new RelayCommand(_ => Move(ClassificationRules, SelectedClassificationRule, +1));
        ResetClassificationRulesCommand = new RelayCommand(_ => ReplaceRules(ClassificationRules, RuleDefaults.Classification()));
        PreviewLaunchCommand = new RelayCommand(_ => PreviewPattern(LaunchRules, "启动文件"));
        PreviewDragCommand = new RelayCommand(_ => PreviewPattern(DragRules, "拖拽文件"));
        PreviewClassificationCommand = new RelayCommand(_ => PreviewClassification());
    }

    private void PreviewPattern(IEnumerable<EditableRuleRow> rows, string label)
    {
        var matched = rows.FirstOrDefault(x => x.Enabled && RuleMatcher.Glob(x.Pattern, PreviewName));
        PreviewSummary = matched is null ? $"{label}：无匹配，系统不会自行选择其他文件"
            : $"{label}：匹配 {matched.Pattern}";
    }

    private void PreviewClassification()
    {
        var artifact = new ManifestService().InspectFilename(PreviewName);
        var configured = ClassificationRules.Select(x => x.ToClassificationRule()).ToList();
        var outcome = new ClassificationService(() => configured).Plan(artifact);
        PreviewSummary = outcome.Category == "Unknown" ? "分类：未匹配，留在收件箱"
            : $"分类：{outcome.Category} → {outcome.RelativeDirectory}";
    }

    private string? ValidateRules()
    {
        foreach (var (label, collection) in new[] { ("启动", LaunchRules), ("拖拽", DragRules), ("分类", ClassificationRules) })
        {
            foreach (var row in collection)
            {
                if (string.IsNullOrWhiteSpace(row.Pattern) || row.Pattern.Length > 256
                    || row.Pattern.IndexOfAny(['/', '\\']) >= 0)
                    return label + "规则的文件名模式不能为空、超过 256 字或包含路径分隔符";
            }
        }
        foreach (var row in ClassificationRules)
        {
            if (!RuleFields.Contains(row.Field)) return "分类匹配字段无效";
            if (string.IsNullOrWhiteSpace(row.Category) || row.Category.IndexOfAny(['/', '\\']) >= 0)
                return "分类名称不能为空或包含路径分隔符";
            if (RuleMatcher.ValidateFolder(row.Folder) is { } problem) return problem;
        }
        return null;
    }

    private static void ReplaceRules(ObservableCollection<EditableRuleRow> target, IEnumerable<FilePatternRule> rules)
    {
        target.Clear();
        foreach (var rule in rules) target.Add(EditableRuleRow.From(rule));
    }
    private static void ReplaceRules(ObservableCollection<EditableRuleRow> target, IEnumerable<ClassificationRule> rules)
    {
        target.Clear();
        foreach (var rule in rules) target.Add(EditableRuleRow.From(rule));
    }
    private static void Add(ObservableCollection<EditableRuleRow> target, EditableRuleRow value, Action<EditableRuleRow> selected)
    {
        target.Add(value);
        selected(value);
    }
    private static void Remove(ObservableCollection<EditableRuleRow> target, EditableRuleRow? value,
        Action<EditableRuleRow?> selected)
    {
        if (value is null || !target.Remove(value)) return;
        selected(target.LastOrDefault());
    }
    private static void Move(ObservableCollection<EditableRuleRow> target, EditableRuleRow? value, int delta)
    {
        if (value is null) return;
        var position = target.IndexOf(value);
        var next = position + delta;
        if (position >= 0 && next >= 0 && next < target.Count) target.Move(position, next);
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
