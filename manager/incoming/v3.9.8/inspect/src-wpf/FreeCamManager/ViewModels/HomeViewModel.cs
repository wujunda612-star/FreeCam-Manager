using System.Collections.ObjectModel;
using FreeCamManager.Core.Models;
using FreeCamManager.Core.Services;

namespace FreeCamManager.ViewModels;

public sealed class HomeRecentItemViewModel(Artifact artifact, ClassificationService classification, FilenameAliasService filenameAliases, AppSettings settings)
{
    public string Name { get; } = artifact.Name;
    public string DisplayName
    {
        get
        {
            var value = settings.HideFreeCamPrefix && Name.StartsWith("FreeCam_", StringComparison.OrdinalIgnoreCase)
                ? Name["FreeCam_".Length..]
                : Name;
            return settings.ShowFilenameAliases ? filenameAliases.Translate(value) : value;
        }
    }
    public object? FileNameToolTip
    {
        get
        {
            var parts = new List<string>();
            if (settings.ShowFilenameAliases) parts.Add($"原始文件名：{Name}");
            if (!string.IsNullOrWhiteSpace(artifact.Notes)) parts.Add($"备注：{artifact.Notes}");
            return parts.Count == 0 ? null : string.Join("\n\n", parts);
        }
    }
    public string Category { get; } = classification.CategoryLabel(artifact);
    public string Status { get; } = LibraryService.DisplayStatus(artifact);
    public string Time { get; } = FormatTime(artifact.ImportedAt, artifact.BuildDate);
    private static string FormatTime(string importedAt, string buildDate)
    {
        if (DateTimeOffset.TryParse(importedAt, out var imported)) return imported.LocalDateTime.ToString("MM-dd HH:mm");
        if (DateTimeOffset.TryParse(buildDate, out var build)) return build.LocalDateTime.ToString("MM-dd HH:mm");
        return "—";
    }
}

public sealed class HomeViewModel : ObservableObject
{
    private readonly Func<IReadOnlyList<Artifact>> _snapshot;
    private readonly ClassificationService _classification;
    private readonly AppSettings _settings;
    private readonly FilenameAliasService _filenameAliases;
    private string _currentStable = "—";
    private int _developmentCount;
    private int _pendingCount;
    private int _unknownCount;

    public HomeViewModel(Func<IReadOnlyList<Artifact>> snapshot, ClassificationService classification, AppSettings settings, FilenameAliasService filenameAliases)
    {
        _snapshot = snapshot; _classification = classification; _settings = settings; _filenameAliases = filenameAliases;
    }

    public string CurrentStable { get => _currentStable; private set => SetProperty(ref _currentStable, value); }
    public int DevelopmentCount { get => _developmentCount; private set => SetProperty(ref _developmentCount, value); }
    public int PendingCount { get => _pendingCount; private set => SetProperty(ref _pendingCount, value); }
    public int UnknownCount { get => _unknownCount; private set => SetProperty(ref _unknownCount, value); }
    public ObservableCollection<HomeRecentItemViewModel> RecentItems { get; } = [];

    public void Refresh()
    {
        var items = _snapshot().Where(a => !_classification.IsManagerArtifact(a)).ToList();
        var stable = items.Where(a => a.Category == "Stable")
            .Select(StableVersionResolver.Resolve)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .OrderByDescending(VersionKey).FirstOrDefault();
        CurrentStable = stable ?? "—";
        DevelopmentCount = items.Count(a => a.Category is "Feature" or "Experiment");
        PendingCount = items.Count(a => (a.Category is "Feature" or "Experiment") && (string.IsNullOrWhiteSpace(a.TestStatus) || a.TestStatus == "待测试"));
        UnknownCount = items.Count(a => a.Category is "Unknown" or "StableCandidate");

        RecentItems.Clear();
        foreach (var a in items.OrderByDescending(a => ParseTime(a.ImportedAt, a.BuildDate)).Take(8))
            RecentItems.Add(new HomeRecentItemViewModel(a, _classification, _filenameAliases, _settings));
    }

    private static DateTimeOffset ParseTime(string imported, string build) => DateTimeOffset.TryParse(imported, out var t) ? t : DateTimeOffset.TryParse(build, out t) ? t : DateTimeOffset.MinValue;
    private static string VersionKey(string v) => string.Join('.', v.TrimStart('R', 'r').Split('.').Select(x => int.TryParse(x, out var n) ? n.ToString("D5") : x));
}
