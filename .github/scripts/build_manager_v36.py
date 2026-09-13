from __future__ import annotations

import argparse
import base64
import gzip
import hashlib
import json
import shutil
import subprocess
import tempfile
import zipfile
from pathlib import Path


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{label}: expected exactly one anchor, found {count}")
    return text.replace(old, new, 1)


def add_tests(source_root: Path) -> None:
    root = source_root / "src-wpf"
    program_path = root / "FreeCamManager.Tests" / "Program.cs"
    program = program_path.read_text(encoding="utf-8")

    reg_anchor = '        await Run("V3.5 update cleanup keeps diagnostics only", V35UpdateCleanupKeepsDiagnosticsOnly);\n'
    registrations = reg_anchor + (
        '        await Run("V3.6 context menu separator uses MenuItem separator key", V36ContextMenuSeparatorUsesMenuItemKey);\n'
        '        await Run("V3.6 home summary icons share one presentation style", V36HomeSummaryIconsShareOnePresentationStyle);\n'
        '        await Run("V3.6 single-instance startup contract", V36SingleInstanceStartupContract);\n'
    )
    program = replace_once(program, reg_anchor, registrations, "test registration")

    old_v35_assert = '''        Assert(controls.Contains("<Style TargetType=\\"Separator\\">", StringComparison.Ordinal)
            && controls.Contains("Margin=\\"0,4\\"", StringComparison.Ordinal),
            "context menu separators must use the full available width");'''
    new_v35_assert = '''        Assert(controls.Contains("x:Key=\\"{x:Static MenuItem.SeparatorStyleKey}\\"", StringComparison.Ordinal)
            && controls.Contains("Margin=\\"0,4\\"", StringComparison.Ordinal),
            "context menu separators must use the WPF menu separator key and full available width");'''
    program = replace_once(program, old_v35_assert, new_v35_assert, "V3.5 separator regression")

    methods = r'''
    private static Task V36ContextMenuSeparatorUsesMenuItemKey()
    {
        var sourceRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "FreeCamManager"));
        var controls = File.ReadAllText(Path.Combine(sourceRoot, "Styles", "Controls.xaml"));
        Assert(controls.Contains("x:Key=\"{x:Static MenuItem.SeparatorStyleKey}\"", StringComparison.Ordinal),
            "WPF ContextMenu separators must use MenuItem.SeparatorStyleKey rather than only an implicit Separator style");
        Assert(controls.Contains("Margin=\"0,4\"", StringComparison.Ordinal),
            "menu separator must have zero horizontal inset");
        return Task.CompletedTask;
    }

    private static Task V36HomeSummaryIconsShareOnePresentationStyle()
    {
        var sourceRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "FreeCamManager"));
        var home = File.ReadAllText(Path.Combine(sourceRoot, "Views", "HomeView.xaml"));
        Assert(home.Contains("x:Key=\"HomeSummaryIconStyle\"", StringComparison.Ordinal), "Home summary icon style is missing");
        Assert(home.Contains("<Setter Property=\"StrokeThickness\" Value=\"1.4\"/>", StringComparison.Ordinal), "Home summary icon stroke thickness must be shared");
        Assert(home.Contains("<Setter Property=\"Width\" Value=\"23\"/>", StringComparison.Ordinal)
            && home.Contains("<Setter Property=\"Height\" Value=\"23\"/>", StringComparison.Ordinal),
            "Home summary icon dimensions must be shared");
        Assert(home.Contains("<Setter Property=\"Stretch\" Value=\"Uniform\"/>", StringComparison.Ordinal), "Home summary icons must all use Uniform stretch");
        Assert(home.Contains("<Setter Property=\"VerticalAlignment\" Value=\"Top\"/>", StringComparison.Ordinal), "Home summary icon alignment must be shared");
        var usageCount = home.Split("Style=\"{StaticResource HomeSummaryIconStyle}\"", StringSplitOptions.None).Length - 1;
        Assert(usageCount == 4, $"exactly four Home summary icons must use the shared style, got {usageCount}");
        return Task.CompletedTask;
    }

    private static Task V36SingleInstanceStartupContract()
    {
        var sourceRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "FreeCamManager"));
        var servicePath = Path.Combine(sourceRoot, "Services", "SingleInstanceService.cs");
        Assert(File.Exists(servicePath), "SingleInstanceService.cs must exist");
        var service = File.ReadAllText(servicePath);
        var app = File.ReadAllText(Path.Combine(sourceRoot, "App.xaml.cs"));
        Assert(service.Contains("FreeCamManager.SingleInstance", StringComparison.Ordinal)
            && service.Contains("new Mutex(true, MutexName", StringComparison.Ordinal),
            "single-instance mutex ownership contract is missing");
        Assert(service.Contains("SignalPrimaryInstance", StringComparison.Ordinal)
            && service.Contains("EventWaitHandle.OpenExisting", StringComparison.Ordinal),
            "secondary instance activation signal is missing");
        Assert(app.Contains("if (!_singleInstance.TryAcquirePrimary())", StringComparison.Ordinal)
            && app.Contains("SingleInstanceService.SignalPrimaryInstance();", StringComparison.Ordinal)
            && app.Contains("Shutdown();", StringComparison.Ordinal),
            "secondary Manager instance must signal the primary and exit");
        Assert(app.Contains("_singleInstance.StartListening", StringComparison.Ordinal)
            && app.Contains("BringPrimaryWindowToFront", StringComparison.Ordinal),
            "primary Manager instance must listen and restore/activate its window");
        return Task.CompletedTask;
    }

'''
    program = replace_once(program, "    private static string TempDir()\n", methods + "    private static string TempDir()\n", "TempDir method")
    program_path.write_text(program, encoding="utf-8")

    validator = r'''from pathlib import Path
import sys

root = Path(__file__).resolve().parent
app = root / "FreeCamManager"
tests = root / "FreeCamManager.Tests" / "Program.cs"
errors = []

def need(cond, msg):
    if not cond:
        errors.append(msg)

csproj = (app / "FreeCamManager.csproj").read_text(encoding="utf-8")
controls = (app / "Styles" / "Controls.xaml").read_text(encoding="utf-8")
home = (app / "Views" / "HomeView.xaml").read_text(encoding="utf-8")
appcs = (app / "App.xaml.cs").read_text(encoding="utf-8")
single_path = app / "Services" / "SingleInstanceService.cs"
single = single_path.read_text(encoding="utf-8") if single_path.exists() else ""
testsrc = tests.read_text(encoding="utf-8")

need("<Version>3.6.0</Version>" in csproj, "app version must be 3.6.0")
need('x:Key="{x:Static MenuItem.SeparatorStyleKey}"' in controls, "menu separator must use MenuItem.SeparatorStyleKey")
need('Margin="0,4"' in controls, "menu separator must have zero horizontal inset")
need('x:Key="HomeSummaryIconStyle"' in home, "HomeSummaryIconStyle must exist")
need('<Setter Property="StrokeThickness" Value="1.4"/>' in home, "HomeSummaryIconStyle must define StrokeThickness 1.4")
need('<Setter Property="Width" Value="23"/>' in home and '<Setter Property="Height" Value="23"/>' in home, "HomeSummaryIconStyle must define 23x23 dimensions")
need('<Setter Property="Stretch" Value="Uniform"/>' in home, "HomeSummaryIconStyle must define Uniform stretch")
need('<Setter Property="VerticalAlignment" Value="Top"/>' in home, "HomeSummaryIconStyle must define Top alignment")
need(home.count('Style="{StaticResource HomeSummaryIconStyle}"') == 4, "all four Home summary icons must use HomeSummaryIconStyle")
need(single_path.exists() and "new Mutex(true, MutexName" in single and "SignalPrimaryInstance" in single, "single-instance service must remain wired")
need('if (!_singleInstance.TryAcquirePrimary())' in appcs and "SingleInstanceService.SignalPrimaryInstance();" in appcs and "_singleInstance.StartListening" in appcs and "BringPrimaryWindowToFront" in appcs, "App single-instance startup/activation wiring must remain intact")
need("V3.6 context menu separator uses MenuItem separator key" in testsrc, "V3.6 separator regression must be registered")
need("V3.6 home summary icons share one presentation style" in testsrc, "V3.6 Home icon regression must be registered")
need("V3.6 single-instance startup contract" in testsrc, "V3.6 single-instance regression must be registered")

if errors:
    print("FAIL: V3.6 contract")
    for e in errors:
        print(" -", e)
    sys.exit(1)
print("PASS: V3.6 contract")
'''
    (root / "validate-v36.py").write_text(validator, encoding="utf-8")


