from pathlib import Path
import json, sys

root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path('.')

def read(rel):
    p = root / rel
    return p, p.read_text(encoding='utf-8-sig')

def write(p, text):
    p.write_text(text, encoding='utf-8')

def replace_once(text, old, new, label):
    count = text.count(old)
    if count != 1:
        raise SystemExit(f'{label}: expected 1 occurrence, found {count}')
    return text.replace(old, new, 1)

p, text = read('FreeCamManager.Core/Services/ClassificationService.cs')
text = replace_once(text,
'''    public ClassificationDecision Plan(Artifact a)\n    {\n        if (Eq(a.ArtifactType, "Result"))\n''',
'''    public ClassificationDecision Plan(Artifact a)\n    {\n        var name = (a.Name ?? Path.GetFileName(a.Path ?? "") ?? "").Trim();\n        if (name.StartsWith("FreeCam_Manager_", StringComparison.OrdinalIgnoreCase))\n            return new("Manager", "50_Manager");\n        if (name.StartsWith("WW底层索引_", StringComparison.OrdinalIgnoreCase)\n            || name.StartsWith("WW底层索引库_", StringComparison.OrdinalIgnoreCase))\n            return new("IndexLibrary", "60_索引库");\n        if (Eq(a.ArtifactType, "Result"))\n''', 'classification priority')
text = replace_once(text,
'''            "stable" => "稳定版",\n            "stablecandidate" => "待确认稳定版",\n            "result" => "测试结果",\n''',
'''            "stable" => "稳定版",\n            "stablecandidate" => "待确认稳定版",\n            "manager" => "管理器",\n            "indexlibrary" => "索引库",\n            "result" => "测试结果",\n''', 'category labels')
write(p, text)

p, text = read('FreeCamManager.Core/Services/LibraryService.cs')
text = replace_once(text,
'''    public bool SetRating(string path, int rating) => Mutate(path, a =>\n    {\n        a.Rating = Math.Clamp(rating, 0, 5);\n        a.Favorite = a.Rating > 0;\n    });\n''',
'''    public bool SetRating(string path, int rating) => Mutate(path, a =>\n    {\n        a.Rating = Math.Clamp(rating, 0, 5);\n        a.Favorite = a.Rating > 0;\n        if (a.Rating == 5) a.Protected = true;\n    });\n''', 'five-star lock')
write(p, text)

p, text = read('FreeCamManager.Core/Services/InboxWatcherService.cs')
text = replace_once(text,
'''                if (IsTemporaryDownload(name)) continue;\n                if (name.StartsWith("FreeCam_Manager_", StringComparison.OrdinalIgnoreCase)) continue;\n                present.Add(path);\n''',
'''                if (IsTemporaryDownload(name)) continue;\n                present.Add(path);\n''', 'manager inbox ignore removal')
text = replace_once(text,
'''                        try { inspect = await _manifest.InspectAsync(path, ct).ConfigureAwait(false); }\n                        catch { inspect = _manifest.InspectFilename(name); inspect.Path = path; }\n                        if (_extraction.ShouldExtract(inspect))\n                        {\n''',
'''                        try { inspect = await _manifest.InspectAsync(path, ct).ConfigureAwait(false); }\n                        catch { inspect = _manifest.InspectFilename(name); inspect.Path = path; }\n                        var decision = _classification.Plan(inspect);\n                        if (decision.Category is not "Manager" and not "IndexLibrary" && _extraction.ShouldExtract(inspect))\n                        {\n''', 'special category extraction bypass')
write(p, text)

p, text = read('FreeCamManager.Core/Services/OrganizerService.cs')
text = replace_once(text,
'        foreach (var d in new[] { "01_Testing", "10_Stable", "20_Feature", "30_Experiment", "40_Result", "80_Archive", "90_Unknown" })\n',
'        foreach (var d in new[] { "01_Testing", "10_Stable", "20_Feature", "30_Experiment", "40_Result", "50_Manager", "60_索引库", "80_Archive", "90_Unknown" })\n', 'organizer roots')
write(p, text)

p, text = read('FreeCamManager.Core/Services/LibraryRebuildService.cs')
text = replace_once(text,
'''    private static readonly string[] ManagedRoots =\n        new[] { "10_Stable", "20_Feature", "30_Experiment", "40_Result", "80_Archive", "90_Unknown" };\n''',
'''    private static readonly string[] ManagedRoots =\n        new[] { "10_Stable", "20_Feature", "30_Experiment", "40_Result", "50_Manager", "60_索引库", "80_Archive", "90_Unknown" };\n''', 'rebuild roots')
text = replace_once(text,
'''            "30_Experiment" => "Experiment",\n            "40_Result" => "Result",\n            "80_Archive" when relativePath.Contains(\n''',
'''            "30_Experiment" => "Experiment",\n            "40_Result" => "Result",\n            "50_Manager" => "Manager",\n            "60_索引库" => "IndexLibrary",\n            "80_Archive" when relativePath.Contains(\n''', 'rebuild category map')
write(p, text)

