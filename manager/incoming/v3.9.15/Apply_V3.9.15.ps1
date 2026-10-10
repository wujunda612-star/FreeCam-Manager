param([Parameter(Mandatory=$true)][string]$SourceRoot)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$Utf8 = New-Object System.Text.UTF8Encoding($true)
$src = Join-Path $SourceRoot 'src-wpf'
$staged = Join-Path $env:GITHUB_WORKSPACE 'manager\incoming\v3.9.15\inspection'
if (-not (Test-Path -LiteralPath (Join-Path $src 'FreeCamManager\FreeCamManager.csproj'))) {
    throw 'V3.9.14 baseline source directory is invalid'
}
$mapping = @{
    'ManifestService.cs' = 'FreeCamManager.Core\Services\ManifestService.cs'
    'ClassificationService.cs' = 'FreeCamManager.Core\Services\ClassificationService.cs'
    'FreeCamManager.Tests_Program.cs' = 'FreeCamManager.Tests\Program.cs'
}
foreach($entry in $mapping.GetEnumerator()) {
    $from = Join-Path $staged $entry.Key
    $to = Join-Path $src $entry.Value
    if (-not (Test-Path -LiteralPath $from) -or -not (Test-Path -LiteralPath $to)) {
        throw "Missing source patch or destination: $from $to"
    }
    Copy-Item -LiteralPath $from -Destination $to -Force
}
$project = Join-Path $src 'FreeCamManager\FreeCamManager.csproj'
$p = [IO.File]::ReadAllText($project)
if (-not $p.Contains('<Version>3.9.14</Version>')) { throw 'Expected 3.9.14 baseline version' }
[IO.File]::WriteAllText($project,$p.Replace('<Version>3.9.14</Version>','<Version>3.9.15</Version>'),$Utf8)
foreach($mpath in @((Join-Path $SourceRoot 'BUILD_MANIFEST.json'),(Join-Path $src 'BUILD_MANIFEST.json'))) {
    if (-not (Test-Path -LiteralPath $mpath)) { continue }
    $m = Get-Content -Raw -LiteralPath $mpath | ConvertFrom-Json
    if ($m.PSObject.Properties['Version']) { $m.Version = 'V3.9.15' }
    if ($m.PSObject.Properties['BuildName']) { $m.BuildName = 'FreeCam_Manager_V3.9.15' }
    if ($m.PSObject.Properties['Base']) { $m.Base = 'FreeCam_Manager_V3.9.14' }
    if ($m.PSObject.Properties['Branch']) { $m.Branch = 'release/manager-v3.9.15' }
    if ($m.PSObject.Properties['Feature']) { $m.Feature = 'FreeCam and WW build identity and legacy Manifest classification' }
    if ($m.PSObject.Properties['BuildId']) { $m.BuildId = 'MANAGER-V3915-20261010' }
    if ($m.PSObject.Properties['Commit']) { $m.Commit = 'PUBLISH-WORKFLOW' }
    if ($m.PSObject.Properties['ReleaseState']) { $m.ReleaseState = 'Stable' }
    if ($m.PSObject.Properties['RemotePublish']) { $m.RemotePublish = $true }
    $m | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $mpath -Encoding UTF8
}
$testFile = Join-Path $src 'FreeCamManager.Tests\Program.cs'
if (-not ([IO.File]::ReadAllText($testFile).Contains('V3915RealManifestContracts'))) {
    throw 'V3.9.15 regression matrix is missing'
}
Write-Host '[V3.9.15] source and regression matrix applied'
