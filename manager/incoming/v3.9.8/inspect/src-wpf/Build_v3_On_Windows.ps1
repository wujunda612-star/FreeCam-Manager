param(
    [string]$OutDir = "",
    [switch]$NoLaunch
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($OutDir)) {
    $OutDir = Join-Path $Root 'BuildOutput'
} elseif (-not [System.IO.Path]::IsPathRooted($OutDir)) {
    $OutDir = Join-Path $Root $OutDir
}
$OutDir = [System.IO.Path]::GetFullPath($OutDir)
$Tools = Join-Path $Root '.tools'
$PortableDotnet = Join-Path $Tools 'dotnet'
$Installer = Join-Path $Tools 'dotnet-install.ps1'
$LogFile = Join-Path $OutDir 'BUILD_RESULT.txt'
$TestStdout = Join-Path $OutDir 'TEST_STDOUT.txt'
$TestStderr = Join-Path $OutDir 'TEST_STDERR.txt'
$BuildStdout = Join-Path $OutDir 'TEST_BUILD_STDOUT.txt'
$BuildStderr = Join-Path $OutDir 'TEST_BUILD_STDERR.txt'
$PublishStdout = Join-Path $OutDir 'PUBLISH_STDOUT.txt'
$PublishStderr = Join-Path $OutDir 'PUBLISH_STDERR.txt'
$AppProject = Join-Path $Root 'FreeCamManager\FreeCamManager.csproj'

