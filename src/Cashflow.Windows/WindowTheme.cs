using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Cashflow.Windows;

internal static class WindowTheme
{
    private const int DwmUseImmersiveDarkModeBefore20H1 = 19;
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmWindowCornerPreference = 33;
    private const int DwmWindowCornerRound = 2;

    public static void ApplyDarkTitleBar(Window window, Func<IntPtr, int, int, int, int>? setAttribute = null)
    {
        setAttribute ??= SetAttribute;
        if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
        {
            Apply(window, setAttribute);
            return;
        }

        window.SourceInitialized += (_, _) => Apply(window, setAttribute);
    }

    private static void Apply(Window window, Func<IntPtr, int, int, int, int> setAttribute)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var enabled = 1;
        var rounded = DwmWindowCornerRound;

        if (setAttribute(handle, DwmUseImmersiveDarkMode, enabled, sizeof(int)) != 0)
        {
            setAttribute(handle, DwmUseImmersiveDarkModeBefore20H1, enabled, sizeof(int));
        }

        setAttribute(handle, DwmWindowCornerPreference, rounded, sizeof(int));
    }

    private static int SetAttribute(IntPtr handle, int attribute, int value, int size) =>
        DwmSetWindowAttribute(handle, attribute, ref value, size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);
}
