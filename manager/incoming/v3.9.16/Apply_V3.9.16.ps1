param([Parameter(Mandatory=$true)][string]$SourceRoot)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$utf8 = New-Object System.Text.UTF8Encoding($true)
$src = Join-Path $SourceRoot 'src-wpf'
$staged = Join-Path $env:GITHUB_WORKSPACE 'manager\incoming\v3.9.16\inspection'
$mapping = @{
  'StableVersionResolver.cs' = 'FreeCamManager.Core\Services\StableVersionResolver.cs'
  'ManifestService.cs' = 'FreeCamManager.Core\Services\ManifestService.cs'
  'LibraryService.cs' = 'FreeCamManager.Core\Services\LibraryService.cs'
  'OrganizerService.cs' = 'FreeCamManager.Core\Services\OrganizerService.cs'
  'StableViewModel.cs' = 'FreeCamManager\ViewModels\StableViewModel.cs'
  'MainWindowViewModel.cs' = 'FreeCamManager\ViewModels\MainWindowViewModel.cs'
  'App.xaml.cs' = 'FreeCamManager\App.xaml.cs'
  'FreeCamManager.Tests_Program.cs' = 'FreeCamManager.Tests\Program.cs'
}
foreach($entry in $mapping.GetEnumerator()) {
  $from = Join-Path $staged $entry.Key
  $to = Join-Path $src $entry.Value
  if (-not (Test-Path -LiteralPath $from) -or -not (Test-Path -LiteralPath $to)) {
    throw "Missing source patch or destination: $from -> $to"
  }
  Copy-Item -LiteralPath $from -Destination $to -Force
}
$project = Join-Path $src 'FreeCamManager\FreeCamManager.csproj'
$p = [IO.File]::ReadAllText($project)
if (-not $p.Contains('<Version>3.9.15</Version>')) { throw 'V3.9.15 version anchor missing' }
[IO.File]::WriteAllText($project,$p.Replace('<Version>3.9.15</Version>','<Version>3.9.16</Version>'),$utf8)
foreach($mpath in @((Join-Path $SourceRoot 'BUILD_MANIFEST.json'),(Join-Path $src 'BUILD_MANIFEST.json'))) {
  if (-not (Test-Path -LiteralPath $mpath)) { continue }
  $m = Get-Content -Raw -LiteralPath $mpath | ConvertFrom-Json
  if ($m.PSObject.Properties['Version']) { $m.Version = 'V3.9.16' }
  if ($m.PSObject.Properties['BuildName']) { $m.BuildName = 'FreeCam_Manager_V3.9.16' }
  if ($m.PSObject.Properties['Base']) { $m.Base = 'FreeCam_Manager_V3.9.15' }
  if ($m.PSObject.Properties['Branch']) { $m.Branch = 'release/manager-v3.9.16' }
  if ($m.PSObject.Properties['Feature']) { $m.Feature = 'Stable legacy row discovery and missing-file visibility' }
  if ($m.PSObject.Properties['BuildId']) { $m.BuildId = 'MANAGER-V3916-20261010' }
  if ($m.PSObject.Properties['Commit']) { $m.Commit = 'PUBLISH-WORKFLOW' }
  if ($m.PSObject.Properties['ReleaseState']) { $m.ReleaseState = 'Stable' }
  if ($m.PSObject.Properties['RemotePublish']) { $m.RemotePublish = $true }
  $m | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $mpath -Encoding UTF8
}
$source = [IO.File]::ReadAllText((Join-Path $src 'FreeCamManager.Tests\Program.cs'))
if (-not $source.Contains('V3916StableVisibilityAndDeletedDuplicates')) {
  throw 'V3.9.16 regression is missing'
}
Write-Host '[V3.9.16] stable visibility, deletion and regression source patches applied'
