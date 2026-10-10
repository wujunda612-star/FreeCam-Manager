param([Parameter(Mandatory=$true)][string]$SourceRoot)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$Utf8 = New-Object System.Text.UTF8Encoding($true)
$src = Join-Path $SourceRoot 'src-wpf'
$staged = Join-Path $env:GITHUB_WORKSPACE 'manager\incoming\v3.9.17\inspection'
$mapping = @{
    'MainWindowViewModel.cs' = 'FreeCamManager\ViewModels\MainWindowViewModel.cs'
    'ArtifactRowViewModel.cs' = 'FreeCamManager\ViewModels\ArtifactRowViewModel.cs'
    'DevelopmentViewModel.cs' = 'FreeCamManager\ViewModels\DevelopmentViewModel.cs'
    'HistoryViewModel.cs' = 'FreeCamManager\ViewModels\HistoryViewModel.cs'
    'DevelopmentView.xaml' = 'FreeCamManager\Views\DevelopmentView.xaml'
    'HistoryView.xaml' = 'FreeCamManager\Views\HistoryView.xaml'
    'App.xaml.cs' = 'FreeCamManager\App.xaml.cs'
    'OrganizerService.cs' = 'FreeCamManager.Core\Services\OrganizerService.cs'
    'InboxWatcherService.cs' = 'FreeCamManager.Core\Services\InboxWatcherService.cs'
    'SqliteMigrationStore.cs' = 'FreeCamManager.SQLiteMigration.Core\SqliteMigrationStore.cs'
    'ProductionManagerSqliteLibrarySession.cs' = 'FreeCamManager.SQLiteMigration.Core\ProductionManagerSqliteLibrarySession.cs'
    'FreeCamManager.Tests_Program.cs' = 'FreeCamManager.Tests\Program.cs'
    'FreeCamManager.SQLiteMigration.Tests_Program.cs' = 'FreeCamManager.SQLiteMigration.Tests\Program.cs'
}
foreach($entry in $mapping.GetEnumerator()) {
    $from = Join-Path $staged $entry.Key
    $to = Join-Path $src $entry.Value
    if (-not (Test-Path -LiteralPath $from) -or -not (Test-Path -LiteralPath $to)) {
        throw "Missing V3.9.17 staged code: $from -> $to"
    }
    Copy-Item -LiteralPath $from -Destination $to -Force
}
$project = Join-Path $src 'FreeCamManager\FreeCamManager.csproj'
$p = [IO.File]::ReadAllText($project)
if (-not $p.Contains('<Version>3.9.16</Version>')) { throw 'Expected source baseline 3.9.16' }
[IO.File]::WriteAllText($project,$p.Replace('<Version>3.9.16</Version>','<Version>3.9.17</Version>'),$Utf8)
foreach($mpath in @((Join-Path $SourceRoot 'BUILD_MANIFEST.json'), (Join-Path $src 'BUILD_MANIFEST.json'))) {
    if (-not (Test-Path -LiteralPath $mpath)) { continue }
    $m = Get-Content -Raw -LiteralPath $mpath | ConvertFrom-Json
    if ($m.PSObject.Properties['Version']) { $m.Version = 'V3.9.17' }
    if ($m.PSObject.Properties['BuildName']) { $m.BuildName = 'FreeCam_Manager_V3.9.17' }
    if ($m.PSObject.Properties['Base']) { $m.Base = 'FreeCam_Manager_V3.9.16' }
    if ($m.PSObject.Properties['Branch']) { $m.Branch = 'release/manager-v3.9.17' }
    if ($m.PSObject.Properties['Feature']) { $m.Feature = 'Incremental UI refresh and SQLite delta persistence' }
    if ($m.PSObject.Properties['BuildId']) { $m.BuildId = 'MANAGER-V3917-20261010' }
    if ($m.PSObject.Properties['Commit']) { $m.Commit = 'PUBLISH-WORKFLOW' }
    if ($m.PSObject.Properties['ReleaseState']) { $m.ReleaseState = 'Stable' }
    if ($m.PSObject.Properties['RemotePublish']) { $m.RemotePublish = $true }
    $m | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $mpath -Encoding UTF8
}
$tests = [IO.File]::ReadAllText((Join-Path $src 'FreeCamManager.Tests\Program.cs'))
$sqliteTests = [IO.File]::ReadAllText((Join-Path $src 'FreeCamManager.SQLiteMigration.Tests\Program.cs'))
if (-not $tests.Contains('V3917OnePersistPerImport') -or -not $sqliteTests.Contains('TestProductionDeltaSessionAsync')) {
    throw 'V3.9.17 performance regressions missing from source package'
}
Write-Host '[V3.9.17] performance changes and regression suites staged'
