using System.Collections.ObjectModel;
using System.Windows.Input;
using FreeCamManager.Core.Models;
using FreeCamManager.Core.Services;
using FreeCamManager.Services;

namespace FreeCamManager.ViewModels;

public sealed class StableVersionViewModel
{
    public string Version { get; init; } = "";
    public bool HasRuntime { get; init; }
    public bool HasSource { get; init; }
    public bool HasRepo { get; init; }
    public bool HasSha256 { get; init; }
    public bool IsCandidate { get; init; }
    public bool IsStable { get; init; }
    public string RuntimeText => HasRuntime ? "✓" : "—";
    public string SourceText => HasSource ? "✓" : "—";
    public string RepoText => HasRepo ? "✓" : "—";
    public string Sha256Text => HasSha256 ? "✓" : "—";
    public int Completeness => (HasRuntime ? 1 : 0) + (HasSource ? 1 : 0) + (HasRepo ? 1 : 0) + (HasSha256 ? 1 : 0);
    public string CompletenessText => $"{Completeness}/4";
    public string StateText => IsStable ? "已冻结" : IsCandidate ? "待确认" : "—";
}

public sealed class StableViewModel : ObservableObject
{
    private readonly Func<IReadOnlyList<Artifact>> _snapshot;
    private readonly OrganizerService _organizer;
    private readonly UserDialogService _dialogs;
    private readonly Func<Task> _refreshAll;
    private readonly Action<string> _statusSink;
    private StableVersionViewModel? _selected;

    public StableViewModel(Func<IReadOnlyList<Artifact>> snapshot, OrganizerService organizer, UserDialogService dialogs, Func<Task> refreshAll, Action<string> statusSink)
    {
        _snapshot = snapshot; _organizer = organizer; _dialogs = dialogs; _refreshAll = refreshAll; _statusSink = statusSink;
        ConfirmStableCommand = new AsyncRelayCommand(_ => ConfirmStableAsync(), _ => Selected?.IsCandidate == true);
        SyncBackupCommand = new AsyncRelayCommand(_ => SyncBackupAsync());
        RefreshCommand = new AsyncRelayCommand(_ => _refreshAll());
    }

    public ObservableCollection<StableVersionViewModel> Versions { get; } = [];
    public StableVersionViewModel? Selected
    {
        get => _selected;
        set { if (SetProperty(ref _selected, value)) (ConfirmStableCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged(); }
    }
    public ICommand ConfirmStableCommand { get; }
    public ICommand SyncBackupCommand { get; }
    public ICommand RefreshCommand { get; }

    public void Refresh()
    {
        var selectedVersion = Selected?.Version;
        var groups = _snapshot().Where(a => a.Category is "Stable" or "StableCandidate")
            .GroupBy(StableVersionResolver.Resolve, StringComparer.OrdinalIgnoreCase)
            .Where(g => !string.IsNullOrWhiteSpace(g.Key))
            .OrderByDescending(g => VersionKey(g.Key));
        Versions.Clear();
        foreach (var g in groups)
        {
            var list = g.ToList();
            Versions.Add(new StableVersionViewModel
            {
                Version = g.Key,
                HasRuntime = list.Any(a => Eq(a.ArtifactType, "Runtime")),
                HasSource = list.Any(a => Eq(a.ArtifactType, "Source")),
                HasRepo = list.Any(a => Eq(a.ArtifactType, "Repo")),
                HasSha256 = list.Any(a => Eq(a.ArtifactType, "SHA256")),
                IsCandidate = list.Any(a => a.Category == "StableCandidate"),
                IsStable = list.Any(a => a.Category == "Stable")
            });
        }
        Selected = Versions.FirstOrDefault(x => string.Equals(x.Version, selectedVersion, StringComparison.OrdinalIgnoreCase));
    }

    private async Task ConfirmStableAsync()
    {
        if (Selected is null || !Selected.IsCandidate) return;
        if (!Selected.HasRuntime || !Selected.HasSource)
        {
            _dialogs.Info("冻结材料不足", "确认 Stable（稳定版）至少需要运行包 + 完整源码。Repo.bundle（Git 仓库备份）可选，SHA256（校验文件）会在确认后自动生成。");
            return;
        }
        if (!_dialogs.Confirm("确认稳定版", $"正式把 {Selected.Version} 冻结为 Stable（稳定版）？\n\n冻结后文件默认锁定保护。")) return;
        try
        {
            await _organizer.ConfirmStableAsync(Selected.Version);
            _statusSink($"已冻结稳定版: {Selected.Version}");
            await _refreshAll();
        }
        catch (Exception ex) { _dialogs.Error("冻结失败", ex.Message); }
    }

    private async Task SyncBackupAsync()
    {
        try
        {
            var (verified, copied, failed) = await _organizer.SyncStableBackupAsync();
            _statusSink($"稳定版备份：已验证 {verified}，已复制 {copied}，失败 {failed}");
            if (failed > 0) _dialogs.Info("备份完成", $"已验证 {verified}\n已复制 {copied}\n失败 {failed}");
        }
        catch (Exception ex) { _dialogs.Error("备份失败", ex.Message); }
    }

    private static bool Eq(string a, string b) => string.Equals(a?.Trim(), b, StringComparison.OrdinalIgnoreCase);
    private static string VersionKey(string v) => string.Join('.', v.TrimStart('R', 'r').Split('.').Select(x => int.TryParse(x, out var n) ? n.ToString("D5") : x));
}
