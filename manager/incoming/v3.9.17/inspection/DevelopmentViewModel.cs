using System.IO;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using FreeCamManager.Core.Models;
using FreeCamManager.Core.Services;
using FreeCamManager.Services;

namespace FreeCamManager.ViewModels;

public sealed class DevelopmentViewModel : ObservableObject
{
    private readonly Func<IReadOnlyList<Artifact>> _snapshot;
    private readonly LibraryService _library;
    private readonly OrganizerService _organizer;
    private readonly ClassificationService _classification;
    private readonly UserDialogService _dialogs;
    private readonly TestWorkspaceService _workspace;
    private readonly TestResultService _results;
    private readonly Func<Task> _refreshAll;
    private readonly Func<Task<bool>> _scanInboxNow;
    private readonly Func<string> _root;
    private readonly Func<string> _testingRoot;
    private readonly Func<string> _resultRoot;
    private readonly Action<string> _statusSink;
    private readonly AppSettings _settings;
    private readonly SettingsService _settingsService;
    private readonly FilenameAliasService _filenameAliases;
    private DevelopmentFilter _filter = DevelopmentFilter.All;
    private string _searchText = "";
    private string _sortMode = "最新优先";
    private ArtifactRowViewModel? _selectedRow;
    private double _fileColumnWidth;
    private double _featureColumnWidth;
    private double _stageColumnWidth;
    private bool _fileColumnCustomized;

    public DevelopmentViewModel(
        Func<IReadOnlyList<Artifact>> snapshot,
        LibraryService library,
        OrganizerService organizer,
        ClassificationService classification,
        UserDialogService dialogs,
        TestWorkspaceService workspace,
        TestResultService results,
        Func<Task> refreshAll,
        Func<string> root,
        Func<string> testingRoot,
        Func<string> resultRoot,
        Action<string> statusSink,
        AppSettings settings,
        SettingsService settingsService,
        FilenameAliasService filenameAliases,
        Func<Task<bool>> scanInboxNow)
    {
        _snapshot = snapshot; _library = library; _organizer = organizer; _classification = classification;
        _dialogs = dialogs; _workspace = workspace; _results = results; _refreshAll = refreshAll;
        _root = root; _testingRoot = testingRoot; _resultRoot = resultRoot; _statusSink = statusSink;
        _settings = settings; _settingsService = settingsService; _filenameAliases = filenameAliases; _scanInboxNow = scanInboxNow;
        _fileColumnCustomized = settings.DevelopmentFileColumnWidth >= 180;
        _fileColumnWidth = _fileColumnCustomized ? settings.DevelopmentFileColumnWidth : 180;
        _featureColumnWidth = Math.Max(60, settings.DevelopmentFeatureColumnWidth);
        _stageColumnWidth = Math.Max(52, settings.DevelopmentStageColumnWidth);
        SetFilterCommand = new RelayCommand(p => SetFilter(p?.ToString() ?? "全部"));
        RefreshCommand = new AsyncRelayCommand(_ => ManualRefreshAsync());
        OpenTestingCommand = new RelayCommand(_ => OpenTesting());
    }

    public ObservableCollection<ArtifactRowViewModel> Items { get; } = [];
    public IReadOnlyList<string> SortOptions { get; } = ["最新优先", "最旧优先", "星级优先"];
    public ICommand SetFilterCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand OpenTestingCommand { get; }

    public ArtifactRowViewModel? SelectedRow { get => _selectedRow; set => SetProperty(ref _selectedRow, value); }
    public string SearchText { get => _searchText; set { if (SetProperty(ref _searchText, value)) Refresh(); } }
    public string SortMode { get => _sortMode; set { if (SetProperty(ref _sortMode, value)) Refresh(); } }
    public double FileColumnWidth { get => _fileColumnWidth; private set => SetProperty(ref _fileColumnWidth, Math.Max(180, value)); }
    public double FeatureColumnWidth { get => _featureColumnWidth; private set => SetProperty(ref _featureColumnWidth, Math.Max(60, value)); }
    public double StageColumnWidth { get => _stageColumnWidth; private set => SetProperty(ref _stageColumnWidth, Math.Max(52, value)); }