p, text = read('FreeCamManager/ViewModels/ArtifactRowViewModel.cs')
text = replace_once(text,
'''        EditNotesCommand = new RelayCommand(_ => EditNotes());\n        ArchiveCommand = new AsyncRelayCommand(_ => ArchiveAsync());\n''',
'''        EditNotesCommand = new RelayCommand(_ => EditNotes());\n        ToggleProtectionCommand = new RelayCommand(_ => IsProtected = !IsProtected);\n        ArchiveCommand = new AsyncRelayCommand(_ => ArchiveAsync());\n''', 'protection command construction')
text = replace_once(text,
'''            _artifact.Rating = clamped;\n            _library.SetRating(Path, clamped);\n            _ = SaveMetadataAsync(clamped == 0 ? "已取消标星" : $"已标记 {clamped} 星");\n''',
'''            _artifact.Rating = clamped;\n            _library.SetRating(Path, clamped);\n            if (clamped == 5 && !_isProtected)\n            {\n                _artifact.Protected = true;\n                _isProtected = true;\n                OnPropertyChanged(nameof(IsProtected));\n                OnPropertyChanged(nameof(ProtectionMenuText));\n                _ = SaveMetadataAsync("已标记 5 星并锁定保护");\n                return;\n            }\n            _ = SaveMetadataAsync(clamped == 0 ? "已取消标星" : $"已标记 {clamped} 星");\n''', 'row five-star local state')
text = replace_once(text,
'''            _artifact.Protected = locked;\n            SetProperty(ref _isProtected, locked);\n            _ = SaveMetadataAsync(locked ? "已锁定保护" : "已解除保护");\n''',
'''            _artifact.Protected = locked;\n            SetProperty(ref _isProtected, locked);\n            OnPropertyChanged(nameof(ProtectionMenuText));\n            _ = SaveMetadataAsync(locked ? "已锁定保护" : "已解除保护");\n''', 'protection menu notification')
text = replace_once(text,
'''    public ICommand EditNotesCommand { get; }\n    public ICommand ArchiveCommand { get; }\n''',
'''    public ICommand EditNotesCommand { get; }\n    public ICommand ToggleProtectionCommand { get; }\n    public string ProtectionMenuText => IsProtected ? "取消锁定" : "锁定";\n    public ICommand ArchiveCommand { get; }\n''', 'protection properties')
text = replace_once(text,
'''            _isProtected = isProtected;\n            OnPropertyChanged(nameof(IsProtected));\n        }\n\n        OnPropertyChanged(nameof(DisplayName));\n''',
'''            _isProtected = isProtected;\n            OnPropertyChanged(nameof(IsProtected));\n            OnPropertyChanged(nameof(ProtectionMenuText));\n        }\n\n        OnPropertyChanged(nameof(DisplayName));\n''', 'refresh protection notification')
text = text.replace('请先点击锁图标解锁，再移到历史归档。', '请先右键选择“取消锁定”，再移到历史归档。')
text = text.replace('请先点击锁图标解锁，再删除此版本。', '请先右键选择“取消锁定”，再删除此版本。')
write(p, text)

