from pathlib import Path

path = Path('.github/workflows/publish-manager-update.yml')
text = path.read_text(encoding='utf-8')
if 'name: Validate final online source package on Windows' in text:
    print('release gate already present')
    raise SystemExit(0)

anchor = '''jobs:\n  publish:\n    runs-on: ubuntu-latest\n'''
if text.count(anchor) != 1:
    raise SystemExit(f'publish job anchor count={text.count(anchor)}')

gate = r'''jobs:
  release-gate:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0

      - name: Validate final online source package on Windows
        shell: pwsh
        run: |
          $ErrorActionPreference = 'Stop'
          $cfg = Get-Content -Raw -LiteralPath 'manager/publish.json' | ConvertFrom-Json
          $package = Join-Path 'manager/packages' $cfg.packageName
          if (-not (Test-Path -LiteralPath $package)) { throw "Final source package is not staged: $package" }

          $actualSha = (Get-FileHash -Algorithm SHA256 -LiteralPath $package).Hash.ToLowerInvariant()
          $expectedSha = ([string]$cfg.sourceSha256).ToLowerInvariant()
          if ($actualSha -ne $expectedSha) { throw "Final package SHA256 mismatch: expected=$expectedSha actual=$actualSha" }

          $work = Join-Path $PWD 'release-gate-source'
          if (Test-Path $work) { Remove-Item -Recurse -Force $work }
          Expand-Archive -LiteralPath $package -DestinationPath $work -Force
          $src = Join-Path $work 'src-wpf'
          foreach ($required in @('Build_v3_On_Windows.ps1','Apply_Manager_Update.ps1','FreeCamManager.sln')) {
            if (-not (Test-Path -LiteralPath (Join-Path $src $required))) { throw "Updater-required source path missing: src-wpf/$required" }
          }

          $project = Join-Path $src 'FreeCamManager\FreeCamManager.csproj'
          if (-not (Test-Path -LiteralPath $project)) { throw 'FreeCamManager.csproj missing from final package.' }
          [xml]$projectXml = Get-Content -Raw -LiteralPath $project
          $projectVersion = [string]$projectXml.Project.PropertyGroup.Version | Select-Object -First 1
          if ($projectVersion -ne [string]$cfg.version) { throw "Project version mismatch: publish=$($cfg.version) package=$projectVersion" }

          & (Join-Path $src 'Build_v3_On_Windows.ps1') -NoLaunch -OutDir 'BuildOutput_ReleaseGate'
          if ($LASTEXITCODE -ne 0) { throw "Final package updater build failed: $LASTEXITCODE" }
          $resultPath = Join-Path $src 'BuildOutput_ReleaseGate\BUILD_RESULT.txt'
          if (-not (Test-Path -LiteralPath $resultPath)) { throw 'BUILD_RESULT.txt missing after release-gate build.' }
          $result = Get-Content -Raw -LiteralPath $resultPath
          if ($result -notmatch 'Tests:\s*\d+ passed,\s*0 failed') { throw 'Final package regression suite did not report zero failures.' }
          if (-not (Test-Path -LiteralPath (Join-Path $src 'BuildOutput_ReleaseGate\FreeCam_Manager.exe'))) { throw 'Final package did not produce FreeCam_Manager.exe.' }
          Write-Host "FINAL_PACKAGE_RELEASE_GATE_PASS version=$($cfg.version) sha256=$actualSha"

  publish:
    needs: release-gate
    runs-on: ubuntu-latest
'''
text = text.replace(anchor, gate, 1)
path.write_text(text, encoding='utf-8')
print('Hardened', path)
