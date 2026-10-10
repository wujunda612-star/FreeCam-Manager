using System.Collections.ObjectModel;
using System.IO;
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
    public bool HasFrozenMarker { get; init; }

    // Once a version has actually been frozen, temporary index/material loss must not
    // silently "unlock" it. A surviving SHA256 or protected Stable row is a durable marker.
    public bool IsFrozen => HasFrozenMarker;
    public bool HasMaterialIssue => IsFrozen && (!HasRuntime || !HasSource || !HasSha256);
    public bool NeedsFreezeCompletion => !IsFrozen && (IsCandidate || IsStable);

    public string RuntimeText => HasRuntime ? "✓" : "—";
    public string SourceText => HasSource ? "✓" : "—";
    public string RepoText => HasRepo ? "✓" : "—";
    public string Sha256Text => HasSha256 ? "✓" : "—";
    public int Completeness => (HasRuntime ? 1 : 0) + (HasSource ? 1 : 0) + (HasRepo ? 1 : 0) + (HasSha256 ? 1 : 0);
    public string CompletenessText => $"{Completeness}/4";
    public string StateText => IsFrozen
        ? (HasMaterialIssue ? "已冻结 · 材料缺失" : "已冻结")
        : NeedsFreezeCompletion ? "待完成" : "—";
}

public sealed class StableViewModel : ObservableObject
{
    private readonly Func<IReadOnlyList<Artifact>> _snapshot;
    private readonly OrganizerService _organizer;
    private readonly UserDialogService _dialogs;
    private readonly Func<Task> _refreshAll;
    private readonly Action<string> _statusSink;
    private StableVersionViewModel? _selected;
    private bool _isBusy;
    private bool _isFreezing;

    public StableViewModel(Func<IReadOnlyList<Artifact>> snapshot, OrganizerService organizer, UserDialogService dialogs, Func<Task> refreshAll, Action<string> statusSink)
    {
        _snapshot = snapshot; _organizer = organizer; _dialogs = dialogs; _refreshAll = refreshAll; _statusSink = statusSink;
        ConfirmStableCommand = new AsyncRelayCommand(_ => ConfirmStableAsync(), _ => !IsBusy && Selected?.NeedsFreezeCompletion == true);
        SyncBackupCommand = new AsyncRelayCommand(_ => SyncBackupAsync(), _ => !IsBusy);
        RefreshCommand = new AsyncRelayCommand(_ => _refreshAll(), _ => !IsBusy);
    }