def apply_production(source_root: Path) -> None:
    root = source_root / "src-wpf"

    csproj_path = root / "FreeCamManager" / "FreeCamManager.csproj"
    csproj = csproj_path.read_text(encoding="utf-8")
    csproj = replace_once(csproj, "<Version>3.5.0</Version>", "<Version>3.6.0</Version>", "project version")
    csproj_path.write_text(csproj, encoding="utf-8")

    controls_path = root / "FreeCamManager" / "Styles" / "Controls.xaml"
    controls = controls_path.read_text(encoding="utf-8")
    old_separator = '''    <Style TargetType="Separator">
        <Setter Property="HorizontalAlignment" Value="Stretch"/>
        <Setter Property="Template">
            <Setter.Value><ControlTemplate TargetType="Separator"><Border Height="1" Background="{DynamicResource DividerBrush}" Margin="0,4" HorizontalAlignment="Stretch"/></ControlTemplate></Setter.Value>
        </Setter>
    </Style>'''
    new_separator = '''    <Style x:Key="{x:Static MenuItem.SeparatorStyleKey}" TargetType="Separator">
        <Setter Property="HorizontalAlignment" Value="Stretch"/>
        <Setter Property="Template">
            <Setter.Value><ControlTemplate TargetType="Separator"><Border Height="1" Background="{DynamicResource DividerBrush}" Margin="0,4" HorizontalAlignment="Stretch"/></ControlTemplate></Setter.Value>
        </Setter>
    </Style>'''
    controls = replace_once(controls, old_separator, new_separator, "separator style")
    controls_path.write_text(controls, encoding="utf-8")

    home_path = root / "FreeCamManager" / "Views" / "HomeView.xaml"
    home = home_path.read_text(encoding="utf-8")
    root_anchor = '             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">\n    <Grid>'
    resources = '''             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <UserControl.Resources>
        <Style x:Key="HomeSummaryIconStyle" TargetType="Path">
            <Setter Property="StrokeThickness" Value="1.4"/>
            <Setter Property="Width" Value="23"/>
            <Setter Property="Height" Value="23"/>
            <Setter Property="Stretch" Value="Uniform"/>
            <Setter Property="VerticalAlignment" Value="Top"/>
        </Style>
    </UserControl.Resources>

    <Grid>'''
    home = replace_once(home, root_anchor, resources, "HomeView root")

    replacements = {
        '<Path Grid.Column="1" Data="{StaticResource IconStable}" Stroke="{DynamicResource SuccessBrush}" StrokeThickness="1.4" Width="23" Height="23" Stretch="Uniform" VerticalAlignment="Top"/>': '<Path Grid.Column="1" Style="{StaticResource HomeSummaryIconStyle}" Data="{StaticResource IconStable}" Stroke="{DynamicResource SuccessBrush}"/>',
        '<Path Grid.Column="1" Data="{StaticResource IconDevelopment}" Stroke="{DynamicResource AccentBrush}" StrokeThickness="1.4" Width="23" Height="23" VerticalAlignment="Top"/>': '<Path Grid.Column="1" Style="{StaticResource HomeSummaryIconStyle}" Data="{StaticResource IconDevelopment}" Stroke="{DynamicResource AccentBrush}"/>',
        '<Path Grid.Column="1" Data="{StaticResource IconInbox}" Stroke="{DynamicResource WarningBrush}" StrokeThickness="1.4" Width="23" Height="23" VerticalAlignment="Top"/>': '<Path Grid.Column="1" Style="{StaticResource HomeSummaryIconStyle}" Data="{StaticResource IconInbox}" Stroke="{DynamicResource WarningBrush}"/>',
        '<Path Grid.Column="1" Data="{StaticResource IconSearch}" Stroke="{DynamicResource TextMutedBrush}" StrokeThickness="1.4" Width="23" Height="23" VerticalAlignment="Top"/>': '<Path Grid.Column="1" Style="{StaticResource HomeSummaryIconStyle}" Data="{StaticResource IconSearch}" Stroke="{DynamicResource TextMutedBrush}"/>',
    }
    for old, new in replacements.items():
        home = replace_once(home, old, new, f"Home icon {old[:60]}")
    home_path.write_text(home, encoding="utf-8")

    v35_path = root / "validate-v35.py"
    v35 = v35_path.read_text(encoding="utf-8")
    if "import re\n" not in v35:
        v35 = v35.replace("from pathlib import Path\n", "from pathlib import Path\nimport re\n", 1)
    old_version_check = "need('<Version>3.5.0</Version>' in csproj, 'app version must be 3.5.0')"
    new_version_check = (
        "version_match = re.search(r'<Version>(\\d+)\\.(\\d+)\\.(\\d+)</Version>', csproj)\n"
        "need(version_match is not None and tuple(map(int, version_match.groups())) >= (3, 5, 0), 'app version must be >= 3.5.0')"
    )
    v35 = replace_once(v35, old_version_check, new_version_check, "validate-v35 version floor")
    old_sep_check = "need('Margin=\"7,4\"' not in controls, 'separator must not keep the old 7px horizontal inset')"
    new_sep_check = (
        "need('x:Key=\"{x:Static MenuItem.SeparatorStyleKey}\"' in controls, 'separator must use the WPF MenuItem separator style key')\n"
        "need('Margin=\"7,4\"' not in controls, 'separator must not keep the old 7px horizontal inset')"
    )
    v35 = replace_once(v35, old_sep_check, new_sep_check, "validate-v35 separator contract")
    v35_path.write_text(v35, encoding="utf-8")


