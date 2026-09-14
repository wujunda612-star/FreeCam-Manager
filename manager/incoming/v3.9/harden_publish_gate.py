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
          & '.\manager\release\Validate_Manager_Source_Package.ps1' `
            -PackagePath $package `
            -ExpectedSha256 ([string]$cfg.sourceSha256) `
            -ExpectedVersion ([string]$cfg.version) `
            -WorkDirectory (Join-Path $PWD 'release-gate-source')
          if ($LASTEXITCODE -ne 0) { throw "Final package release gate failed: $LASTEXITCODE" }

  publish:
    needs: release-gate
    runs-on: ubuntu-latest
'''
text = text.replace(anchor, gate, 1)
path.write_text(text, encoding='utf-8')
print('Hardened', path)
