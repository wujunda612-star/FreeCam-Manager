from pathlib import Path
import sys, zipfile, re

package = Path(sys.argv[1]) if len(sys.argv) > 1 else Path('manager/packages/FreeCam_Manager_V3.9.2_Source.zip')

with zipfile.ZipFile(package) as zf:
    history = zf.read('src-wpf/FreeCamManager/Views/HistoryView.xaml').decode('utf-8-sig')
    history_vm = zf.read('src-wpf/FreeCamManager/ViewModels/HistoryViewModel.cs').decode('utf-8-sig')
    csproj = zf.read('src-wpf/FreeCamManager/FreeCamManager.csproj').decode('utf-8-sig')

errors = []

# 1) Duplicate-copy delete menu must align like the other history context-menu items.
dup_match = re.search(r'<MenuItem Header="删除此副本"[^>]*>', history)
if not dup_match:
    errors.append('missing 删除此副本 menu item')
elif 'HorizontalContentAlignment="Left"' not in dup_match.group(0):
    errors.append('删除此副本 menu item is not explicitly left-aligned')

# 2) Star/time columns need a visible gutter: expand the star column from 24 to 40 in header + rows.
if history.count('<ColumnDefinition Width="40"/><ColumnDefinition Width="82"/>') < 2:
    errors.append('history star/time columns do not have the V3.9.3 16px gutter in both header and rows')
if 'const double fixedColumns = 94 + 86 + 86 + 108 + 40 + 82;' not in history_vm:
    errors.append('HistoryViewModel fixed column width was not updated for the wider star column')

if '<Version>3.9.3</Version>' not in csproj:
    errors.append('project version is not 3.9.3')

if errors:
    print('V3.9.3 contract FAILED:')
    for e in errors:
        print(' -', e)
    raise SystemExit(1)

print('V3.9.3 contract PASS')
