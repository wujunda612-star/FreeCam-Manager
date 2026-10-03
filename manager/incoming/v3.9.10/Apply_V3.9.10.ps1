param([Parameter(Mandatory=$true)][string]$SourceRoot)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$Utf8 = New-Object System.Text.UTF8Encoding($true)
$nl = [Environment]::NewLine
function Write-Utf8([string]$p,[string]$t){ [IO.File]::WriteAllText($p,$t,$Utf8) }
function Replace-One([string]$p,[string]$old,[string]$new,[string]$label){
 $t=[IO.File]::ReadAllText($p); $i=$t.IndexOf($old,[StringComparison]::Ordinal)
 if($i -lt 0){ throw "$label: marker not found in $p" }
 if($t.IndexOf($old,$i+$old.Length,[StringComparison]::Ordinal) -ge 0){ throw "$label: marker not unique in $p" }
 Write-Utf8 $p ($t.Substring(0,$i)+$new+$t.Substring($i+$old.Length))
}
$Src = if(Test-Path (Join-Path $SourceRoot 'src-wpf\FreeCamManager\FreeCamManager.csproj')) { Join-Path $SourceRoot 'src-wpf' } elseif(Test-Path (Join-Path $SourceRoot 'FreeCamManager\FreeCamManager.csproj')) { $SourceRoot } else { throw "src-wpf not found under $SourceRoot" }

# 1) Scoped active-test evidence monitor. This replaces the removed global high-frequency refresh.
$row = Join-Path $Src 'FreeCamManager\ViewModels\ArtifactRowViewModel.cs'
Replace-One $row '    private bool _isProtected;' ('    private bool _isProtected;' + $nl + '    private int _testMonitorGeneration;') 'add scoped test monitor generation'
Replace-One $row '            Launch(launchPath);' ('            Launch(launchPath);' + $nl + '            var monitorGeneration = ++_testMonitorGeneration;' + $nl + '            _ = MonitorTestCompletionAsync(prepared.TestingPath, monitorGeneration);') 'start scoped test monitor after launch'
$monitor=@'
    private async Task MonitorTestCompletionAsync(string testingPath, int generation)
    {
        var deadline = DateTimeOffset.UtcNow.AddHours(6);
        var lastEvidence = "";
        while (generation == _testMonitorGeneration && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(2));
            if (generation != _testMonitorGeneration || !Directory.Exists(testingPath)) return;
            try
            {
                var current = _library.ByPath(Path) ?? _artifact;
                var evidence = await _results.FindEvidenceAsync(testingPath, current);
                if (evidence is null) continue;
                var evidencePath = evidence.Path;
                if (!string.Equals(lastEvidence, evidencePath, StringComparison.OrdinalIgnoreCase))
                {
                    var testedAt = evidence.LastWriteTimeUtc.ToString("O");
                    _library.SetTestEvidence(Path, evidencePath, _root(), testedAt);
                    await _library.SaveAsync();
                    _artifact.ResultPath = evidencePath;
                    _artifact.ResultRelativePath = PathRebaseService.TryMakeRelative(_root(), evidencePath);
                    _artifact.LastTestedAt = testedAt;
                    _artifact.TestStatus = "已测试";
                    _artifact.Status = "已测试";
                    OnPropertyChanged(nameof(ResultPath));
                    OnPropertyChanged(nameof(HasResult));
                    OnPropertyChanged(nameof(ResultHint));
                    OnPropertyChanged(nameof(TestStatus));
                    lastEvidence = evidencePath;
                    _statusSink("已检测到测试结果: " + Name);
                    await _refreshAll();
                }

                if (TestResultService.IsPreferredResultZip(evidencePath)
                    && !TestResultService.IsReferenceEvidencePath(evidencePath, testingPath)) return;
            }
            catch
            {
                // Best-effort scoped monitor. Manual "重新整理收件箱" / refresh can reconcile later.
            }
        }
    }

'@
Replace-One $row '    private async Task<string> SelectLaunchFileAsync(string testing)' ($monitor + '    private async Task<string> SelectLaunchFileAsync(string testing)') 'insert scoped test monitor implementation'

# 2) Make the existing manual scan action explicit: "重新整理收件箱".
$devXaml = Join-Path $Src 'FreeCamManager\Views\DevelopmentView.xaml'
Replace-One $devXaml '<TextBlock Text="刷新" Margin="7,0,0,0"/>' '<TextBlock Text="重新整理收件箱" Margin="7,0,0,0"/>' 'rename manual inbox action'
$devVm = Join-Path $Src 'FreeCamManager\ViewModels\DevelopmentViewModel.cs'
Replace-One $devVm '_statusSink("正在扫描收件箱...");' '_statusSink("正在重新整理收件箱...");' 'manual inbox start text'
Replace-One $devVm '_statusSink(changed ? "扫描完成，列表已更新" : "扫描完成，没有发现可处理的新内容");' '_statusSink(changed ? "整理完成，列表已更新" : "整理完成，没有发现可处理的新内容");' 'manual inbox done text'
Replace-One $devVm '_statusSink("立即扫描失败: " + ex.Message);' '_statusSink("重新整理收件箱失败: " + ex.Message);' 'manual inbox error text'

