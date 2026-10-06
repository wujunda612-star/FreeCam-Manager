param([Parameter(Mandatory=$true)][string]$SourceRoot)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$Utf8 = New-Object System.Text.UTF8Encoding($true)

function Write-Utf8([string]$Path,[string]$Text) {
    [IO.File]::WriteAllText($Path,$Text,$Utf8)
}

$src = Join-Path $SourceRoot 'src-wpf'
if (-not (Test-Path -LiteralPath (Join-Path $src 'FreeCamManager\FreeCamManager.csproj'))) {
    throw "src-wpf not found under $SourceRoot"
}

$row = Join-Path $src 'FreeCamManager\ViewModels\ArtifactRowViewModel.cs'
$t = [IO.File]::ReadAllText($row)
$pattern = '(?s)(private async Task SaveMetadataAsync\\(string message\\).*?)(await _library\\.SaveAsync\\(\\);)'
$matches = [regex]::Matches($t, $pattern)
if ($matches.Count -ne 1) { throw "SaveMetadataAsync patch anchor count=$($matches.Count)" }
$t = [regex]::Replace(
    $t,
    $pattern,
    '$1await Task.Run(() => _library.SaveAsync());',
    1
)
Write-Utf8 $row $t

$csproj = Join-Path $src 'FreeCamManager\FreeCamManager.csproj'
$p = [IO.File]::ReadAllText($csproj)
if (-not $p.Contains('<Version>3.9.12</Version>')) { throw 'V3.9.12 version anchor missing' }
$p = $p.Replace('<Version>3.9.12</Version>', '<Version>3.9.13</Version>')
Write-Utf8 $csproj $p

foreach ($manifest in @((Join-Path $SourceRoot 'BUILD_MANIFEST.json'), (Join-Path $src 'BUILD_MANIFEST.json'))) {
    if (-not (Test-Path -LiteralPath $manifest)) { continue }
    $m = Get-Content -Raw -LiteralPath $manifest | ConvertFrom-Json
    if ($m.PSObject.Properties['Version']) { $m.Version = 'V3.9.13' }
    if ($m.PSObject.Properties['BuildName']) { $m.BuildName = 'FreeCam_Manager_V3.9.13' }
    if ($m.PSObject.Properties['Base']) { $m.Base = 'FreeCam_Manager_V3.9.12' }
    if ($m.PSObject.Properties['Branch']) { $m.Branch = 'release/manager-v3.9.13' }
    if ($m.PSObject.Properties['Feature']) { $m.Feature = 'Move lightweight metadata persistence off the WPF UI thread' }
    if ($m.PSObject.Properties['BuildType']) { $m.BuildType = 'Stable' }
    if ($m.PSObject.Properties['Stage']) { $m.Stage = 'Release' }
    if ($m.PSObject.Properties['BuildId']) { $m.BuildId = 'MANAGER-V3913-20261006' }
    if ($m.PSObject.Properties['Commit']) { $m.Commit = 'PUBLISH-WORKFLOW' }
    if ($m.PSObject.Properties['ReleaseState']) { $m.ReleaseState = 'Stable' }
    if ($m.PSObject.Properties['RemotePublish']) { $m.RemotePublish = $true }
    $m | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $manifest -Encoding UTF8
}

$rowCheck = [IO.File]::ReadAllText($row)
if (-not $rowCheck.Contains('await Task.Run(() => _library.SaveAsync());')) {
    throw 'Background metadata persistence contract missing'
}
if ($rowCheck -match '(?s)private async Task SaveMetadataAsync\(string message\).*?await _library\.SaveAsync\(\);') {
    throw 'Direct UI-thread metadata SaveAsync call still present'
}
$sharedCallersOk = $rowCheck.Contains('SaveMetadataAsync(saveMessage)') -and
    $rowCheck.Contains('SaveMetadataAsync(clamped == 0') -and
    $rowCheck.Contains('SaveMetadataAsync(locked ?') -and
    $rowCheck.Contains('SaveMetadataAsync("备注已保存")')
if (-not $sharedCallersOk) {
    throw 'Shared lightweight metadata save callers regressed'
}

Write-Host '[V3.9.13] metadata UI-thread persistence patch PASS'