p, text = read('FreeCamManager/Views/DevelopmentView.xaml')
old_cols = '<ColumnDefinition Width="86"/><ColumnDefinition Width="108"/><ColumnDefinition Width="24"/><ColumnDefinition Width="32"/><ColumnDefinition Width="82"/>'
new_cols = '<ColumnDefinition Width="86"/><ColumnDefinition Width="108"/><ColumnDefinition Width="24"/><ColumnDefinition Width="82"/>'
if text.count(old_cols) != 2: raise SystemExit(f'development columns: expected 2 occurrences, found {text.count(old_cols)}')
text = text.replace(old_cols, new_cols)
text = replace_once(text,
'''                    <TextBlock Grid.Column="6" Text="星级" Foreground="{DynamicResource TextSecondaryBrush}" FontWeight="SemiBold" FontSize="12"/>\n                    <TextBlock Grid.Column="7" Text="锁" Foreground="{DynamicResource TextSecondaryBrush}" FontWeight="SemiBold" FontSize="12"/>\n                    <TextBlock Grid.Column="8" Text="时间" Foreground="{DynamicResource TextSecondaryBrush}" FontWeight="SemiBold" FontSize="12"/>\n''',
'''                    <TextBlock Grid.Column="6" Text="星级" Foreground="{DynamicResource TextSecondaryBrush}" FontWeight="SemiBold" FontSize="12"/>\n                    <TextBlock Grid.Column="7" Text="时间" Foreground="{DynamicResource TextSecondaryBrush}" FontWeight="SemiBold" FontSize="12"/>\n''', 'development header labels')
text = replace_once(text,
'''                                <controls:StarRatingControl Grid.Column="6" Value="{Binding Rating, Mode=TwoWay}" VerticalAlignment="Center"/>\n                                <controls:LockToggleControl Grid.Column="7" IsLocked="{Binding IsProtected, Mode=TwoWay}" VerticalAlignment="Center"/>\n                                <TextBlock Grid.Column="8" Text="{Binding TimeDisplay}" Foreground="{DynamicResource TextMutedBrush}"/>\n''',
'''                                <controls:StarRatingControl Grid.Column="6" Value="{Binding Rating, Mode=TwoWay}" VerticalAlignment="Center"/>\n                                <TextBlock Grid.Column="7" Text="{Binding TimeDisplay}" Foreground="{DynamicResource TextMutedBrush}"/>\n''', 'development row lock removal')
text = replace_once(text,
'''                            <MenuItem Header="编辑备注" Command="{Binding SelectedRow.EditNotesCommand}"/>\n                            <Separator/>\n                            <MenuItem Header="移到历史归档" Command="{Binding SelectedRow.ArchiveCommand}"/>\n''',
'''                            <MenuItem Header="编辑备注" Command="{Binding SelectedRow.EditNotesCommand}"/>\n                            <MenuItem Header="{Binding SelectedRow.ProtectionMenuText}" Command="{Binding SelectedRow.ToggleProtectionCommand}"/>\n                            <Separator/>\n                            <MenuItem Header="移到历史归档" Command="{Binding SelectedRow.ArchiveCommand}"/>\n''', 'development context lock')
write(p, text)

p, text = read('FreeCamManager/Views/HistoryView.xaml')
old_cols = '<ColumnDefinition Width="86"/><ColumnDefinition Width="108"/><ColumnDefinition Width="24"/><ColumnDefinition Width="32"/><ColumnDefinition Width="82"/>'
new_cols = '<ColumnDefinition Width="86"/><ColumnDefinition Width="108"/><ColumnDefinition Width="24"/><ColumnDefinition Width="82"/>'
if text.count(old_cols) != 2: raise SystemExit(f'history columns: expected 2 occurrences, found {text.count(old_cols)}')
text = text.replace(old_cols, new_cols)
text = replace_once(text,
'''                    <TextBlock Grid.Column="7" Text="星级" Foreground="{DynamicResource TextSecondaryBrush}" FontWeight="SemiBold" FontSize="12"/>\n                    <TextBlock Grid.Column="8" Text="锁" Foreground="{DynamicResource TextSecondaryBrush}" FontWeight="SemiBold" FontSize="12"/>\n                    <TextBlock Grid.Column="9" Text="时间" Foreground="{DynamicResource TextSecondaryBrush}" FontWeight="SemiBold" FontSize="12"/>\n''',
'''                    <TextBlock Grid.Column="7" Text="星级" Foreground="{DynamicResource TextSecondaryBrush}" FontWeight="SemiBold" FontSize="12"/>\n                    <TextBlock Grid.Column="8" Text="时间" Foreground="{DynamicResource TextSecondaryBrush}" FontWeight="SemiBold" FontSize="12"/>\n''', 'history header labels')
text = replace_once(text,
'''                                <controls:StarRatingControl Grid.Column="7" Value="{Binding Rating, Mode=TwoWay}" VerticalAlignment="Center"/>\n                                <controls:LockToggleControl Grid.Column="8" IsLocked="{Binding IsProtected, Mode=TwoWay}" VerticalAlignment="Center"/>\n                                <TextBlock Grid.Column="9" Text="{Binding TimeDisplay}" Foreground="{DynamicResource TextMutedBrush}"/>\n''',
'''                                <controls:StarRatingControl Grid.Column="7" Value="{Binding Rating, Mode=TwoWay}" VerticalAlignment="Center"/>\n                                <TextBlock Grid.Column="8" Text="{Binding TimeDisplay}" Foreground="{DynamicResource TextMutedBrush}"/>\n''', 'history row lock removal')
text = replace_once(text,
'''                            <Separator/><MenuItem Header="编辑备注" Command="{Binding SelectedRow.EditNotesCommand}"/>\n                            <Separator/><MenuItem Header="移到历史归档" Command="{Binding SelectedRow.ArchiveCommand}"/><MenuItem Header="删除文件" Command="{Binding SelectedRow.DeleteCommand}" Foreground="{DynamicResource DangerBrush}"/>\n''',
'''                            <Separator/><MenuItem Header="编辑备注" Command="{Binding SelectedRow.EditNotesCommand}"/>\n                            <MenuItem Header="{Binding SelectedRow.ProtectionMenuText}" Command="{Binding SelectedRow.ToggleProtectionCommand}"/>\n                            <Separator/><MenuItem Header="移到历史归档" Command="{Binding SelectedRow.ArchiveCommand}"/><MenuItem Header="删除文件" Command="{Binding SelectedRow.DeleteCommand}" Foreground="{DynamicResource DangerBrush}"/>\n''', 'history context lock')
write(p, text)

