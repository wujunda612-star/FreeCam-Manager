from pathlib import Path
import sys
import zipfile

package = Path(sys.argv[1]) if len(sys.argv) > 1 else Path('manager/packages/FreeCam_Manager_V3.9.2_Source.zip')
if not package.exists():
    raise SystemExit(f'package not found: {package}')

with zipfile.ZipFile(package) as zf:
    settings = zf.read('src-wpf/FreeCamManager/Views/SettingsView.xaml').decode('utf-8-sig')
    csproj = zf.read('src-wpf/FreeCamManager/FreeCamManager.csproj').decode('utf-8-sig')
    tests = zf.read('src-wpf/FreeCamManager.Tests/Program.cs').decode('utf-8-sig')

assert '<Version>3.9.2</Version>' in csproj, 'project version must be 3.9.2'
assert 'x:Name="SettingsBehaviorGrid"' in settings, '3x2 settings grid marker missing'
assert 'x:Name="SettingsBehaviorSingleRow"' not in settings, 'old 6x1 row marker must be removed'

required = [
    ('Grid.Row="0" Grid.Column="0"', '界面主题'),
    ('Grid.Row="0" Grid.Column="2"', '已废弃自动删除'),
    ('Grid.Row="0" Grid.Column="4"', '隐藏 FreeCam_ 文件名前缀'),
    ('Grid.Row="2" Grid.Column="0"', '显示文件名中文别名'),
    ('Grid.Row="2" Grid.Column="2"', '功能中文显示'),
    ('Grid.Row="2" Grid.Column="4"', '阶段中文显示'),
]
for marker, label in required:
    marker_pos = settings.find(marker)
    label_pos = settings.find(label, marker_pos)
    assert marker_pos >= 0 and label_pos >= marker_pos and label_pos - marker_pos < 900, f'{label} is not in expected 3x2 cell {marker}'

assert 'V3.9.2 display-and-behavior settings must use a 3x2 grid' in tests, 'core regression contract was not updated for V3.9.2'
assert 'SettingsBehaviorGrid' in tests and 'SettingsBehaviorSingleRow' in tests, 'core regression contract must reject old single-row layout'

print('V3.9.2 contract PASS')
