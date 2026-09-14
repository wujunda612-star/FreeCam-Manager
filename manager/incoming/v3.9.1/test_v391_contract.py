from pathlib import Path
import re
import sys
import zipfile


def require(condition: bool, message: str) -> None:
    if not condition:
        raise AssertionError(message)


def read_zip_text(zf: zipfile.ZipFile, name: str) -> str:
    try:
        return zf.read(name).decode("utf-8-sig")
    except KeyError as exc:
        raise AssertionError(f"missing package entry: {name}") from exc


def main() -> int:
    package = Path(sys.argv[1]) if len(sys.argv) > 1 else Path("manager/packages/FreeCam_Manager_V3.9.1_Source.zip")
    require(package.is_file(), f"package not found: {package}")

    with zipfile.ZipFile(package) as zf:
        settings = read_zip_text(zf, "src-wpf/FreeCamManager/Views/SettingsView.xaml")
        csproj = read_zip_text(zf, "src-wpf/FreeCamManager/FreeCamManager.csproj")

    require("<Version>3.9.1</Version>" in csproj, "FreeCamManager.csproj must declare Version 3.9.1")
    require('x:Name="SettingsBehaviorSingleRow"' in settings, "display/behavior settings must use the V3.9.1 single-row grid")
    require('x:Name="SettingsPrimaryColumn"' not in settings, "old primary vertical column must be removed")
    require('x:Name="SettingsAliasColumn"' not in settings, "old alias vertical column must be removed")

    row_start = settings.index('x:Name="SettingsBehaviorSingleRow"')
    terms_start = settings.index('x:Name="SettingsRowTerms"')
    row = settings[row_start:terms_start]

    labels = [
        "界面主题",
        "已废弃自动删除",
        "隐藏 FreeCam_ 文件名前缀",
        "显示文件名中文别名",
        "功能中文显示",
        "阶段中文显示",
    ]
    positions = []
    for label in labels:
        require(label in row, f"single-row settings missing label: {label}")
        positions.append(row.index(label))
    require(positions == sorted(positions), "single-row settings must keep the agreed left-to-right order")

    columns = re.findall(r'<StackPanel Grid.Column="(\d+)"[^>]*>\s*<TextBlock Text="(?:界面主题|已废弃自动删除|隐藏 FreeCam_ 文件名前缀|显示文件名中文别名|功能中文显示|阶段中文显示)"', row)
    require(columns == ["0", "2", "4", "6", "8", "10"], f"expected six horizontal setting columns, got: {columns}")

    print("V3.9.1 contract PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
