using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FreeCamManager.Services;

public static class WindowChromeService
{
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmWindowCornerPreferenceRound = 2;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    public static void TryEnableRoundedCorners(Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            var preference = DwmWindowCornerPreferenceRound;
            _ = DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref preference, sizeof(int));
        }
        catch { }
    }
}
