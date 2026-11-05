namespace SnoozeIt.Client.Services;

using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using SnoozeIt.Core.Native;

/// <summary>
/// Listens to Windows EVENT_SYSTEM_FOREGROUND events via SetWinEventHook.
/// When any currently snoozed window gains focus (user clicks taskbar/alt-tabs),
/// it immediately unfreezes (thaws) the process in &lt; 100ms.
/// </summary>
public sealed class WindowFocusWatcher : IDisposable
{
    private readonly ProcessServiceClient _client;
    private readonly ConcurrentDictionary<int, byte> _snoozedPids = new();
    private NativeMethods.WinEventDelegate? _winEventProc;
    private IntPtr _hookHandle = IntPtr.Zero;

    public event Action<int>? ProcessAutoThawed;

    public WindowFocusWatcher(ProcessServiceClient client)
    {
        _client = client;
    }

    /// <summary>
    /// Registers a process ID as currently snoozed so that focusing any of its windows will trigger an auto-thaw.
    /// </summary>
    public void RegisterSnoozedProcess(int processId)
    {
        _snoozedPids.TryAdd(processId, 0);
    }

    /// <summary>
    /// Unregisters a process ID from auto-thaw tracking (e.g. after it has been explicitly resumed).
    /// </summary>
    public void UnregisterSnoozedProcess(int processId)
    {
        _snoozedPids.TryRemove(processId, out _);
    }

    /// <summary>
    /// Starts listening for system foreground changes.
    /// </summary>
    public void Start()
    {
        if (_hookHandle != IntPtr.Zero) return;

        // Keep delegate alive to prevent garbage collection
        _winEventProc = new NativeMethods.WinEventDelegate(OnWinEvent);

        _hookHandle = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero,
            _winEventProc,
            0,
            0,
            NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);
    }

    private void OnWinEvent(
        IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (hwnd == IntPtr.Zero) return;

        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0) return;

        int processId = (int)pid;

        if (_snoozedPids.TryRemove(processId, out _))
        {
            // Auto-thaw asynchronously immediately on focus
            _ = _client.ResumeProcessAsync(processId).ContinueWith(t =>
            {
                if (t.IsCompletedSuccessfully && t.Result.Success)
                {
                    ProcessAutoThawed?.Invoke(processId);
                }
            });
        }
    }

    public void Stop()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        Stop();
    }
}

