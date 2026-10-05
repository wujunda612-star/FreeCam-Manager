param(
    [Parameter(Mandatory=$true)][string]$SourceRoot
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$program = Join-Path $SourceRoot 'src-wpf\FreeCamManager.Tests\Program.cs'
if (-not (Test-Path -LiteralPath $program)) { throw "Program.cs not found: $program" }

$t = [IO.File]::ReadAllText($program)
$replacement = '        Assert(devVm.Contains("ManualRefreshAsync", StringComparison.Ordinal) && devVm.Contains("_scanInboxNow", StringComparison.Ordinal), "development manual action must trigger inbox reorganization");'
if ($t.Contains($replacement)) {
    Write-Host 'V32 refresh contract already updated.'
    exit 0
}

$pattern = 'Assert\(devVm\.Contains\("ManualRefreshAsync", StringComparison\.Ordinal\).*?"development refresh must trigger real inbox scan"\);'
$matches = [regex]::Matches($t, $pattern, [System.Text.RegularExpressions.RegexOptions]::Singleline)
if ($matches.Count -ne 1) { throw "V32 refresh contract match count=$($matches.Count)" }
$m = $matches[0]
$t = $t.Substring(0, $m.Index) + $replacement + $t.Substring($m.Index + $m.Length)
[IO.File]::WriteAllText($program, $t, (New-Object System.Text.UTF8Encoding($true)))
Write-Host 'V32 refresh contract updated.'
