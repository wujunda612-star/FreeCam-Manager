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


# 1) Settings: V3.9.1 6x1 -> V3.9.2 3x2.
p, text = read('FreeCamManager/Views/SettingsView.xaml')
marker = 'x:Name="SettingsBehaviorSingleRow"'
pos = text.find(marker)
if pos < 0:
    raise SystemExit('settings layout: V3.9.1 single-row marker not found')
block_start = text.rfind('                    <Grid Grid.Row="2"', 0, pos)
block_end = text.find('                    <Grid Grid.Row="4" x:Name="SettingsRowTerms">', pos)
if block_start < 0 or block_end < 0 or block_end <= block_start:
    raise SystemExit('settings layout: failed to locate V3.9.1 behavior block')

grid = '''                    <Grid Grid.Row="2" x:Name="SettingsBehaviorGrid" HorizontalAlignment="Left">
                        <Grid.RowDefinitions>
                            <RowDefinition Height="Auto"/>
                            <RowDefinition Height="14"/>
                            <RowDefinition Height="Auto"/>
                        </Grid.RowDefinitions>
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="250"/>
                            <ColumnDefinition Width="24"/>
                            <ColumnDefinition Width="250"/>
                            <ColumnDefinition Width="24"/>
                            <ColumnDefinition Width="250"/>
                        </Grid.ColumnDefinitions>

                        <StackPanel Grid.Row="0" Grid.Column="0">
                            <TextBlock Text="界面主题" Style="{StaticResource FieldLabelStyle}"/>
                            <controls:CompactPickerControl ItemsSource="{Binding Themes}" SelectedValue="{Binding SelectedTheme, Mode=TwoWay}" Width="120" Height="34" HorizontalAlignment="Left"/>
                        </StackPanel>
                        <StackPanel Grid.Row="0" Grid.Column="2">
                            <TextBlock Text="已废弃自动删除" Style="{StaticResource FieldLabelStyle}"/>
                            <controls:CompactPickerControl ItemsSource="{Binding DiscardDeletePolicies}" SelectedValue="{Binding SelectedDiscardDeletePolicy, Mode=TwoWay}" Width="145" Height="34" HorizontalAlignment="Left"/>
                        </StackPanel>
                        <StackPanel Grid.Row="0" Grid.Column="4">
                            <TextBlock Text="隐藏 FreeCam_ 文件名前缀" Style="{StaticResource FieldLabelStyle}"/>
                            <ToggleButton Style="{StaticResource SwitchToggleStyle}" IsChecked="{Binding HideFreeCamPrefix, Mode=TwoWay}" HorizontalAlignment="Left" VerticalAlignment="Center" Margin="0,5,0,0"/>
                        </StackPanel>

                        <StackPanel Grid.Row="2" Grid.Column="0" ToolTip="只改变列表显示，不修改磁盘文件名、路径、Manifest、BuildId 或 Result 配对。">
                            <TextBlock Text="显示文件名中文别名" Style="{StaticResource FieldLabelStyle}"/>
                            <ToggleButton Style="{StaticResource SwitchToggleStyle}" IsChecked="{Binding ShowFilenameAliases, Mode=TwoWay}" HorizontalAlignment="Left" VerticalAlignment="Center" Margin="0,5,0,0"/>
                        </StackPanel>
                        <StackPanel Grid.Row="2" Grid.Column="2" ToolTip="开启后功能列显示中文；悬浮时显示原始字段。">
                            <TextBlock Text="功能中文显示" Style="{StaticResource FieldLabelStyle}"/>
                            <ToggleButton Style="{StaticResource SwitchToggleStyle}" IsChecked="{Binding ShowFeatureAliases, Mode=TwoWay}" HorizontalAlignment="Left" VerticalAlignment="Center" Margin="0,5,0,0"/>
                        </StackPanel>
                        <StackPanel Grid.Row="2" Grid.Column="4" ToolTip="开启后阶段列显示中文；悬浮时显示原始字段。">
                            <TextBlock Text="阶段中文显示" Style="{StaticResource FieldLabelStyle}"/>
                            <ToggleButton Style="{StaticResource SwitchToggleStyle}" IsChecked="{Binding ShowStageAliases, Mode=TwoWay}" HorizontalAlignment="Left" VerticalAlignment="Center" Margin="0,5,0,0"/>
                        </StackPanel>
                    </Grid>

'''
text = text[:block_start] + grid + text[block_end:]
write(p, text)

