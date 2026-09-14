param(
    [Parameter(Mandatory = $true)][string]$PackagePath,
    [Parameter(Mandatory = $true)][string]$ExpectedSha256,
    [Parameter(Mandatory = $true)][string]$ExpectedVersion,
    [string]$WorkDirectory = ""
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $PackagePath)) {
    throw "Final source package is not staged: $PackagePath"
}

$actualSha = (Get-FileHash -Algorithm SHA256 -LiteralPath $PackagePath).Hash.ToLowerInvariant()
$expectedSha = $ExpectedSha256.ToLowerInvariant()
if ($actualSha -ne $expectedSha) {
    throw "Final package SHA256 mismatch: expected=$expectedSha actual=$actualSha"
}

if ([string]::IsNullOrWhiteSpace($WorkDirectory)) {
    $WorkDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("FreeCamManager-ReleaseGate-" + [Guid]::NewGuid().ToString('N'))
}
if (Test-Path -LiteralPath $WorkDirectory) {
    Remove-Item -Recurse -Force -LiteralPath $WorkDirectory
}
New-Item -ItemType Directory -Force -Path $WorkDirectory | Out-Null

try {
    Expand-Archive -LiteralPath $PackagePath -DestinationPath $WorkDirectory -Force
    $src = Join-Path $WorkDirectory 'src-wpf'
    foreach ($required in @('Build_v3_On_Windows.ps1', 'Apply_Manager_Update.ps1', 'FreeCamManager.sln')) {
        if (-not (Test-Path -LiteralPath (Join-Path $src $required))) {
            throw "Updater-required source path missing: src-wpf/$required"
        }
    }

    $project = Join-Path $src 'FreeCamManager\FreeCamManager.csproj'
    if (-not (Test-Path -LiteralPath $project)) {
        throw 'FreeCamManager.csproj missing from final package.'
    }
    [xml]$projectXml = Get-Content -Raw -LiteralPath $project
    $versionNode = @($projectXml.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ }) | Select-Object -First 1
    $projectVersion = [string]$versionNode
    if ($projectVersion -ne $ExpectedVersion) {
        throw "Project version mismatch: expected=$ExpectedVersion package=$projectVersion"
    }

    & (Join-Path $src 'Build_v3_On_Windows.ps1') -NoLaunch -OutDir 'BuildOutput_ReleaseGate'
    if ($LASTEXITCODE -ne 0) {
        throw "Final package updater build failed: $LASTEXITCODE"
    }

    $resultPath = Join-Path $src 'BuildOutput_ReleaseGate\BUILD_RESULT.txt'
    if (-not (Test-Path -LiteralPath $resultPath)) {
        throw 'BUILD_RESULT.txt missing after release-gate build.'
    }
    $result = Get-Content -Raw -LiteralPath $resultPath
    if ($result -notmatch 'Tests:\s*\d+ passed,\s*0 failed') {
        throw 'Final package regression suite did not report zero failures.'
    }
    if (-not (Test-Path -LiteralPath (Join-Path $src 'BuildOutput_ReleaseGate\FreeCam_Manager.exe'))) {
        throw 'Final package did not produce FreeCam_Manager.exe.'
    }

    Write-Host "FINAL_PACKAGE_RELEASE_GATE_PASS version=$ExpectedVersion sha256=$actualSha"
}
finally {
    if (Test-Path -LiteralPath $WorkDirectory) {
        Remove-Item -Recurse -Force -LiteralPath $WorkDirectory -ErrorAction SilentlyContinue
    }
}
