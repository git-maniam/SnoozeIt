namespace SnoozeIt.Core.Native;

using System;
using System.Runtime.InteropServices;

/// <summary>
/// Low-level Win32/NT kernel P/Invoke declarations for process suspension,
/// memory eviction, and window focus tracking.
/// </summary>
internal static partial class NativeMethods
{
    // ── Kernel Suspension & Thawing (ntdll.dll) ──────────────────────────────

    [LibraryImport("ntdll.dll")]
    public static partial int NtSuspendProcess(IntPtr processHandle);

    [LibraryImport("ntdll.dll")]
    public static partial int NtResumeProcess(IntPtr processHandle);

    // ── Memory Eviction (psapi.dll / kernel32.dll) ───────────────────────────

    [LibraryImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EmptyWorkingSet(IntPtr hProcess);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetProcessWorkingSetSizeEx(
        IntPtr hProcess,
        IntPtr dwMinimumWorkingSetSize,
        IntPtr dwMaximumWorkingSetSize,
        uint flags);

    public const uint QUOTA_LIMITS_HARDWS_MIN_ENABLE  = 0x00000001;
    public const uint QUOTA_LIMITS_HARDWS_MIN_DISABLE = 0x00000002;

    // ── Process Inspection & Security (kernel32.dll / advapi32.dll) ──────────

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial IntPtr OpenProcess(
        uint processAccess,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle,
        int processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(IntPtr hObject);

    public const uint PROCESS_SUSPEND_RESUME            = 0x0800;
    public const uint PROCESS_SET_QUOTA                 = 0x0100;
    public const uint PROCESS_QUERY_LIMITED_INFORMATION  = 0x1000;
    public const uint PROCESS_ALL_ACCESS_FLAGS =
        PROCESS_SUSPEND_RESUME | PROCESS_SET_QUOTA | PROCESS_QUERY_LIMITED_INFORMATION;

    // ── Window Focus Tracking (user32.dll) ───────────────────────────────────

    public delegate void WinEventDelegate(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial IntPtr SetWinEventHook(
        uint eventMin,
        uint eventMax,
        IntPtr hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc,
        uint idProcess,
        uint idThread,
        uint dwFlags);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnhookWinEvent(IntPtr hWinEventHook);

    [LibraryImport("user32.dll")]
    public static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsHungAppWindow(IntPtr hWnd);

    // WinEvent constants
    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    public const uint WINEVENT_OUTOFCONTEXT   = 0x0000;
    public const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
}

