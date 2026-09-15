from pathlib import Path
import json
import sys

root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path('.')


def read(rel: str):
    p = root / rel
    return p, p.read_text(encoding='utf-8-sig')


def write(p: Path, text: str):
    p.write_text(text, encoding='utf-8')


def replace_exact(text: str, old: str, new: str, expected: int, label: str) -> str:
    count = text.count(old)
    if count != expected:
        raise SystemExit(f'{label}: expected {expected} occurrence(s), found {count}')
    return text.replace(old, new)


# 1) History context menu: keep duplicate-copy deletion aligned with all other menu items.
p, text = read('FreeCamManager/Views/HistoryView.xaml')
old_dup = '<MenuItem Header="删除此副本" Command="{Binding SelectedRow.DeleteDuplicateCopyCommand}" Foreground="{DynamicResource DangerBrush}">'
new_dup = '<MenuItem Header="删除此副本" Command="{Binding SelectedRow.DeleteDuplicateCopyCommand}" Foreground="{DynamicResource DangerBrush}" HorizontalContentAlignment="Left">'
text = replace_exact(text, old_dup, new_dup, 1, 'duplicate-copy menu alignment')

# 2) History list spacing: widen the star column by 16 px in both header and item rows.
old_cols = '<ColumnDefinition Width="24"/><ColumnDefinition Width="82"/>'
new_cols = '<ColumnDefinition Width="40"/><ColumnDefinition Width="82"/>'
text = replace_exact(text, old_cols, new_cols, 2, 'history star/time gutter')
write(p, text)

# Keep responsive file-column calculation consistent with the XAML fixed widths.
p, text = read('FreeCamManager/ViewModels/HistoryViewModel.cs')
text = replace_exact(
    text,
    'const double fixedColumns = 94 + 86 + 86 + 108 + 24 + 82;',
    'const double fixedColumns = 94 + 86 + 86 + 108 + 40 + 82;',
    1,
    'history fixed column widths',
)
write(p, text)

# 3) Version/build metadata.
p, text = read('FreeCamManager/FreeCamManager.csproj')
text = replace_exact(text, '<Version>3.9.2</Version>', '<Version>3.9.3</Version>', 1, 'project version')
write(p, text)

manifest_path = root / 'BUILD_MANIFEST.json'
manifest = json.loads(manifest_path.read_text(encoding='utf-8-sig'))
manifest.update({
    'Version': 'V3.9.3',
    'BuildName': 'FreeCam_Manager_V3.9.3',
    'Base': 'FreeCam_Manager_V3.9.2',
    'Branch': 'feature/manager-v3.9.3-ui-polish',
    'Feature': 'History Menu Alignment + Star Time Spacing',
    'Stage': 'Release',
    'BuildId': 'MANAGER-V393-20260915',
})
manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')

print('Applied FreeCam Manager V3.9.3 patch')