    public ObservableCollection<StableVersionViewModel> Versions { get; } = [];
    public StableVersionViewModel? Selected
    {
        get => _selected;
        set { if (SetProperty(ref _selected, value)) RaiseCommandStates(); }
    }

    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }
    public string ConfirmButtonText => _isFreezing ? "正在冻结…" : "确认所选为稳定版";

    public ICommand ConfirmStableCommand { get; }
    public ICommand SyncBackupCommand { get; }
    public ICommand RefreshCommand { get; }

    public void Refresh()
    {
        var selectedVersion = Selected?.Version;
        var groups = _snapshot().Where(a => a.Category is "Stable" or "StableCandidate")
            .Where(a => !IsResultLikeArtifact(a))
            .GroupBy(StableVersionResolver.Resolve, StringComparer.OrdinalIgnoreCase)
            .Where(g => !string.IsNullOrWhiteSpace(g.Key))
            .OrderByDescending(g => VersionKey(g.Key));

        Versions.Clear();
        foreach (var g in groups)
        {
            var list = g.ToList();
            var hasRuntime = list.Any(a => Eq(a.ArtifactType, "Runtime") && File.Exists(a.Path));
            var hasSource = list.Any(a => Eq(a.ArtifactType, "Source") && File.Exists(a.Path));
            var hasRepo = list.Any(a => Eq(a.ArtifactType, "Repo") && File.Exists(a.Path));
            var hasSha256 = list.Any(a => Eq(a.ArtifactType, "SHA256") && File.Exists(a.Path));
            var isCandidate = list.Any(a => a.Category == "StableCandidate" && File.Exists(a.Path));
            var isStable = list.Any(a => a.Category == "Stable" && File.Exists(a.Path));

            // Stable state is historical state, not a live completeness calculation.
            // A SHA file is generated only by confirmation; older Stable rows are also
            // recognized through their protected/release/status markers.
            var hasFrozenMarker = hasSha256 || list.Any(a => a.Category == "Stable" &&
                (a.Protected || Eq(a.ReleaseState, "Stable") || Eq(a.Status, "已冻结")));

            Versions.Add(new StableVersionViewModel
            {
                Version = g.Key,
                HasRuntime = hasRuntime,
                HasSource = hasSource,
                HasRepo = hasRepo,
                HasSha256 = hasSha256,
                IsCandidate = isCandidate,
                IsStable = isStable,
                HasFrozenMarker = hasFrozenMarker
            });
        }

        Selected = Versions.FirstOrDefault(x => string.Equals(x.Version, selectedVersion, StringComparison.OrdinalIgnoreCase));
        RaiseCommandStates();
    }

    private async Task ConfirmStableAsync()
    {
        if (Selected is null || !Selected.NeedsFreezeCompletion || IsBusy) return;
        var version = Selected.Version;
        if (!Selected.HasRuntime || !Selected.HasSource)
        {
            _dialogs.Info("冻结材料不足", "确认 Stable（稳定版）至少需要运行包 + 完整源码。Repo.bundle（Git 仓库备份）可选，SHA256（校验文件）会在确认后自动生成。");
            return;
        }
        if (!_dialogs.Confirm("确认稳定版", $"正式把 {version} 冻结为 Stable（稳定版）？\n\n本地冻结完成后会自动同步并校验备份；耗时任务在后台执行，界面不会被阻塞。")) return;

        SetBusy(true, freezing: true);
        try
        {
            try
            {
                _statusSink($"正在后台冻结稳定版: {version}…");
                // Organizer includes hashing, file moves and SQLite persistence. Keep all
                // synchronous portions of those operations off the WPF dispatcher thread.
                await Task.Run(() => _organizer.ConfirmStableAsync(version));
                _statusSink($"本地稳定版已冻结: {version}；正在后台同步并校验备份…");
                await _refreshAll();
            }
            catch (Exception ex)
            {
                _dialogs.Error("冻结失败", ex.Message);
                return;
            }

            try
            {
                var result = await Task.Run(() => _organizer.SyncStableBackupAsync(version));
                var (verified, copied, failed) = result;
                if (failed > 0)
                {
                    _statusSink($"稳定版已冻结；备份已验证 {verified}，已复制 {copied}，失败 {failed}");
                    _dialogs.Info("稳定版已冻结，但备份未完全通过", $"版本：{version}\n已验证 {verified}\n已复制 {copied}\n失败 {failed}\n\n可稍后使用“同步 / 校验备份”重试。");
                }
                else
                {
                    _statusSink($"稳定版已冻结并完成备份：{version} · 已验证 {verified}，已复制 {copied}");
                }
            }
            catch (Exception ex)
            {
                _statusSink($"稳定版已冻结，但备份同步失败: {version}");
                _dialogs.Info("稳定版已冻结，但备份同步失败", ex.Message + "\n\n本地 10_Stable 与 SHA256 已保留，可稍后重新同步。");
            }
        }
        finally
        {
            SetBusy(false);
            await _refreshAll();
        }
    }

    private async Task SyncBackupAsync()
    {
        if (IsBusy) return;
        SetBusy(true);
        try
        {
            var result = await Task.Run(() => _organizer.SyncStableBackupAsync());
            var (verified, copied, failed) = result;
            _statusSink($"稳定版备份：已验证 {verified}，已复制 {copied}，失败 {failed}");
            if (failed > 0) _dialogs.Info("备份完成", $"已验证 {verified}\n已复制 {copied}\n失败 {failed}");
        }
        catch (Exception ex) { _dialogs.Error("备份失败", ex.Message); }
        finally
        {
            SetBusy(false);
            await _refreshAll();
        }
    }

    private void SetBusy(bool value, bool freezing = false)
    {
        IsBusy = value;
        _isFreezing = value && freezing;
        OnPropertyChanged(nameof(ConfirmButtonText));
        RaiseCommandStates();
    }

    private void RaiseCommandStates()
    {
        (ConfirmStableCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (SyncBackupCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (RefreshCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
    }

    private static bool IsResultLikeArtifact(Artifact artifact)
    {
        if (Eq(artifact.ArtifactType, "Result")) return true;
        var name = !string.IsNullOrWhiteSpace(artifact.Name) ? artifact.Name : Path.GetFileName(artifact.Path);
        var stem = Path.GetFileNameWithoutExtension(name ?? "");
        var marker = stem.LastIndexOf("_Result", StringComparison.OrdinalIgnoreCase);
        if (marker < 0) return false;
        var tail = stem[(marker + "_Result".Length)..].Trim();
        if (tail.Length == 0) return true;
        return tail.Length >= 3 && tail[0] == '(' && tail[^1] == ')'
            && int.TryParse(tail[1..^1], out _);
    }

    private static bool Eq(string? a, string b) => string.Equals(a?.Trim(), b, StringComparison.OrdinalIgnoreCase);
    private static string VersionKey(string v) => string.Join('.', v.TrimStart('R', 'r').Split('.').Select(x => int.TryParse(x, out var n) ? n.ToString("D5") : x));
}