    public int AllCount { get; private set; }
    public int FeatureCount { get; private set; }
    public int ExperimentCount { get; private set; }
    public int TestCount { get; private set; }
    public string AllFilterText => $"全部  {AllCount}";
    public string FeatureFilterText => $"正式功能  {FeatureCount}";
    public string ExperimentFilterText => $"实验  {ExperimentCount}";
    public string TestFilterText => $"测试  {TestCount}";
    public bool IsAllSelected => _filter == DevelopmentFilter.All;
    public bool IsFeatureSelected => _filter == DevelopmentFilter.Feature;
    public bool IsExperimentSelected => _filter == DevelopmentFilter.Experiment;
    public bool IsTestSelected => _filter == DevelopmentFilter.Test;

    public void Refresh()
    {
        var selectedPath = SelectedRow?.Path;
        var source = _snapshot().Where(a => !_classification.IsManagerArtifact(a) && a.Category is "Feature" or "Experiment").ToList();
        AllCount = source.Count;
        FeatureCount = source.Count(a => _classification.MatchesDevelopmentFilter(a, DevelopmentFilter.Feature));
        ExperimentCount = source.Count(a => _classification.MatchesDevelopmentFilter(a, DevelopmentFilter.Experiment));
        TestCount = source.Count(a => _classification.MatchesDevelopmentFilter(a, DevelopmentFilter.Test));

        IEnumerable<Artifact> query = source.Where(a => _classification.MatchesDevelopmentFilter(a, _filter));
        var q = SearchText.Trim();
        if (q.Length > 0) query = query.Where(a => Match(a, q));
        query = SortMode switch
        {
            "最旧优先" => query.OrderBy(a => ParseTime(a.ImportedAt, a.BuildDate)),
            "星级优先" => query.OrderByDescending(a => a.Rating).ThenByDescending(a => ParseTime(a.ImportedAt, a.BuildDate)),
            _ => query.OrderByDescending(a => ParseTime(a.ImportedAt, a.BuildDate))
        };

        // Reconcile rows in place. Clearing the collection destroys the active
        // CompactPickerControl, so a background scan used to close/reset an open
        // manual-conclusion picker while the user was choosing a value.
        var desired = query.ToList();
        for (var i = 0; i < desired.Count; i++)
        {
            var artifact = desired[i];
            var existing = Items.FirstOrDefault(x => string.Equals(x.Path, artifact.Path, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                Items.Insert(i, CreateRow(artifact));
                continue;
            }

            existing.RefreshFromArtifact(artifact);
            var currentIndex = Items.IndexOf(existing);
            if (currentIndex != i) Items.Move(currentIndex, i);
        }
        while (Items.Count > desired.Count) Items.RemoveAt(Items.Count - 1);

        SelectedRow = Items.FirstOrDefault(x => string.Equals(x.Path, selectedPath, StringComparison.OrdinalIgnoreCase));
        RaiseCounts();
    }

    public void SetAutoFileColumnWidth(double availableWidth)
    {
        if (_fileColumnCustomized || availableWidth <= 0) return;
        const double fixedColumns = 94 + 86 + 108 + 24 + 82;
        FileColumnWidth = Math.Max(180, availableWidth - fixedColumns - FeatureColumnWidth - StageColumnWidth);
    }

    public void AdjustFileColumnWidth(double delta)
    {
        _fileColumnCustomized = true;
        FileColumnWidth += delta;
        _settings.DevelopmentFileColumnWidth = FileColumnWidth;
    }

    public void AdjustFeatureColumnWidth(double delta)
    {
        FeatureColumnWidth += delta;
        _settings.DevelopmentFeatureColumnWidth = FeatureColumnWidth;
    }

    public void AdjustStageColumnWidth(double delta)
    {
        StageColumnWidth += delta;
        _settings.DevelopmentStageColumnWidth = StageColumnWidth;
    }

    public async Task PersistColumnWidthsAsync()
    {
        _settings.DevelopmentFileColumnWidth = _fileColumnCustomized ? FileColumnWidth : 0;
        _settings.DevelopmentFeatureColumnWidth = FeatureColumnWidth;
        _settings.DevelopmentStageColumnWidth = StageColumnWidth;
        try { await _settingsService.SaveAsync(AppPaths.SettingsFile, _settings); }
        catch (Exception ex) { _statusSink("列宽保存失败: " + ex.Message); }
    }

    private ArtifactRowViewModel CreateRow(Artifact a)
    {
        var row = new ArtifactRowViewModel(a, _library, _organizer, _classification, _dialogs, _workspace, _results,
            _root, _testingRoot, _resultRoot, _statusSink, _refreshAll,
            () => _settings.HideFreeCamPrefix, () => _settings.DiscardAutoDeleteDays,
            _filenameAliases.Translate, () => _settings.ShowFilenameAliases,
            () => _settings.ShowFeatureAliases, () => _settings.ShowStageAliases);
        row.Configuration = _settings;
        row.SettingsPersistence = _settingsService;
        return row;
    }

    private async Task ManualRefreshAsync()
    {
        try
        {
            _statusSink("正在重新整理收件箱...");
            var changed = await _scanInboxNow();
            await _refreshAll();
            _statusSink(changed ? "重新整理完成，列表已更新" : "重新整理完成，没有发现可处理的新内容");
        }
        catch (Exception ex)
        {
            _statusSink("重新整理收件箱失败: " + ex.Message);
        }
    }

    private void SetFilter(string label)
    {
        _filter = label switch { "正式功能" => DevelopmentFilter.Feature, "实验" => DevelopmentFilter.Experiment, "测试" => DevelopmentFilter.Test, _ => DevelopmentFilter.All };
        OnPropertyChanged(nameof(IsAllSelected)); OnPropertyChanged(nameof(IsFeatureSelected)); OnPropertyChanged(nameof(IsExperimentSelected)); OnPropertyChanged(nameof(IsTestSelected));
        Refresh();
    }

    private void RaiseCounts()
    {
        OnPropertyChanged(nameof(AllCount)); OnPropertyChanged(nameof(FeatureCount)); OnPropertyChanged(nameof(ExperimentCount)); OnPropertyChanged(nameof(TestCount));
        OnPropertyChanged(nameof(AllFilterText)); OnPropertyChanged(nameof(FeatureFilterText)); OnPropertyChanged(nameof(ExperimentFilterText)); OnPropertyChanged(nameof(TestFilterText));
    }

    private void OpenTesting()
    {
        try
        {
            var path = _testingRoot(); Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { _dialogs.Error("打开失败", ex.Message); }
    }

    private bool Match(Artifact a, string q)
    {
        var translated = _filenameAliases.Translate(a.Name);
        var translatedFeature = _filenameAliases.Translate(a.Feature);
        var translatedStage = _filenameAliases.Translate(a.Stage);
        var fields = new[] { a.Name, translated, a.Base, a.Feature, translatedFeature, a.Stage, translatedStage, a.BuildId, a.Commit, a.ManualStatus, a.TestStatus, a.Notes };
        return fields.Any(x => !string.IsNullOrWhiteSpace(x) && x.Contains(q, StringComparison.OrdinalIgnoreCase));
    }

    private static DateTimeOffset ParseTime(string imported, string build) => DateTimeOffset.TryParse(imported, out var t) ? t : DateTimeOffset.TryParse(build, out t) ? t : DateTimeOffset.MinValue;
}
