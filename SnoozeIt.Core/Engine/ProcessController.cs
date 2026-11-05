namespace SnoozeIt.Core.Engine;

using System;
using System.ComponentModel;
using System.Diagnostics;
using SnoozeIt.Core.Native;

/// <summary>
/// Core controller that suspends, trims working sets, and resumes processes
/// using low-level NT kernel APIs.
/// </summary>
public sealed class ProcessController
{
    /// <summary>
    /// Atomically suspends a process and evicts its physical working set.
    /// Returns the number of bytes reclaimed (approximate).
    /// </summary>
    public Result<long> SuspendAndTrim(int processId)
    {
        // 1. Safety check
        string processName;
        try
        {
            using var proc = Process.GetProcessById(processId);
            processName = proc.ProcessName + ".exe";
        }
        catch (ArgumentException)
        {
            return Result<long>.Fail($"Process {processId} does not exist.");
        }

        if (!SafetyFilter.IsSafeToSuspend(processId, processName, out var reason))
            return Result<long>.Fail(reason!);

        // 2. Open process handle with required access rights
        IntPtr hProcess = NativeMethods.OpenProcess(
            NativeMethods.PROCESS_ALL_ACCESS_FLAGS, false, processId);

        if (hProcess == IntPtr.Zero)
            return Result<long>.Fail($"Failed to open process {processId}: {new Win32Exception().Message}");

        try
        {
            // Record working set before
            long wsBefore;
            try
            {
                using var proc = Process.GetProcessById(processId);
                proc.Refresh();
                wsBefore = proc.WorkingSet64;
            }
            catch
            {
                wsBefore = 0;
            }

            // 3. Atomic pause via NtSuspendProcess
            int ntStatus = NativeMethods.NtSuspendProcess(hProcess);
            if (ntStatus != 0) // STATUS_SUCCESS == 0
                return Result<long>.Fail($"NtSuspendProcess returned NTSTATUS 0x{ntStatus:X8}.");

            // 4. Evict working set to page file
            NativeMethods.EmptyWorkingSet(hProcess);
            NativeMethods.SetProcessWorkingSetSizeEx(
                hProcess,
                (IntPtr)(-1),
                (IntPtr)(-1),
                NativeMethods.QUOTA_LIMITS_HARDWS_MIN_ENABLE);

            // Record working set after
            long wsAfter;
            try
            {
                using var proc = Process.GetProcessById(processId);
                proc.Refresh();
                wsAfter = proc.WorkingSet64;
            }
            catch
            {
                wsAfter = 0;
            }

            long reclaimed = Math.Max(0, wsBefore - wsAfter);
            return Result<long>.Ok(reclaimed);
        }
        finally
        {
            NativeMethods.CloseHandle(hProcess);
        }
    }

    /// <summary>
    /// Resumes a previously suspended process.
    /// </summary>
    public Result Resume(int processId)
    {
        IntPtr hProcess = NativeMethods.OpenProcess(
            NativeMethods.PROCESS_SUSPEND_RESUME, false, processId);

        if (hProcess == IntPtr.Zero)
            return Result.Fail($"Failed to open process {processId}: {new Win32Exception().Message}");

        try
        {
            int ntStatus = NativeMethods.NtResumeProcess(hProcess);
            if (ntStatus != 0)
                return Result.Fail($"NtResumeProcess returned NTSTATUS 0x{ntStatus:X8}.");

            // Release the hard working-set minimum so the process can reclaim RAM naturally
            NativeMethods.SetProcessWorkingSetSizeEx(
                hProcess,
                (IntPtr)(-1),
                (IntPtr)(-1),
                NativeMethods.QUOTA_LIMITS_HARDWS_MIN_DISABLE);

            return Result.Ok();
        }
        finally
        {
            NativeMethods.CloseHandle(hProcess);
        }
    }
}

// ── Lightweight Result types ─────────────────────────────────────────────────

/// <summary>Non-generic result (success or failure with message).</summary>
public readonly struct Result
{
    public bool Success { get; }
    public string? Error { get; }

    private Result(bool success, string? error) { Success = success; Error = error; }
    public static Result Ok() => new(true, null);
    public static Result Fail(string error) => new(false, error);
}

/// <summary>Generic result carrying a value on success.</summary>
public readonly struct Result<T>
{
    public bool Success { get; }
    public T? Value { get; }
    public string? Error { get; }

    private Result(bool success, T? value, string? error)
    {
        Success = success; Value = value; Error = error;
    }

    public static Result<T> Ok(T value) => new(true, value, null);
    public static Result<T> Fail(string error) => new(false, default, error);
}

