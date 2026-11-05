namespace SnoozeIt.Core.Engine;

using System.Collections.Frozen;

/// <summary>
/// Guards against suspending critical system processes, protected processes,
/// and SnoozeIt's own binaries.
/// </summary>
public static class SafetyFilter
{
    /// <summary>
    /// Hardcoded set of process image names that must never be suspended.
    /// </summary>
    public static readonly FrozenSet<string> BlacklistedBinaries = new[]
    {
        "system", "idle", "smss.exe", "csrss.exe", "wininit.exe", "services.exe",
        "lsass.exe", "svchost.exe", "dwm.exe", "explorer.exe", "taskhostw.exe",
        "sihost.exe", "fontdrvhost.exe", "runtimebroker.exe", "shellexperiencehost.exe",
        "searchindexer.exe", "securityhealthservice.exe", "msmpeng.exe",
        "snoozeit.client.exe", "snoozeit.service.exe"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Determines whether a process is safe to suspend.
    /// </summary>
    /// <param name="processId">The PID of the target process.</param>
    /// <param name="processName">The image name (e.g. "chrome.exe").</param>
    /// <param name="reason">If unsafe, the human-readable reason.</param>
    /// <returns><c>true</c> if the process may be suspended; <c>false</c> otherwise.</returns>
    public static bool IsSafeToSuspend(int processId, string processName, out string? reason)
    {
        // Never touch PID 0 (Idle) or PID 4 (System)
        if (processId <= 4)
        {
            reason = $"PID {processId} is a protected kernel process.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(processName))
        {
            reason = "Process name is empty or null.";
            return false;
        }

        string cleanName = processName.Trim();
        string withExe = cleanName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? cleanName : cleanName + ".exe";
        string withoutExe = cleanName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? cleanName[..^4] : cleanName;

        if (BlacklistedBinaries.Contains(cleanName) || BlacklistedBinaries.Contains(withExe) || BlacklistedBinaries.Contains(withoutExe))
        {
            reason = $"'{processName}' is on the critical system service blacklist.";
            return false;
        }

        reason = null;
        return true;
    }
}

