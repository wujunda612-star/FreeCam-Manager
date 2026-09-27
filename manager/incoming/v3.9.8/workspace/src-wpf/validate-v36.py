from pathlib import Path
import re
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

version_match = re.search(r'<Version>(\d+)\.(\d+)\.(\d+)</Version>', csproj)
need(version_match is not None and tuple(map(int, version_match.groups())) >= (3, 6, 0), 'app version must be >= 3.6.0')
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
