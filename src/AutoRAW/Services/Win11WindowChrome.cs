using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AutoRAW.Services;

/// <summary>Тёмная шапка окна через DWM (Windows 10 19H1+).</summary>
public static class Win11WindowChrome
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    public static void SetTitleBarDark(Window window, bool dark)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        int v = dark ? 1 : 0;
        int attr = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 18985)
            ? DWMWA_USE_IMMERSIVE_DARK_MODE
            : DWMWA_USE_IMMERSIVE_DARK_MODE_OLD;

        DwmSetWindowAttribute(hwnd, attr, ref v, sizeof(int));
    }
}
