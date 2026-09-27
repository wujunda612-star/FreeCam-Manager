from pathlib import Path
import base64,re
root=Path(__file__).resolve().parent
app=(root/'FreeCamManager/App.xaml.cs').read_text(encoding='utf-8')
script=(root/'Apply_Manager_Update.ps1').read_bytes()
assert script.decode('ascii')
payload=(root/'FreeCamManager/Services/EmbeddedManagerUpdater.cs').read_text(encoding='utf-8')
assert 'using System.IO;' in payload, 'EmbeddedManagerUpdater must import System.IO for Directory/Path/File'
parts=re.findall(r'"([A-Za-z0-9+/=]+)"',payload)
assert base64.b64decode(''.join(parts)) == script
assert 'EmbeddedManagerUpdater.WriteTo(staged.StagingDirectory)' in app
assert 'Run_Manager_Update.cmd' in app and 'UPDATE_BOOTSTRAP.log' in app and 'UPDATE_HANDOFF.txt' in app
for line in app.splitlines():
    if 'powershell.exe' in line or 'echo updater=' in line or '$"updater=' in line:
        assert 'staged.UpdaterScriptPath' not in line, line
csproj=(root/'FreeCamManager/FreeCamManager.csproj').read_text(encoding='utf-8')
m=re.search(r'<Version>(\d+)\.(\d+)\.(\d+)</Version>', csproj)
assert m and tuple(map(int,m.groups())) >= (3,4,7), 'Fix7 updater contract applies to Fix7 and later versions'
print('PASS: Fix7 updater is ASCII-safe and self-hosted by current Manager')
