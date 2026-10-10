using System.Collections.ObjectModel;
using System.IO;
using System.Diagnostics;
using System.Windows.Input;
using FreeCamManager.Core.Models;
using FreeCamManager.Core.Services;
using FreeCamManager.Services;

namespace FreeCamManager.ViewModels;

/// <summary>User-created categories do not replace the existing Development/Stable pages.</summary>
public sealed class CustomCategoryViewModel : ObservableObject
{
    private readonly Func<IReadOnlyList<Artifact>> _snapshot;
    private readonly AppSettings _settings;
    private readonly SettingsService _settingsService;
    private readonly LibraryService _library;
    private readonly OrganizerService _organizer;
    private readonly ClassificationService _classification;
    private readonly UserDialogService _dialogs;
    private readonly TestWorkspaceService _workspace;
    private readonly TestResultService _results;
    private readonly FilenameAliasService _aliases;
    private readonly Func<Task> _refresh;
    private readonly Action<string> _status;
    private string _selectedCategory = "全部";
    private string _searchText = "";
    private ArtifactRowViewModel? _selectedRow;

    public CustomCategoryViewModel(Func<IReadOnlyList<Artifact>> snapshot, AppSettings settings,
        SettingsService settingsService, LibraryService library, OrganizerService organizer,
        ClassificationService classification, UserDialogService dialogs, TestWorkspaceService workspace,
        TestResultService results, FilenameAliasService aliases, Func<Task> refresh, Action<string> status)
    {
        _snapshot = snapshot; _settings = settings; _settingsService = settingsService;
        _library = library; _organizer = organizer; _classification = classification;
        _dialogs = dialogs; _workspace = workspace; _results = results; _aliases = aliases;
        _refresh = refresh; _status = status;
        RefreshCommand = new AsyncRelayCommand(_ => _refresh());
        OpenFolderCommand = new RelayCommand(_ => OpenFolder());
    }

    public ObservableCollection<string> Categories { get; } = [];
    public ObservableCollection<ArtifactRowViewModel> Items { get; } = [];
    public string SelectedCategory
    {
        get => _selectedCategory;
        set { if (SetProperty(ref _selectedCategory, value)) RefreshItems(); }
    }
    public string SearchText
    {
        get => _searchText;
        set { if (SetProperty(ref _searchText, value)) RefreshItems(); }
    }
    public ArtifactRowViewModel? SelectedRow
    {
        get => _selectedRow;
        set => SetProperty(ref _selectedRow, value);
    }
    public ICommand RefreshCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public void Refresh()
    {
        var names = (_settings.ClassificationRules ?? RuleDefaults.Classification())
            .Select(x => x.Category)
            .Concat(_snapshot().Select(x => x.Category))
            .Where(x => !RuleMatcher.IsBuiltInCategory(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        Categories.Clear();
        Categories.Add("全部");
        foreach (var name in names) Categories.Add(name);
        if (!Categories.Contains(_selectedCategory))
        {
            _selectedCategory = "全部";
            OnPropertyChanged(nameof(SelectedCategory));
        }
        RefreshItems();
    }

    private void RefreshItems()
    {
        var previous = SelectedRow?.Path;
        var query = _snapshot()
            .Where(x => !RuleMatcher.IsBuiltInCategory(x.Category))
            .Where(x => _selectedCategory == "全部"
                || string.Equals(x.Category, _selectedCategory, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(_searchText))
            query = query.Where(x => x.Name.Contains(_searchText.Trim(), StringComparison.OrdinalIgnoreCase)
                || x.Category.Contains(_searchText.Trim(), StringComparison.OrdinalIgnoreCase)
                || x.Notes.Contains(_searchText.Trim(), StringComparison.OrdinalIgnoreCase));
        Items.Clear();
        foreach (var artifact in query.OrderByDescending(x => x.ImportedAt))
        {
            var row = new ArtifactRowViewModel(artifact, _library, _organizer, _classification, _dialogs,
                _workspace, _results, () => _settings.RootDir, () => SettingsService.TestingRoot(_settings),
                () => SettingsService.ResultRoot(_settings), _status, _refresh,
                () => _settings.HideFreeCamPrefix, () => _settings.DiscardAutoDeleteDays,
                _aliases.Translate, () => _settings.ShowFilenameAliases, () => _settings.ShowFeatureAliases,
                () => _settings.ShowStageAliases)
            {
                Configuration = _settings,
                SettingsPersistence = _settingsService
            };
            Items.Add(row);
        }
        SelectedRow = Items.FirstOrDefault(x => x.Path.Equals(previous, StringComparison.OrdinalIgnoreCase));
    }

    private void OpenFolder()
    {
        var root = _settings.RootDir;
        if (!Directory.Exists(root)) return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{root}\"") { UseShellExecute = true });
    }
}