# 3) Final version metadata.
$csproj = Join-Path $Src 'FreeCamManager\FreeCamManager.csproj'
Replace-One $csproj '<Version>3.9.9</Version>' '<Version>3.9.10</Version>' 'version 3.9.10'
$manifestCandidates = @((Join-Path $SourceRoot 'BUILD_MANIFEST.json'), (Join-Path $Src 'BUILD_MANIFEST.json')) | Where-Object { Test-Path -LiteralPath $_ }
foreach($manifest in $manifestCandidates){
 $m=Get-Content -Raw -LiteralPath $manifest | ConvertFrom-Json
 $m.Version='V3.9.10'; $m.BuildName='FreeCam_Manager_V3.9.10'; $m.Base='FreeCam_Manager_V3.9.8'
 $m.Branch='release/manager-v3.9.10'; $m.Feature='Stable recovery + inbox performance + RC recognition + rule UI cleanup + scoped test status monitor + manual inbox reorganize'
 $m.BuildType='Stable'; $m.Stage='Release'; $m.BuildId='MANAGER-V3910-20261004'; $m.Commit='PUBLISH-WORKFLOW'; $m.ReleaseState='Stable'; $m.RemotePublish=$true
 $m | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $manifest -Encoding UTF8
}

# 4) Regression contracts for the two post-test fixes.
$program = Join-Path $Src 'FreeCamManager.Tests\Program.cs'
$runAnchor='        await Run("V3.9.9 rule editor localization and dark UI contract", V399RuleEditorUiContract);'
$runs=@'
        await Run("V3.9.10 active test uses scoped result monitor", V3910ScopedTestResultMonitorContract);
        await Run("V3.9.10 manual inbox reorganization keeps automatic scan conservative", V3910ManualInboxReorganizeContract);
'@
$t=[IO.File]::ReadAllText($program)
if(-not $t.Contains('V3910ScopedTestResultMonitorContract')){
 if(-not $t.Contains($runAnchor)){ throw 'V3.9.10 test run anchor missing' }
 $t=$t.Replace($runAnchor,$runAnchor+"`r`n"+$runs.TrimEnd("`r","`n"))
 $methodAnchor='    private static string TempDir()'
 $methods=@'
    private static Task V3910ScopedTestResultMonitorContract()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "FreeCamManager"));
        var row = File.ReadAllText(Path.Combine(root, "ViewModels", "ArtifactRowViewModel.cs"));
        var core = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "FreeCamManager.Core"));
        var watcher = File.ReadAllText(Path.Combine(core, "Services", "InboxWatcherService.cs"));
        Assert(row.Contains("MonitorTestCompletionAsync") && row.Contains("_ = MonitorTestCompletionAsync(prepared.TestingPath")
            && row.Contains("_results.FindEvidenceAsync(testingPath, current)") && row.Contains("IsPreferredResultZip(evidencePath)"),
            "active test must monitor only its own testing workspace and upgrade to preferred Result ZIP");
        Assert(watcher.Contains("if (manual && await _testRefresh.RefreshAsync"),
            "automatic inbox scan must keep full-library test refresh disabled");
        return Task.CompletedTask;
    }

    private static Task V3910ManualInboxReorganizeContract()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "FreeCamManager"));
        var view = File.ReadAllText(Path.Combine(root, "Views", "DevelopmentView.xaml"));
        var vm = File.ReadAllText(Path.Combine(root, "ViewModels", "DevelopmentViewModel.cs"));
        Assert(view.Contains("重新整理收件箱"), "manual inbox action must have an explicit reorganization label");
        Assert(vm.Contains("正在重新整理收件箱") && vm.Contains("整理完成") && vm.Contains("重新整理收件箱失败"),
            "manual inbox reorganization status messages missing");
        return Task.CompletedTask;
    }

'@
 if(-not $t.Contains($methodAnchor)){ throw 'V3.9.10 test method anchor missing' }
 $t=$t.Replace($methodAnchor,$methods+$methodAnchor)
 Write-Utf8 $program $t
}

# Final static guards.
$rowText=[IO.File]::ReadAllText($row); $watcher=Join-Path $Src 'FreeCamManager.Core\Services\InboxWatcherService.cs'; $watchText=[IO.File]::ReadAllText($watcher)
if(-not $rowText.Contains('MonitorTestCompletionAsync') -or -not $rowText.Contains('FindEvidenceAsync(testingPath, current)')){ throw 'scoped result monitor patch missing' }
if(-not $watchText.Contains('if (manual && await _testRefresh.RefreshAsync')){ throw 'automatic scan performance isolation regressed' }
if(-not ([IO.File]::ReadAllText($devXaml)).Contains('重新整理收件箱')){ throw 'manual inbox label missing' }
Write-Host '[V3.9.10] patch PASS'
