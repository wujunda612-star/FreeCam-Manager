param(
    [Parameter(Mandatory=$true)][int]$OldPid,
    [Parameter(Mandatory=$true)][string]$CurrentExe,
    [Parameter(Mandatory=$true)][string]$StagedSourceRoot,
    [Parameter(Mandatory=$true)][string]$TargetVersion,
    [switch]$Restart
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$CurrentExe = [System.IO.Path]::GetFullPath($CurrentExe)
$StagedSourceRoot = [System.IO.Path]::GetFullPath($StagedSourceRoot)
$StageDir = Split-Path -Parent $StagedSourceRoot
if ((Split-Path -Leaf $StageDir) -ieq 'source') {
    $StageDir = Split-Path -Parent $StageDir
}
$LogFile = Join-Path $StageDir 'UPDATE_RESULT.txt'
$StatusFile = Join-Path $StageDir 'UPDATE_STATUS.json'
$BuildScript = Join-Path $StagedSourceRoot 'src-wpf\Build_v3_On_Windows.ps1'
$BuildOut = Join-Path $StageDir 'build-output'
$NewExe = Join-Path $BuildOut 'FreeCam_Manager.exe'
$TargetDir = Split-Path -Parent $CurrentExe
$TempExe = $CurrentExe + '.new'
$BackupExe = $CurrentExe + '.bak'

function Write-UpdateLog([string]$Text) {
    $stamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
    $line = "[$stamp] $Text"
    Write-Host $line
    Add-Content -LiteralPath $LogFile -Value $line -Encoding UTF8
}

function Write-Status([string]$State, [string]$Message) {
    $payload = [ordered]@{
        state = $State
        targetVersion = $TargetVersion
        message = $Message
        updatedAt = (Get-Date).ToString('o')
        logFile = $LogFile
    } | ConvertTo-Json -Depth 3
    [System.IO.File]::WriteAllText($StatusFile, $payload, (New-Object System.Text.UTF8Encoding($false)))
}

function Restart-ManagerIfRequested {
    if ($Restart -and (Test-Path -LiteralPath $CurrentExe)) {
        try {
            Write-UpdateLog "Restarting Manager: $CurrentExe"
            Start-Process -FilePath $CurrentExe -WorkingDirectory $TargetDir
        } catch {
            Write-UpdateLog ("Restart failed: " + $_.Exception.Message)
        }
    }
}

New-Item -ItemType Directory -Force -Path $StageDir | Out-Null
Set-Content -LiteralPath $LogFile -Value "FreeCam Manager updater -> $TargetVersion" -Encoding UTF8
Write-Status 'starting' 'Waiting for previous Manager instance to exit'

try {
    if ($OldPid -gt 0) {
        $old = Get-Process -Id $OldPid -ErrorAction SilentlyContinue
        if ($old) {
            Write-UpdateLog "Waiting for old Manager PID $OldPid"
            try { Wait-Process -Id $OldPid -Timeout 30 -ErrorAction Stop } catch {
                throw "Previous Manager instance did not exit within 30 seconds; update cancelled"
            }
        }
    }

    if (-not (Test-Path -LiteralPath $BuildScript)) { throw "Build script not found: $BuildScript" }
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $BuildOut
    Write-Status 'building' 'Building new Manager version locally'
    Write-UpdateLog "Building staged source: $StagedSourceRoot"

    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $BuildScript -OutDir $BuildOut -NoLaunch
    $buildExit = $LASTEXITCODE
    Write-UpdateLog "Build exit code: $buildExit"
    if ($buildExit -ne 0) { throw "New version build failed; exit code $buildExit" }
    if (-not (Test-Path -LiteralPath $NewExe)) { throw "Build completed but output executable was not found: $NewExe" }

    Write-Status 'installing' 'Build succeeded; replacing Manager executable'
    Remove-Item -Force -ErrorAction SilentlyContinue $TempExe
    Copy-Item -Force -LiteralPath $NewExe -Destination $TempExe

    Remove-Item -Force -ErrorAction SilentlyContinue $BackupExe
    if (Test-Path -LiteralPath $CurrentExe) {
        Copy-Item -Force -LiteralPath $CurrentExe -Destination $BackupExe
    }

    try {
        if (Test-Path -LiteralPath $CurrentExe) { Remove-Item -Force -LiteralPath $CurrentExe }
        Move-Item -Force -LiteralPath $TempExe -Destination $CurrentExe
    }
    catch {
        Write-UpdateLog ("Replacement failed: " + $_.Exception.Message)
        if (Test-Path -LiteralPath $BackupExe) {
            Copy-Item -Force -LiteralPath $BackupExe -Destination $CurrentExe
            Write-UpdateLog 'Restored backup executable.'
        }
        throw
    }

    # Config files are user-local mutable data. Only seed new defaults when a file is absent.
    $BuildConfig = Join-Path $BuildOut 'Config'
    $TargetConfig = Join-Path $TargetDir 'Config'
    if (Test-Path -LiteralPath $BuildConfig) {
        New-Item -ItemType Directory -Force -Path $TargetConfig | Out-Null
        foreach ($name in @('FilenameTerms.json', 'version.json')) {
            $src = Join-Path $BuildConfig $name
            $dst = Join-Path $TargetConfig $name
            if ((Test-Path -LiteralPath $src) -and -not (Test-Path -LiteralPath $dst)) {
                Copy-Item -LiteralPath $src -Destination $dst
                Write-UpdateLog "Seeded missing Config/$name"
            }
        }
    }

    Write-Status 'success' 'Update completed; restarting Manager'
    Write-UpdateLog "Update installed successfully: $TargetVersion"
    Restart-ManagerIfRequested
    exit 0
}
catch {
    $message = $_.Exception.Message
    Write-UpdateLog ("UPDATE_FAILED: " + $message)
    if (-not (Test-Path -LiteralPath $CurrentExe) -and (Test-Path -LiteralPath $BackupExe)) {
        try {
            Copy-Item -Force -LiteralPath $BackupExe -Destination $CurrentExe
            Write-UpdateLog 'Recovered old executable from backup.'
        } catch {
            Write-UpdateLog ("Backup restore failed: " + $_.Exception.Message)
        }
    }
    Write-Status 'failed' $message
    Restart-ManagerIfRequested
    exit 1
}
