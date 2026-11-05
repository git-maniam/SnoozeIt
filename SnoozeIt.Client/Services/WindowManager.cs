namespace SnoozeIt.Client.Services;

using System;
using System.Runtime.InteropServices;

/// <summary>
/// Provides native Win32 helpers to hide/restore the main window and minimize to the notification tray.
/// </summary>
public static partial class WindowManager
{
    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;
    private const int SW_RESTORE = 9;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr hWnd);

    public static void HideWindow(IntPtr hWnd)
    {
        if (hWnd != IntPtr.Zero)
        {
            ShowWindow(hWnd, SW_HIDE);
        }
    }

    public static void RestoreWindow(IntPtr hWnd)
    {
        if (hWnd != IntPtr.Zero)
        {
            ShowWindow(hWnd, SW_SHOW);
            ShowWindow(hWnd, SW_RESTORE);
            SetForegroundWindow(hWnd);
        }
    }
}