for rel, old, new in [
    ('FreeCamManager/ViewModels/DevelopmentViewModel.cs', 'const double fixedColumns = 94 + 86 + 108 + 24 + 32 + 82;', 'const double fixedColumns = 94 + 86 + 108 + 24 + 82;'),
    ('FreeCamManager/ViewModels/HistoryViewModel.cs', 'const double fixedColumns = 94 + 86 + 86 + 108 + 24 + 32 + 82;', 'const double fixedColumns = 94 + 86 + 86 + 108 + 24 + 82;')
]:
    p, text = read(rel)
    text = replace_once(text, old, new, rel + ' fixed widths')
    write(p, text)

for rel, old, new in [
    ('FreeCamManager/Styles/Colors.Dark.xaml', '<SolidColorBrush x:Key="StarActiveBrush" Color="#E5B75C"/>', '<SolidColorBrush x:Key="StarActiveBrush" Color="#5B8DEF"/>'),
    ('FreeCamManager/Styles/Colors.Light.xaml', '<SolidColorBrush x:Key="StarActiveBrush" Color="#C9952F"/>', '<SolidColorBrush x:Key="StarActiveBrush" Color="#3F73D8"/>')
]:
    p, text = read(rel)
    text = replace_once(text, old, new, rel + ' star color')
    write(p, text)

p, text = read('FreeCamManager/Views/SettingsView.xaml')
start_marker = '                    <Grid Grid.Row="2" x:Name="SettingsRowThemeDiscard">\n'
end_marker = '                    <Grid Grid.Row="8" x:Name="SettingsRowTerms">\n'
start = text.find(start_marker)
end = text.find(end_marker)
if start < 0 or end < 0 or end <= start: raise SystemExit('settings layout markers not found')
layout = '''                    <Grid Grid.Row="2">\n                        <Grid.ColumnDefinitions><ColumnDefinition Width="180"/><ColumnDefinition Width="22"/><ColumnDefinition Width="220"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>\n                        <StackPanel x:Name="SettingsPrimaryColumn" Width="180" HorizontalAlignment="Left">\n                            <StackPanel Width="140" HorizontalAlignment="Left">\n                                <TextBlock Text="界面主题" Style="{StaticResource FieldLabelStyle}"/>\n                                <controls:CompactPickerControl ItemsSource="{Binding Themes}" SelectedValue="{Binding SelectedTheme, Mode=TwoWay}" Width="140" Height="34" HorizontalAlignment="Left"/>\n                            </StackPanel>\n                            <StackPanel Width="180" HorizontalAlignment="Left" Margin="0,14,0,0">\n                                <TextBlock Text="已废弃自动删除" Style="{StaticResource FieldLabelStyle}"/>\n                                <controls:CompactPickerControl ItemsSource="{Binding DiscardDeletePolicies}" SelectedValue="{Binding SelectedDiscardDeletePolicy, Mode=TwoWay}" Width="180" Height="34" HorizontalAlignment="Left"/>\n                            </StackPanel>\n                            <StackPanel Width="180" Margin="0,14,0,0">\n                                <TextBlock Text="隐藏 FreeCam_ 文件名前缀" Style="{StaticResource FieldLabelStyle}"/>\n                                <ToggleButton Style="{StaticResource SwitchToggleStyle}" IsChecked="{Binding HideFreeCamPrefix, Mode=TwoWay}" HorizontalAlignment="Left" VerticalAlignment="Center" Margin="0,5,0,0"/>\n                            </StackPanel>\n                        </StackPanel>\n                        <StackPanel x:Name="SettingsAliasColumn" Grid.Column="2" Width="220" HorizontalAlignment="Left">\n                            <StackPanel Width="220" ToolTip="只改变列表显示，不修改磁盘文件名、路径、Manifest、BuildId 或 Result 配对。">\n                                <TextBlock Text="显示文件名中文别名" Style="{StaticResource FieldLabelStyle}"/>\n                                <ToggleButton Style="{StaticResource SwitchToggleStyle}" IsChecked="{Binding ShowFilenameAliases, Mode=TwoWay}" HorizontalAlignment="Left" VerticalAlignment="Center" Margin="0,5,0,0"/>\n                            </StackPanel>\n                            <StackPanel Width="220" Margin="0,14,0,0" ToolTip="开启后功能列显示中文；悬浮时显示原始字段。">\n                                <TextBlock Text="功能中文显示" Style="{StaticResource FieldLabelStyle}"/>\n                                <ToggleButton Style="{StaticResource SwitchToggleStyle}" IsChecked="{Binding ShowFeatureAliases, Mode=TwoWay}" HorizontalAlignment="Left" VerticalAlignment="Center" Margin="0,5,0,0"/>\n                            </StackPanel>\n                            <StackPanel Width="220" Margin="0,14,0,0" ToolTip="开启后阶段列显示中文；悬浮时显示原始字段。">\n                                <TextBlock Text="阶段中文显示" Style="{StaticResource FieldLabelStyle}"/>\n                                <ToggleButton Style="{StaticResource SwitchToggleStyle}" IsChecked="{Binding ShowStageAliases, Mode=TwoWay}" HorizontalAlignment="Left" VerticalAlignment="Center" Margin="0,5,0,0"/>\n                            </StackPanel>\n                        </StackPanel>\n                    </Grid>\n\n'''
text = text[:start] + layout + text[end:]
write(p, text)

