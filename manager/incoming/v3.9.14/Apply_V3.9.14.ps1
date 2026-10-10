param([Parameter(Mandatory=$true)][string]$SourceRoot)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$utf8 = New-Object System.Text.UTF8Encoding($true)
$src = Join-Path $SourceRoot 'src-wpf'
$inspect = Join-Path $env:GITHUB_WORKSPACE 'manager\incoming\v3.9.14\inspection'
if (-not (Test-Path -LiteralPath (Join-Path $src 'FreeCamManager\FreeCamManager.csproj'))) {
    throw 'Unexpected baseline source directory structure.'
}

$mapping = @{
    'ManifestService.cs' = 'FreeCamManager.Core\Services\ManifestService.cs'
    'ClassificationService.cs' = 'FreeCamManager.Core\Services\ClassificationService.cs'
    'StableVersionResolver.cs' = 'FreeCamManager.Core\Services\StableVersionResolver.cs'
    'SettingsViewModel.cs' = 'FreeCamManager\ViewModels\SettingsViewModel.cs'
    'SettingsView.xaml' = 'FreeCamManager\Views\SettingsView.xaml'
    'DevelopmentView.xaml' = 'FreeCamManager\Views\DevelopmentView.xaml'
    'HistoryView.xaml' = 'FreeCamManager\Views\HistoryView.xaml'
    'CustomCategoryView.xaml' = 'FreeCamManager\Views\CustomCategoryView.xaml'
    'StableView.xaml' = 'FreeCamManager\Views\StableView.xaml'
    'ScrollToTopButton.cs' = 'FreeCamManager\Controls\ScrollToTopButton.cs'
}
foreach ($entry in $mapping.GetEnumerator()) {
    $from = Join-Path $inspect $entry.Key
    $to = Join-Path $src $entry.Value
    if (-not (Test-Path -LiteralPath $from)) { throw "Missing staged patch file: $from" }
    if ($entry.Key -ne 'ScrollToTopButton.cs' -and -not (Test-Path -LiteralPath $to)) {
        throw "Original source missing: $to"
    }
    Copy-Item -LiteralPath $from -Destination $to -Force
}

$csproj = Join-Path $src 'FreeCamManager\FreeCamManager.csproj'
$xml = [IO.File]::ReadAllText($csproj)
if (-not $xml.Contains('<Version>3.9.13</Version>')) { throw '3.9.13 version anchor not found' }
$xml = $xml.Replace('<Version>3.9.13</Version>', '<Version>3.9.14</Version>')
[IO.File]::WriteAllText($csproj, $xml, $utf8)

# Keep the built-in filename aliases consistent with the already published online terms.
$termsSource = Join-Path $env:GITHUB_WORKSPACE 'FilenameTerms.json'
$termsDest = Join-Path $src 'FreeCamManager\Config\FilenameTerms.json'
$terms = Get-Content -LiteralPath $termsSource -Raw -Encoding UTF8 | ConvertFrom-Json
if (@($terms).Count -lt 242) { throw 'V3.9.14 terminology table is incomplete' }
Copy-Item -LiteralPath $termsSource -Destination $termsDest -Force

foreach ($path in @((Join-Path $SourceRoot 'BUILD_MANIFEST.json'), (Join-Path $src 'BUILD_MANIFEST.json'))) {
    if (-not (Test-Path -LiteralPath $path)) { continue }
    $m = Get-Content -Raw -LiteralPath $path | ConvertFrom-Json
    if ($m.PSObject.Properties['Version']) { $m.Version = 'V3.9.14' }
    if ($m.PSObject.Properties['BuildName']) { $m.BuildName = 'FreeCam_Manager_V3.9.14' }
    if ($m.PSObject.Properties['Base']) { $m.Base = 'FreeCam_Manager_V3.9.13' }
    if ($m.PSObject.Properties['Branch']) { $m.Branch = 'release/manager-v3.9.14' }
    if ($m.PSObject.Properties['Feature']) { $m.Feature = 'Build metadata compatibility; autosave; list scroll to top' }
    if ($m.PSObject.Properties['BuildId']) { $m.BuildId = 'MANAGER-V3914-20261010' }
    if ($m.PSObject.Properties['Commit']) { $m.Commit = 'PUBLISH-WORKFLOW' }
    if ($m.PSObject.Properties['ReleaseState']) { $m.ReleaseState = 'Stable' }
    if ($m.PSObject.Properties['RemotePublish']) { $m.RemotePublish = $true }
    $m | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $path -Encoding UTF8
}

Write-Host '[V3.9.14] staged source patches applied'
