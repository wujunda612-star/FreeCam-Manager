from pathlib import Path
import sys

root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path('.')
program = root / 'FreeCamManager.Tests' / 'Program.cs'
text = program.read_text(encoding='utf-8-sig')

run_anchor = '        await Run("V3.8 path rebase merges duplicate target safely", V38PathRebaseMergesDuplicateTargetSafely);\n'
run_lines = (
    '        await Run("V3.9 Manager and index library classification", V39ManagerAndIndexLibraryClassification);\n'
    '        await Run("V3.9 UI protection and settings contract", V39UiProtectionAndSettingsContract);\n'
)
if 'V3.9 Manager and index library classification' not in text:
    if run_anchor not in text:
        raise SystemExit('run anchor not found')
    text = text.replace(run_anchor, run_anchor + run_lines, 1)

method_anchor = '    private static async Task V371CorruptedLibraryRestoresFromBackup()\n'
methods = r'''    private static Task V39ManagerAndIndexLibraryClassification()
    {
        var classification = new ClassificationService();

        var manager = classification.Plan(new Artifact
        {
            Name = "FreeCam_Manager_V3.9_Source.zip",
            ArtifactType = "Source",
            BuildType = "Stable"
        });
        Assert(manager.Category == "Manager", "FreeCam_Manager_* must be classified as Manager before normal build rules");
        Assert(manager.RelativeDirectory == "50_Manager", "Manager artifacts must move to 50_Manager");
        Assert(classification.CategoryLabel(new Artifact { Category = "Manager" }) == "管理器", "Manager category label missing");

        foreach (var name in new[] { "WW底层索引_Fix12_VisualFix1.zip", "WW底层索引库_R40.4.0_LookAt正式回填.zip" })
        {
            var index = classification.Plan(new Artifact { Name = name, BuildType = "Probe", ArtifactType = "Runtime" });
            Assert(index.Category == "IndexLibrary", $"{name} must be classified as IndexLibrary before Probe/Experiment rules");
            Assert(index.RelativeDirectory == "60_索引库", $"{name} must move to 60_索引库");
        }
        Assert(classification.CategoryLabel(new Artifact { Category = "IndexLibrary" }) == "索引库", "IndexLibrary category label missing");
        return Task.CompletedTask;
    }

    private static Task V39UiProtectionAndSettingsContract()
    {
        var sourceRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "FreeCamManager"));
        var coreRoot = Path.GetFullPath(Path.Combine(sourceRoot, "..", "FreeCamManager.Core"));
        var dev = File.ReadAllText(Path.Combine(sourceRoot, "Views", "DevelopmentView.xaml"));
        var history = File.ReadAllText(Path.Combine(sourceRoot, "Views", "HistoryView.xaml"));
        var settings = File.ReadAllText(Path.Combine(sourceRoot, "Views", "SettingsView.xaml"));
        var row = File.ReadAllText(Path.Combine(sourceRoot, "ViewModels", "ArtifactRowViewModel.cs"));
        var dark = File.ReadAllText(Path.Combine(sourceRoot, "Styles", "Colors.Dark.xaml"));
        var light = File.ReadAllText(Path.Combine(sourceRoot, "Styles", "Colors.Light.xaml"));
        var watcher = File.ReadAllText(Path.Combine(coreRoot, "Services", "InboxWatcherService.cs"));
        var organizer = File.ReadAllText(Path.Combine(coreRoot, "Services", "OrganizerService.cs"));
        var rebuild = File.ReadAllText(Path.Combine(coreRoot, "Services", "LibraryRebuildService.cs"));

        Assert(!dev.Contains("LockToggleControl", StringComparison.Ordinal) && !dev.Contains("Text=\"锁\"", StringComparison.Ordinal),
            "development list must not keep a dedicated lock column");
        Assert(!history.Contains("LockToggleControl", StringComparison.Ordinal) && !history.Contains("Text=\"锁\"", StringComparison.Ordinal),
            "history list must not keep a dedicated lock column");
        Assert(dev.Contains("ProtectionMenuText", StringComparison.Ordinal) && dev.Contains("ToggleProtectionCommand", StringComparison.Ordinal)
            && history.Contains("ProtectionMenuText", StringComparison.Ordinal) && history.Contains("ToggleProtectionCommand", StringComparison.Ordinal),
            "lock/unlock must be available from the row context menu");
        Assert(row.Contains("clamped == 5", StringComparison.Ordinal) && row.Contains("已标记 5 星并锁定保护", StringComparison.Ordinal),
            "setting 5 stars must also lock the artifact");
        Assert(row.Contains("ProtectionMenuText", StringComparison.Ordinal) && row.Contains("ToggleProtectionCommand", StringComparison.Ordinal),
            "row view model must expose context-menu protection state and command");

        Assert(dark.Contains("x:Key=\"StarActiveBrush\" Color=\"#5B8DEF\"", StringComparison.Ordinal),
            "dark theme active star color must match AccentBrush blue");
        Assert(light.Contains("x:Key=\"StarActiveBrush\" Color=\"#3F73D8\"", StringComparison.Ordinal),
            "light theme active star color must match AccentBrush blue");

        Assert(settings.Contains("x:Name=\"SettingsPrimaryColumn\"", StringComparison.Ordinal)
            && settings.Contains("x:Name=\"SettingsAliasColumn\"", StringComparison.Ordinal),
            "display-and-behavior settings must be organized as two explicit vertical columns");
        var primaryStart = settings.IndexOf("x:Name=\"SettingsPrimaryColumn\"", StringComparison.Ordinal);
        var aliasStart = settings.IndexOf("x:Name=\"SettingsAliasColumn\"", StringComparison.Ordinal);
        Assert(primaryStart >= 0 && aliasStart > primaryStart, "settings column markers are missing or out of order");
        var primary = settings[primaryStart..aliasStart];
        var aliases = settings[aliasStart..];
        Assert(primary.Contains("界面主题", StringComparison.Ordinal) && primary.Contains("已废弃自动删除", StringComparison.Ordinal)
            && primary.Contains("隐藏 FreeCam_ 文件名前缀", StringComparison.Ordinal),
            "left settings column must contain theme, auto-delete, and hide-prefix controls");
        Assert(aliases.Contains("显示文件名中文别名", StringComparison.Ordinal) && aliases.Contains("功能中文显示", StringComparison.Ordinal)
            && aliases.Contains("阶段中文显示", StringComparison.Ordinal),
            "right settings column must contain all three Chinese display controls");

        Assert(!watcher.Contains("if (name.StartsWith(\"FreeCam_Manager_\"", StringComparison.Ordinal),
            "inbox watcher must no longer ignore Manager packages");
        Assert(watcher.Contains("decision.Category is not \"Manager\" and not \"IndexLibrary\"", StringComparison.Ordinal),
            "Manager/IndexLibrary packages must bypass 01_Testing extraction");
        Assert(organizer.Contains("\"50_Manager\"", StringComparison.Ordinal) && organizer.Contains("\"60_索引库\"", StringComparison.Ordinal),
            "organizer must create Manager and IndexLibrary roots");
        Assert(rebuild.Contains("\"50_Manager\"", StringComparison.Ordinal) && rebuild.Contains("\"60_索引库\"", StringComparison.Ordinal)
            && rebuild.Contains("\"50_Manager\" => \"Manager\"", StringComparison.Ordinal)
            && rebuild.Contains("\"60_索引库\" => \"IndexLibrary\"", StringComparison.Ordinal),
            "library rebuild must reconcile Manager and IndexLibrary roots");
        return Task.CompletedTask;
    }

'''
if 'private static Task V39ManagerAndIndexLibraryClassification()' not in text:
    if method_anchor not in text:
        raise SystemExit('method anchor not found')
    text = text.replace(method_anchor, methods + method_anchor, 1)

program.write_text(text, encoding='utf-8')
print(program)