# 2) Core UI regression contract: require 3x2 and reject V3.9.1 single row.
p, text = read('FreeCamManager.Tests/Program.cs')
contract_marker = 'V3.9.1 display-and-behavior settings must use one explicit horizontal row'
contract_pos = text.find(contract_marker)
if contract_pos < 0:
    raise SystemExit('core tests: V3.9.1 settings contract marker not found')
contract_start = text.rfind('        Assert(settings.Contains("x:Name=\\"SettingsBehaviorSingleRow\\""', 0, contract_pos)
expected_pos = text.find('        var expectedLabels = new[]', contract_pos)
foreach_pos = text.find('        foreach (var label in expectedLabels)', expected_pos)
contract_end = text.find('        }\n', foreach_pos)
if min(contract_start, expected_pos, foreach_pos, contract_end) < 0:
    raise SystemExit('core tests: failed to locate V3.9.1 settings contract block')
contract_end += len('        }\n')
new_contract = '''        Assert(settings.Contains("x:Name=\\"SettingsBehaviorGrid\\"", StringComparison.Ordinal),
            "V3.9.2 display-and-behavior settings must use a 3x2 grid");
        Assert(!settings.Contains("x:Name=\\"SettingsBehaviorSingleRow\\"", StringComparison.Ordinal),
            "V3.9.2 must not retain the V3.9.1 single-row settings layout");
        var gridStart = settings.IndexOf("x:Name=\\"SettingsBehaviorGrid\\"", StringComparison.Ordinal);
        var termsStart = settings.IndexOf("x:Name=\\"SettingsRowTerms\\"", StringComparison.Ordinal);
        Assert(gridStart >= 0 && termsStart > gridStart, "V3.9.2 settings grid marker is missing or out of order");
        var behaviorGrid = settings[gridStart..termsStart];
        var expectedCells = new[]
        {
            ("Grid.Row=\\"0\\" Grid.Column=\\"0\\"", "界面主题"),
            ("Grid.Row=\\"0\\" Grid.Column=\\"2\\"", "已废弃自动删除"),
            ("Grid.Row=\\"0\\" Grid.Column=\\"4\\"", "隐藏 FreeCam_ 文件名前缀"),
            ("Grid.Row=\\"2\\" Grid.Column=\\"0\\"", "显示文件名中文别名"),
            ("Grid.Row=\\"2\\" Grid.Column=\\"2\\"", "功能中文显示"),
            ("Grid.Row=\\"2\\" Grid.Column=\\"4\\"", "阶段中文显示")
        };
        foreach (var (cell, label) in expectedCells)
        {
            var cellPos = behaviorGrid.IndexOf(cell, StringComparison.Ordinal);
            var labelPos = cellPos < 0 ? -1 : behaviorGrid.IndexOf(label, cellPos, StringComparison.Ordinal);
            Assert(cellPos >= 0 && labelPos >= cellPos && labelPos - cellPos < 900,
                $"V3.9.2 settings item is missing from expected 3x2 cell: {label}");
        }
'''
text = text[:contract_start] + new_contract + text[contract_end:]
write(p, text)

# 3) History duplicate rows: add a command that deletes only the selected duplicate file/index record.
p, text = read('FreeCamManager/ViewModels/ArtifactRowViewModel.cs')
text = replace_once(
    text,
    '        DeleteVersionCommand = new AsyncRelayCommand(_ => DeleteVersionAsync());\n',
    '        DeleteVersionCommand = new AsyncRelayCommand(_ => DeleteVersionAsync());\n        DeleteDuplicateCopyCommand = new AsyncRelayCommand(_ => DeleteDuplicateCopyAsync());\n',
    'duplicate command initialization',
)
text = replace_once(
    text,
    '    public ICommand DeleteVersionCommand { get; }\n    public ICommand DeleteCommand => DeleteVersionCommand;\n',
    '    public ICommand DeleteVersionCommand { get; }\n    public ICommand DeleteDuplicateCopyCommand { get; }\n    public ICommand DeleteCommand => DeleteVersionCommand;\n',
    'duplicate command property',
)
needle = '    private async Task DeleteVersionAsync()\n'
method_pos = text.find(needle)
if method_pos < 0:
    raise SystemExit('duplicate delete: DeleteVersionAsync marker not found')
