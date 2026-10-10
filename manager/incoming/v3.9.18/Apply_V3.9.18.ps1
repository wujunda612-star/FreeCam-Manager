param([Parameter(Mandatory=$true)][string]$SourceRoot)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$src = Join-Path $SourceRoot 'src-wpf'
$stage = Join-Path $env:GITHUB_WORKSPACE 'manager\incoming\v3.9.18\inspection'
$control = Join-Path $src 'FreeCamManager\Controls\ScrollToTopButton.cs'
$policy = Join-Path $src 'FreeCamManager.Core\Services\BackToTopVisibilityPolicy.cs'
$controlFrom = Join-Path $stage 'ScrollToTopButton.cs'
$policyFrom = Join-Path $stage 'BackToTopVisibilityPolicy.cs'
foreach ($file in @($control, $controlFrom, $policyFrom)) {
    if (-not (Test-Path -LiteralPath $file)) { throw "Missing required file: $file" }
}
$original = [IO.File]::ReadAllText($control)
if (-not ($original.Contains('private const double RevealOffset = 80;') -and $original.Contains('↑ 置顶'))) {
    throw 'Official V3.9.17 control does not match the expected baseline'
}
Copy-Item -LiteralPath $controlFrom -Destination $control -Force
Copy-Item -LiteralPath $policyFrom -Destination $policy -Force

# Add deterministic policy tests to the existing Core regression suite.
$testsPath = Join-Path $src 'FreeCamManager.Tests\Program.cs'
$tests = [IO.File]::ReadAllText($testsPath)
$runAnchor = '        await Run("V3.9.17 inbox import persists build and test folder once", V3917OnePersistPerImport);'
$methodAnchor = '    private static Task ArtifactSnakeCaseJson()'
if (-not $tests.Contains($runAnchor) -or -not $tests.Contains($methodAnchor) -or
    $tests.Contains('V3918BackToTopVisibilityPolicy')) {
    throw 'Unexpected test program baseline'
}
$tests = $tests.Replace($runAnchor, $runAnchor + [Environment]::NewLine +
    '        await Run("V3.9.18 Back-to-Top logical and pixel scroll thresholds", V3918BackToTopVisibilityPolicy);')
$method = @'
    private static Task V3918BackToTopVisibilityPolicy()
    {
        var policy = BackToTopVisibilityPolicy.ShouldShow;
        // Logical scrolling: viewport=12 visible rows. Must NOT require 80 rows.
        Assert(!policy(0, 12, 400, false), "top unexpectedly shows button");
        Assert(!policy(11, 12, 400, false), "revealed before one viewport");
        Assert(policy(12, 12, 400, false), "did not reveal after one viewport");
        Assert(policy(8, 12, 400, true), "visible button flickered near reveal boundary");
        Assert(!policy(6, 12, 400, true), "button did not hide when near the top");
        Assert(!policy(0, 12, 400, true), "button must hide at top");
        Assert(!policy(2, 12, 0, true), "non-scrollable list must hide button");
        Assert(!policy(1, 12, 3, false), "short list must not show needlessly");
        // Physical scrolling: same policy measured in pixels rather than rows.
        Assert(!policy(599, 600, 6000, false), "pixel scroller revealed too early");
        Assert(policy(600, 600, 6000, false), "pixel scroller did not reveal at one viewport");
        Assert(policy(450, 600, 6000, true), "pixel scroller hysteresis lost");
        Assert(!policy(300, 600, 6000, true), "pixel scroller failed to hide near top");
        Assert(!policy(double.NaN, 12, 200, false), "invalid offset must be hidden");
        return Task.CompletedTask;
    }

'@
$tests = $tests.Replace($methodAnchor, $method + $methodAnchor)
[IO.File]::WriteAllText($testsPath, $tests, [System.Text.UTF8Encoding]::new($true))

$project = Join-Path $src 'FreeCamManager\FreeCamManager.csproj'
$p = [IO.File]::ReadAllText($project)
if (-not $p.Contains('<Version>3.9.17</Version>')) { throw 'Expected official source V3.9.17' }
[IO.File]::WriteAllText($project, $p.Replace('<Version>3.9.17</Version>','<Version>3.9.18</Version>'),[System.Text.UTF8Encoding]::new($true))
foreach ($mpath in @((Join-Path $SourceRoot 'BUILD_MANIFEST.json'), (Join-Path $src 'BUILD_MANIFEST.json'))) {
    if (-not (Test-Path -LiteralPath $mpath)) { continue }
    $m = Get-Content -Raw -LiteralPath $mpath | ConvertFrom-Json
    if ($m.PSObject.Properties['Version']) { $m.Version = 'V3.9.18' }
    if ($m.PSObject.Properties['BuildName']) { $m.BuildName = 'FreeCam_Manager_V3.9.18' }
    if ($m.PSObject.Properties['Base']) { $m.Base = 'FreeCam_Manager_V3.9.17' }
    if ($m.PSObject.Properties['Branch']) { $m.Branch = 'release/manager-v3.9.18' }
    if ($m.PSObject.Properties['Feature']) { $m.Feature = 'Back-to-Top logical and physical scrolling threshold correction' }
    if ($m.PSObject.Properties['BuildId']) { $m.BuildId = 'MANAGER-V3918-20261011' }
    if ($m.PSObject.Properties['Commit']) { $m.Commit = 'PUBLISH-WORKFLOW' }
    if ($m.PSObject.Properties['ReleaseState']) { $m.ReleaseState = 'Stable' }
    if ($m.PSObject.Properties['RemotePublish']) { $m.RemotePublish = $true }
    $m | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $mpath -Encoding UTF8
}
if (-not ([IO.File]::ReadAllText($testsPath)).Contains('V3918BackToTopVisibilityPolicy')) {
    throw 'Back-to-Top policy tests not inserted'
}
Write-Host '[V3.9.18] targeted scroll-to-top control and policy tests applied'
