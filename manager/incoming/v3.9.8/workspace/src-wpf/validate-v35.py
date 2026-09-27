from pathlib import Path
import re
import sys

root = Path(__file__).resolve().parent
app = root / 'FreeCamManager'
core = root / 'FreeCamManager.Core'
tests = root / 'FreeCamManager.Tests' / 'Program.cs'

errors = []

def need(cond, msg):
    if not cond:
        errors.append(msg)

csproj = (app / 'FreeCamManager.csproj').read_text(encoding='utf-8')
settings = (app / 'Views' / 'SettingsView.xaml').read_text(encoding='utf-8')
controls = (app / 'Styles' / 'Controls.xaml').read_text(encoding='utf-8')
appcs = (app / 'App.xaml.cs').read_text(encoding='utf-8')
testsrc = tests.read_text(encoding='utf-8')

version_match = re.search(r'<Version>(\d+)\.(\d+)\.(\d+)</Version>', csproj)
need(version_match is not None and tuple(map(int, version_match.groups())) >= (3, 5, 0), 'app version must be >= 3.5.0')
need('工作区规则' not in settings, 'workspace rules card must be removed')
need('测试工作区行为' not in settings, 'settings subtitle must not mention test workspace behavior')
need('Grid.Row="8" Style="{StaticResource PanelCardStyle}"' not in settings, 'obsolete workspace card row must be gone')
need('HorizontalContentAlignment" Value="Stretch"' in controls or 'HorizontalContentAlignment="Stretch"' in controls, 'context menu/menu items must stretch horizontally')
need('x:Key="{x:Static MenuItem.SeparatorStyleKey}"' in controls, 'separator must use the WPF MenuItem separator style key')
need('Margin="7,4"' not in controls, 'separator must not keep the old 7px horizontal inset')
need((core / 'Services' / 'ManagerUpdateCleanupService.cs').exists(), 'ManagerUpdateCleanupService must exist')
need('ManagerUpdateCleanupService' in appcs and 'CleanupSuccessfulUpdates' in appcs, 'startup must invoke update cleanup after successful launch')
need('V3.5 update cleanup keeps diagnostics only' in testsrc, 'C# regression test for update cleanup must be registered')

if errors:
    print('FAIL: V3.5 contract')
    for e in errors:
        print(' -', e)
    sys.exit(1)
print('PASS: V3.5 contract')