function Get-ProjectVersion([string]$ProjectPath) {
    $text = [System.IO.File]::ReadAllText($ProjectPath)
    $match = [regex]::Match($text, '<Version>\s*([^<]+)\s*</Version>', [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
    if (-not $match.Success) { throw "Project Version not found: $ProjectPath" }
    return $match.Groups[1].Value.Trim()
}

function Get-DisplayVersion([string]$VersionText) {
    $parts = $VersionText.Split('.')
    if ($parts.Length -lt 2) { return "V$VersionText" }
    $display = "V$($parts[0]).$($parts[1])"
    if ($parts.Length -ge 3 -and [int]$parts[2] -gt 0) { $display += " Fix$($parts[2])" }
    return $display
}

$VersionText = Get-ProjectVersion $AppProject
$DisplayVersion = Get-DisplayVersion $VersionText

New-Item -ItemType Directory -Force -Path $Tools | Out-Null
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

function Write-Log([string]$Text) {
    $stamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
    $line = "[$stamp] $Text"
    Write-Host $line
    Add-Content -LiteralPath $LogFile -Value $line -Encoding UTF8
}

function Append-FileToLog([string]$Header, [string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return }
    Write-Log $Header
    Get-Content -LiteralPath $Path -Encoding UTF8 -ErrorAction SilentlyContinue | ForEach-Object {
        $line = "    $_"
        Write-Host $line
        Add-Content -LiteralPath $LogFile -Value $line -Encoding UTF8
    }
}

function Run-LoggedProcess(
    [string]$Label,
    [string]$FilePath,
    [string[]]$Arguments,
    [string]$Stdout,
    [string]$Stderr,
    [int]$TimeoutSeconds = 180
) {
    Remove-Item -Force -ErrorAction SilentlyContinue $Stdout, $Stderr
    Write-Log "$Label started."
    $argText = ($Arguments | ForEach-Object {
        if ($_ -match '[\s"]') { '"' + ($_ -replace '"','\"') + '"' } else { $_ }
    }) -join ' '

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $FilePath
    $psi.Arguments = $argText
    $psi.WorkingDirectory = $Root
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    try {
        $psi.StandardOutputEncoding = New-Object System.Text.UTF8Encoding($false)
        $psi.StandardErrorEncoding = New-Object System.Text.UTF8Encoding($false)
    } catch { }

    $p = New-Object System.Diagnostics.Process
    $p.StartInfo = $psi
    if (-not $p.Start()) { throw "$Label failed to start" }

    $stdoutTask = $p.StandardOutput.ReadToEndAsync()
    $stderrTask = $p.StandardError.ReadToEndAsync()
    $timedOut = -not $p.WaitForExit($TimeoutSeconds * 1000)
    if ($timedOut) {
        try { $p.Kill() } catch { }
    }

    try { $p.WaitForExit() } catch { }
    $stdoutText = $stdoutTask.Result
    $stderrText = $stderrTask.Result
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Stdout, $stdoutText, $utf8NoBom)
    [System.IO.File]::WriteAllText($Stderr, $stderrText, $utf8NoBom)

    Append-FileToLog "$Label stdout:" $Stdout
    Append-FileToLog "$Label stderr:" $Stderr

    if ($timedOut) { throw "$Label timed out after $TimeoutSeconds seconds" }
    $p.Refresh()
    $exitCode = [int]$p.ExitCode
    Write-Log "$Label exit_code=$exitCode"
    if ($exitCode -ne 0) { throw "$Label failed with exit code $exitCode" }
}

Set-Content -LiteralPath $LogFile -Value "FreeCam Manager $DisplayVersion WPF local build log" -Encoding UTF8
Write-Log 'Build started.'

try {
    function Has-CompatibleSdk([string]$DotnetPath) {
        try {
            $sdks = & $DotnetPath --list-sdks 2>$null
            foreach ($line in $sdks) {
                if ($line -match '^([0-9]+)\.([0-9]+)\.') {
                    if ([int]$Matches[1] -ge 8) { return $true }
                }
            }
        } catch { }
        return $false
    }

    $dotnetCmd = Get-Command dotnet -ErrorAction SilentlyContinue
    $Dotnet = $null
    if ($dotnetCmd -and (Has-CompatibleSdk $dotnetCmd.Source)) {
        $Dotnet = $dotnetCmd.Source
        Write-Log "Using installed .NET SDK: $Dotnet"
    } elseif ((Test-Path (Join-Path $PortableDotnet 'dotnet.exe')) -and (Has-CompatibleSdk (Join-Path $PortableDotnet 'dotnet.exe'))) {
        $Dotnet = Join-Path $PortableDotnet 'dotnet.exe'
        Write-Log "Using portable .NET SDK: $Dotnet"
    } else {
        Write-Log 'Compatible .NET SDK not found. Downloading official Microsoft dotnet-install script.'
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -UseBasicParsing 'https://dot.net/v1/dotnet-install.ps1' -OutFile $Installer
        Write-Log 'Installing portable .NET 8 SDK (no admin, local folder only). This is about 200 MB.'
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $Installer -Channel 8.0 -Architecture x64 -InstallDir $PortableDotnet -NoPath
        if ($LASTEXITCODE -ne 0) { throw "dotnet-install failed with exit code $LASTEXITCODE" }
        $Dotnet = Join-Path $PortableDotnet 'dotnet.exe'
        if (-not (Has-CompatibleSdk $Dotnet)) { throw 'Portable .NET SDK installation did not produce a compatible SDK.' }
    }

    Write-Log "dotnet: $(& $Dotnet --version)"

    $TestProject = Join-Path $Root 'FreeCamManager.Tests\FreeCamManager.Tests.csproj'
    $PublishDir = Join-Path $OutDir 'publish'
    if (Test-Path $PublishDir) { Remove-Item -Recurse -Force $PublishDir }

    Write-Log 'Restoring projects.'
    & $Dotnet restore $TestProject
    if ($LASTEXITCODE -ne 0) { throw "test restore failed with exit code $LASTEXITCODE" }
    & $Dotnet restore $AppProject -r win-x64
    if ($LASTEXITCODE -ne 0) { throw "app restore failed with exit code $LASTEXITCODE" }

    Run-LoggedProcess 'Core test build' $Dotnet @('build', $TestProject, '-c', 'Release', '--no-restore', '-v:minimal') $BuildStdout $BuildStderr 180

    $TestDll = Join-Path $Root 'FreeCamManager.Tests\bin\Release\net8.0\FreeCamManager.Tests.dll'
    if (-not (Test-Path $TestDll)) { throw "test DLL not found: $TestDll" }
    Run-LoggedProcess 'Core compatibility tests' $Dotnet @($TestDll) $TestStdout $TestStderr 180

    Write-Log 'Publishing WPF app as Windows x64 self-contained single EXE.'
    Run-LoggedProcess 'WPF publish' $Dotnet @(
        'publish', $AppProject,
        '-c', 'Release',
        '-r', 'win-x64',
        '--self-contained', 'true',
        '--no-restore',
        '-o', $PublishDir,
        '/p:PublishSingleFile=true',
        '/p:PublishTrimmed=false',
        '/p:IncludeNativeLibrariesForSelfExtract=true',
        '/p:DebugType=embedded',
        '/p:Deterministic=true'
    ) $PublishStdout $PublishStderr 300

    $Exe = Join-Path $PublishDir 'FreeCam_Manager.exe'
    if (-not (Test-Path $Exe)) { throw "published EXE not found: $Exe" }
    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $Exe).Hash.ToLowerInvariant()
    $size = (Get-Item -LiteralPath $Exe).Length
    Write-Log "EXE OK: $Exe"
    Write-Log "Size: $size bytes"
    Write-Log "SHA256: $hash"

    $DirectExe = Join-Path $OutDir 'FreeCam_Manager.exe'
    Copy-Item -Force -LiteralPath $Exe -Destination $DirectExe
    $PublishConfig = Join-Path $PublishDir 'Config'
    $DirectConfig = Join-Path $OutDir 'Config'
    if (Test-Path $PublishConfig) {
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $DirectConfig
        Copy-Item -Recurse -Force -LiteralPath $PublishConfig -Destination $DirectConfig
        Write-Log "Copied external filename terms to: $DirectConfig"
    }
    Write-Log "Copied ready-to-test EXE to: $DirectExe"
    Write-Log 'Build completed successfully.'
    if (-not $NoLaunch) {
        Write-Log "Launching FreeCam Manager $DisplayVersion."
        Start-Process -FilePath $DirectExe
    } else {
        Write-Log 'NoLaunch requested; build output left ready for updater.'
    }
    exit 0
}
catch {
    Write-Log ("BUILD_FAILED: " + $_.Exception.Message)
    Write-Log ("Exception: " + $_.Exception.ToString())
    exit 1
}