def clean_source(source_root: Path) -> None:
    for p in sorted(source_root.rglob("*"), key=lambda x: len(x.parts), reverse=True):
        if p.is_dir() and p.name in {"bin", "obj", "BuildOutput", ".tools"}:
            shutil.rmtree(p, ignore_errors=True)


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def tree_hash(root: Path) -> str:
    h = hashlib.sha256()
    for p in sorted(x for x in root.rglob("*") if x.is_file()):
        rel = p.relative_to(root).as_posix().encode("utf-8")
        h.update(rel)
        h.update(b"\0")
        h.update(hashlib.sha256(p.read_bytes()).digest())
    return h.hexdigest()


def deterministic_zip(root: Path, output: Path) -> None:
    output.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(output, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as zf:
        for p in sorted(x for x in root.rglob("*") if x.is_file()):
            info = zipfile.ZipInfo(p.relative_to(root).as_posix(), date_time=(1980, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = (0o100644 & 0xFFFF) << 16
            info.create_system = 3
            zf.writestr(info, p.read_bytes(), compress_type=zipfile.ZIP_DEFLATED, compresslevel=9)


def stage(source_root: Path, repo_root: Path) -> None:
    base_zip = repo_root / "manager/packages/FreeCam_Manager_V3.5_Source.zip"
    incoming = repo_root / "manager/incoming/v3.6.0/v35-to-v36.patch.gz.b85"
    candidate = repo_root / "manager/publish-v36.candidate.json"

    with tempfile.TemporaryDirectory() as td_value:
        td = Path(td_value)
        base = td / "base"
        base.mkdir()
        with zipfile.ZipFile(base_zip, "r") as zf:
            zf.extractall(base)

        subprocess.run(["git", "init", "-q"], cwd=base, check=True)
        subprocess.run(["git", "config", "user.name", "v36-builder"], cwd=base, check=True)
        subprocess.run(["git", "config", "user.email", "v36-builder@example.invalid"], cwd=base, check=True)
        subprocess.run(["git", "add", "-A"], cwd=base, check=True)
        subprocess.run(["git", "commit", "-q", "-m", "base"], cwd=base, check=True)

        for p in list(base.iterdir()):
            if p.name == ".git":
                continue
            if p.is_dir():
                shutil.rmtree(p)
            else:
                p.unlink()
        for p in source_root.iterdir():
            dst = base / p.name
            if p.is_dir():
                shutil.copytree(p, dst)
            else:
                shutil.copy2(p, dst)

        patch = subprocess.check_output(["git", "diff", "--binary", "--src-prefix=", "--dst-prefix="], cwd=base)
        if not patch:
            raise SystemExit("generated patch is empty")
        patch_sha = sha256(patch)

        incoming.parent.mkdir(parents=True, exist_ok=True)
        encoded = base64.b85encode(gzip.compress(patch, compresslevel=9, mtime=0)).decode("ascii") + "\n"
        incoming.write_text(encoded, encoding="ascii")

        apply_check = td / "apply-check"
        apply_check.mkdir()
        with zipfile.ZipFile(base_zip, "r") as zf:
            zf.extractall(apply_check)
        patch_file = td / "patch.diff"
        patch_file.write_bytes(patch)
        subprocess.run(["git", "apply", "-p0", "--unsafe-paths", str(patch_file)], cwd=apply_check, check=True)

        expected_tree = tree_hash(source_root)
        applied_tree = tree_hash(apply_check)
        if applied_tree != expected_tree:
            raise SystemExit(f"patch apply tree mismatch expected={expected_tree} actual={applied_tree}")

        zip_path = td / "FreeCam_Manager_V3.6_Source.zip"
        deterministic_zip(source_root, zip_path)
        zip_sha = sha256(zip_path.read_bytes())
        zip_size = zip_path.stat().st_size

    cfg = {
        "schemaVersion": 1,
        "version": "3.6.0",
        "displayVersion": "V3.6",
        "packageName": "FreeCam_Manager_V3.6_Source.zip",
        "sourceSha256": zip_sha,
        "basePackageName": "FreeCam_Manager_V3.5_Source.zip",
        "patchFile": "manager/incoming/v3.6.0/v35-to-v36.patch.gz.b85",
        "patchEncoding": "base85",
        "patchCompression": "gzip",
        "patchSha256": patch_sha,
        "sourceTreeSha256": expected_tree,
        "notes": "修复右键菜单分隔线未实际接管 WPF 菜单专用样式的问题；统一首页四个状态卡片图标的尺寸、缩放、线宽与对齐；保留并回归验证单实例运行与重复启动唤醒。",
        "publishRevision": 1,
    }
    candidate.write_text(json.dumps(cfg, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(cfg, ensure_ascii=False, indent=2))
    print(f"deterministic_zip_size={zip_size}")


def main() -> None:
    parser = argparse.ArgumentParser()
    sub = parser.add_subparsers(dest="command", required=True)

    for cmd in ("add-tests", "apply-production", "clean"):
        p = sub.add_parser(cmd)
        p.add_argument("--source-root", required=True, type=Path)

    p_stage = sub.add_parser("stage")
    p_stage.add_argument("--source-root", required=True, type=Path)
    p_stage.add_argument("--repo-root", required=True, type=Path)

    args = parser.parse_args()
    if args.command == "add-tests":
        add_tests(args.source_root)
    elif args.command == "apply-production":
        apply_production(args.source_root)
    elif args.command == "clean":
        clean_source(args.source_root)
    elif args.command == "stage":
        stage(args.source_root, args.repo_root)


if __name__ == "__main__":
    main()