duplicate_method = '''    private async Task DeleteDuplicateCopyAsync()
    {
        if (!IsDuplicate) return;
        if (IsProtected) { _dialogs.Info("已锁定", "请先右键选择“取消锁定”，再删除此副本。"); return; }
        if (!_dialogs.Confirm("删除此副本", $"只删除当前重复副本？\\n\\n{Name}\\n\\n原始版本、测试目录和 Result / 日志不会删除。")) return;
        try
        {
            await _organizer.DeleteAsync(Path);
            _statusSink("已删除重复副本: " + Name);
            await _refreshAll();
        }
        catch (Exception ex) { _dialogs.Error("删除副本失败", ex.Message); }
    }

'''
text = text[:method_pos] + duplicate_method + text[method_pos:]
write(p, text)

# 4) History context menu: duplicates get a dedicated action; development page is untouched.
p, text = read('FreeCamManager/Views/HistoryView.xaml')
old_menu = '                            <Separator/><MenuItem Header="移到历史归档" Command="{Binding SelectedRow.ArchiveCommand}"/><MenuItem Header="删除文件" Command="{Binding SelectedRow.DeleteCommand}" Foreground="{DynamicResource DangerBrush}"/>\n'
new_menu = '''                            <Separator/>
                            <MenuItem Header="移到历史归档" Command="{Binding SelectedRow.ArchiveCommand}"/>
                            <MenuItem Header="删除文件" Command="{Binding SelectedRow.DeleteCommand}" Foreground="{DynamicResource DangerBrush}">
                                <MenuItem.Style>
                                    <Style TargetType="MenuItem">
                                        <Setter Property="Visibility" Value="Visible"/>
                                        <Style.Triggers>
                                            <DataTrigger Binding="{Binding SelectedRow.IsDuplicate}" Value="True"><Setter Property="Visibility" Value="Collapsed"/></DataTrigger>
                                        </Style.Triggers>
                                    </Style>
                                </MenuItem.Style>
                            </MenuItem>
                            <MenuItem Header="删除此副本" Command="{Binding SelectedRow.DeleteDuplicateCopyCommand}" Foreground="{DynamicResource DangerBrush}">
                                <MenuItem.Style>
                                    <Style TargetType="MenuItem">
                                        <Setter Property="Visibility" Value="Collapsed"/>
                                        <Style.Triggers>
                                            <DataTrigger Binding="{Binding SelectedRow.IsDuplicate}" Value="True"><Setter Property="Visibility" Value="Visible"/></DataTrigger>
                                        </Style.Triggers>
                                    </Style>
                                </MenuItem.Style>
                            </MenuItem>
'''
text = replace_once(text, old_menu, new_menu, 'history duplicate menu')
write(p, text)

# 5) Version / build metadata.
p, text = read('FreeCamManager/FreeCamManager.csproj')
text = replace_once(text, '<Version>3.9.1</Version>', '<Version>3.9.2</Version>', 'project version')
write(p, text)

manifest_path = root / 'BUILD_MANIFEST.json'
manifest = json.loads(manifest_path.read_text(encoding='utf-8-sig'))
manifest.update({
    'Version': 'V3.9.2',
    'BuildName': 'FreeCam_Manager_V3.9.2',
    'Base': 'FreeCam_Manager_V3.9.1',
    'Branch': 'feature/manager-v3.9.2-settings-grid',
    'Feature': 'Settings 3x2 + History Duplicate Copy Delete',
    'Stage': 'Release',
    'BuildId': 'MANAGER-V392-20260915',
})
manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')

print('Applied FreeCam Manager V3.9.2 patch')