p, text = read('FreeCamManager.Tests/Program.cs')
text = text.replace('await Run("Inbox two-scan stability and manager ignore", InboxWatcherStability);',
                    'await Run("Inbox two-scan stability and manager classification", InboxWatcherStability);')
text = replace_once(text,
'''        await watcher.ScanOnceAsync();\n        await watcher.ScanOnceAsync();\n        Assert(File.Exists(manager), "Manager delivery zip must be ignored");\n        Assert(library.Snapshot().Count == 0, "Manager delivery zip polluted project index");\n''',
'''        await watcher.ScanOnceAsync();\n        await watcher.ScanOnceAsync();\n        Assert(!File.Exists(manager), "Manager delivery zip should be organized after the stable scan");\n        Assert(File.Exists(Path.Combine(root, "50_Manager", "FreeCam_Manager_v3.0.zip")), "Manager delivery zip was not moved to 50_Manager");\n        Assert(library.Snapshot().Any(x => x.Category == "Manager"), "Manager delivery zip missing from project index");\n        Assert(!Directory.Exists(Path.Combine(root, "01_Testing", "FreeCam_Manager_v3.0")), "Manager delivery zip must not be extracted into 01_Testing");\n''', 'watcher manager regression')
write(p, text)

p, text = read('FreeCamManager/FreeCamManager.csproj')
text = replace_once(text, '<Version>3.8.1</Version>', '<Version>3.9.0</Version>', 'project version')
write(p, text)

manifest_path = root / 'BUILD_MANIFEST.json'
manifest = json.loads(manifest_path.read_text(encoding='utf-8-sig'))
manifest.update({
    'Version': 'V3.9', 'BuildName': 'FreeCam_Manager_V3.9', 'Base': 'FreeCam_Manager_V3.8_Fix1',
    'Branch': 'feature/manager-v3.9-ui-classification',
    'Feature': 'UI Cleanup + Manager/Index Library Classification + Release Gate',
    'Stage': 'Release', 'BuildId': 'MANAGER-V39-STABLE-20260914'
})
for item in [
    'lock column removed; protection moved to row context menu',
    'five-star rating auto-locks without auto-unlock',
    'theme-blue active rating indicator',
    'display-and-behavior settings two-column layout',
    '50_Manager and 60_索引库 inbox classification',
    'final-package updater release gate'
]:
    if item not in manifest.setdefault('Modules', []): manifest['Modules'].append(item)
manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')

readme = root / 'V38_README.txt'
if readme.exists():
    t = readme.read_text(encoding='utf-8-sig')
    lines = t.splitlines()
    if lines: lines[0] = 'FreeCam Manager V3.9'
    readme.write_text('\n'.join(lines) + '\n', encoding='utf-8-sig')

print('Applied V3.9 production changes to', root)
