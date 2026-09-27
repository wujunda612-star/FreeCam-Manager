using System.Collections.ObjectModel;
using System.Windows.Input;
using FreeCamManager.Core.Models;
using FreeCamManager.Core.Services;
using FreeCamManager.Services;

namespace FreeCamManager.ViewModels;

public sealed class CustomCategoryViewModel : ObservableObject
{
    private readonly Func<IReadOnlyList<Artifact>> _snapshot;
    private readonly AppSettings _settings;
    private readonly LibraryService _library;
    private readonly OrganizerService _organizer;
    private readonly ClassificationService _classification;
    private readonly UserDialogService _dialogs;
    private readonly TestWorkspaceService _workspace;
    private readonly TestResultService _results;
    private readonly FilenameAliasService _aliases;
    private readonly Action<string> _status;
    private readonly Func<Task> _refreshAll;
    private string? _selectedCategory;
    private string _searchText = "";
    private ArtifactRowViewModel? _selectedRow;

    public CustomCategoryViewModel(Func<IReadOnlyList<Artifact>> snapshot, AppSettings settings,
        LibraryService library, OrganizerService organizer, ClassificationService classification,
        UserDialogService dialogs, TestWorkspaceService workspace, TestResultService results,
        FilenameAliasService aliases, Action<string> status, Func<Task> refreshAll)
    {
        _snapshot = snapshot; _settings = settings; _library = library; _organizer = organizer;
        _classification = classification; _dialogs = dialogs; _workspace = workspace;
        _results = results; _aliases = aliases; _status = status; _refreshAll = refreshAll;
        RefreshCommand = new AsyncRelayCommand(_ => _refreshAll());
    }

    public ObservableCollection<string> Categories { get; } = [];
    public ObservableCollection<ArtifactRowViewModel> Items { get; } = [];
    public ICommand RefreshCommand { get; }
    public string? SelectedCategory
    {
        get => _selectedCategory;
        set { if (SetProperty(ref _selectedCategory, value)) RefreshRows(); }
    }
    public string SearchText { get => _searchText; set { if (SetProperty(ref _searchText, value)) RefreshRows(); } }
    public ArtifactRowViewModel? SelectedRow { get => _selectedRow; set => SetProperty(ref _selectedRow, value); }

    public void Refresh()
    {
        var names = (_settings.ClassificationRules ?? [])
            .Where(x => x.Enabled)
            .Select(x => FileRuleEngine.PersistedCategory(x.Category))
            .Where(FileRuleEngine.IsCustomCategory)
            .Select(x => x["Custom:".Length..])
            .Concat(_snapshot().Where(x => FileRuleEngine.IsCustomCategory(x.Category))
                .Select(x => x.Category["Custom:".Length..]))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
        Categories.Clear();
        foreach (var name in names) Categories.Add(name);
        if (SelectedCategory is null || !Categories.Contains(SelectedCategory))
            SelectedCategory = Categories.FirstOrDefault();
        RefreshRows();
    }

    private void RefreshRows()
    {
        var previous = SelectedRow?.Path;
        Items.Clear();
        if (!string.IsNullOrWhiteSpace(SelectedCategory))
        {
            foreach (var item in _snapshot()
                .Where(a => string.Equals(a.Category, "Custom:" + SelectedCategory, StringComparison.OrdinalIgnoreCase))
                .Where(a => string.IsNullOrWhiteSpace(SearchText) ||
                    a.Name.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase) ||
                    a.Notes.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(a => DateTimeOffset.TryParse(a.ImportedAt, out var time) ? time : DateTimeOffset.MinValue))
            {
                Items.Add(new ArtifactRowViewModel(item, _library, _organizer, _classification,
                    _dialogs, _workspace, _results, () => _settings.RootDir,
                    () => SettingsService.TestingRoot(_settings), () => SettingsService.ResultRoot(_settings),
                    _status, _refreshAll, () => _settings.HideFreeCamPrefix,
                    () => _settings.DiscardAutoDeleteDays, _aliases.Translate,
                    () => _settings.ShowFilenameAliases, () => _settings.ShowFeatureAliases,
                    () => _settings.ShowStageAliases, () => _settings));
            }
        }
        SelectedRow = Items.FirstOrDefault(x => string.Equals(x.Path, previous, StringComparison.OrdinalIgnoreCase));
    }
}
