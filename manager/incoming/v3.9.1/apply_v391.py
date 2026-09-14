from pathlib import Path
import json
import sys

root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path('.')


def read(rel: str):
    p = root / rel
    return p, p.read_text(encoding='utf-8-sig')


def write(p: Path, text: str):
    p.write_text(text, encoding='utf-8')


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise SystemExit(f'{label}: expected 1 occurrence, found {count}')
    return text.replace(old, new, 1)


# V3.9.1: the six "显示与行为" settings must be one horizontal row.
p, text = read('FreeCamManager/Views/SettingsView.xaml')

old_rows = '''                    <Grid.RowDefinitions>\n                        <RowDefinition Height="Auto"/>\n                        <RowDefinition Height="14"/>\n                        <RowDefinition Height="Auto"/>\n                        <RowDefinition Height="14"/>\n                        <RowDefinition Height="Auto"/>\n                        <RowDefinition Height="14"/>\n                        <RowDefinition Height="Auto"/>\n                        <RowDefinition Height="14"/>\n                        <RowDefinition Height="Auto"/>\n                    </Grid.RowDefinitions>\n'''
new_rows = '''                    <Grid.RowDefinitions>\n                        <RowDefinition Height="Auto"/>\n                        <RowDefinition Height="14"/>\n                        <RowDefinition Height="Auto"/>\n                        <RowDefinition Height="18"/>\n                        <RowDefinition Height="Auto"/>\n                    </Grid.RowDefinitions>\n'''
text = replace_once(text, old_rows, new_rows, 'display card row definitions')

primary_marker = 'x:Name="SettingsPrimaryColumn"'
primary_pos = text.find(primary_marker)
if primary_pos < 0:
    raise SystemExit('settings layout: SettingsPrimaryColumn marker not found')
block_start = text.rfind('                    <Grid Grid.Row="2">\n', 0, primary_pos)
block_end = text.find('                    <Grid Grid.Row="8" x:Name="SettingsRowTerms">\n', primary_pos)
if block_start < 0 or block_end < 0 or block_end <= block_start:
    raise SystemExit('settings layout: failed to locate old two-column block')

single_row = '''                    <Grid Grid.Row="2" x:Name="SettingsBehaviorSingleRow" HorizontalAlignment="Left">\n                        <Grid.ColumnDefinitions>\n                            <ColumnDefinition Width="120"/>\n                            <ColumnDefinition Width="12"/>\n                            <ColumnDefinition Width="145"/>\n                            <ColumnDefinition Width="12"/>\n                            <ColumnDefinition Width="165"/>\n                            <ColumnDefinition Width="12"/>\n                            <ColumnDefinition Width="165"/>\n                            <ColumnDefinition Width="12"/>\n                            <ColumnDefinition Width="110"/>\n                            <ColumnDefinition Width="12"/>\n                            <ColumnDefinition Width="110"/>\n                        </Grid.ColumnDefinitions>\n\n                        <StackPanel Grid.Column="0">\n                            <TextBlock Text="界面主题" Style="{StaticResource FieldLabelStyle}"/>\n                            <controls:CompactPickerControl ItemsSource="{Binding Themes}" SelectedValue="{Binding SelectedTheme, Mode=TwoWay}" Width="120" Height="34" HorizontalAlignment="Left"/>\n                        </StackPanel>\n                        <StackPanel Grid.Column="2">\n                            <TextBlock Text="已废弃自动删除" Style="{StaticResource FieldLabelStyle}"/>\n                            <controls:CompactPickerControl ItemsSource="{Binding DiscardDeletePolicies}" SelectedValue="{Binding SelectedDiscardDeletePolicy, Mode=TwoWay}" Width="145" Height="34" HorizontalAlignment="Left"/>\n                        </StackPanel>\n                        <StackPanel Grid.Column="4">\n                            <TextBlock Text="隐藏 FreeCam_ 文件名前缀" Style="{StaticResource FieldLabelStyle}"/>\n                            <ToggleButton Style="{StaticResource SwitchToggleStyle}" IsChecked="{Binding HideFreeCamPrefix, Mode=TwoWay}" HorizontalAlignment="Left" VerticalAlignment="Center" Margin="0,5,0,0"/>\n                        </StackPanel>\n                        <StackPanel Grid.Column="6" ToolTip="只改变列表显示，不修改磁盘文件名、路径、Manifest、BuildId 或 Result 配对。">\n                            <TextBlock Text="显示文件名中文别名" Style="{StaticResource FieldLabelStyle}"/>\n                            <ToggleButton Style="{StaticResource SwitchToggleStyle}" IsChecked="{Binding ShowFilenameAliases, Mode=TwoWay}" HorizontalAlignment="Left" VerticalAlignment="Center" Margin="0,5,0,0"/>\n                        </StackPanel>\n                        <StackPanel Grid.Column="8" ToolTip="开启后功能列显示中文；悬浮时显示原始字段。">\n                            <TextBlock Text="功能中文显示" Style="{StaticResource FieldLabelStyle}"/>\n                            <ToggleButton Style="{StaticResource SwitchToggleStyle}" IsChecked="{Binding ShowFeatureAliases, Mode=TwoWay}" HorizontalAlignment="Left" VerticalAlignment="Center" Margin="0,5,0,0"/>\n                        </StackPanel>\n                        <StackPanel Grid.Column="10" ToolTip="开启后阶段列显示中文；悬浮时显示原始字段。">\n                            <TextBlock Text="阶段中文显示" Style="{StaticResource FieldLabelStyle}"/>\n                            <ToggleButton Style="{StaticResource SwitchToggleStyle}" IsChecked="{Binding ShowStageAliases, Mode=TwoWay}" HorizontalAlignment="Left" VerticalAlignment="Center" Margin="0,5,0,0"/>\n                        </StackPanel>\n                    </Grid>\n\n'''
text = text[:block_start] + single_row + text[block_end:]
text = replace_once(
    text,
    '<Grid Grid.Row="8" x:Name="SettingsRowTerms">',
    '<Grid Grid.Row="4" x:Name="SettingsRowTerms">',
    'terms row index',
)
write(p, text)

# Version metadata.
p, text = read('FreeCamManager/FreeCamManager.csproj')
text = replace_once(text, '<Version>3.9.0</Version>', '<Version>3.9.1</Version>', 'project version')
write(p, text)

manifest_path = root / 'BUILD_MANIFEST.json'
manifest = json.loads(manifest_path.read_text(encoding='utf-8-sig'))
manifest.update({
    'Version': 'V3.9.1',
    'BuildName': 'FreeCam_Manager_V3.9.1',
    'Base': 'FreeCam_Manager_V3.9',
    'Branch': 'feature/manager-v3.9.1-ui-row',
    'Feature': 'Display & Behavior Single-Row Layout',
    'Stage': 'Release',
    'BuildId': 'MANAGER-V391-20260914',
})
manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')

print('Applied FreeCam Manager V3.9.1 layout patch')
