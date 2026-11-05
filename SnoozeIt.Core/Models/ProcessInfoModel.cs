namespace SnoozeIt.Core.Models;

/// <summary>
/// Represents the current snooze state of a tracked process.
/// </summary>
public enum SnoozeState
{
    /// <summary>Process is running normally.</summary>
    Active,

    /// <summary>Process has been suspended and its working set evicted.</summary>
    Snoozed,

    /// <summary>Process is being resumed (transitional).</summary>
    Thawing
}

/// <summary>
/// Snapshot of a process's resource usage and snooze status.
/// </summary>
public sealed class ProcessInfoModel
{
    public required int ProcessId { get; init; }
    public required string ProcessName { get; init; }
    public string? FriendlyName { get; init; }
    public string? IconPath { get; init; }

    /// <summary>Current private working set in bytes.</summary>
    public long WorkingSetBytes { get; set; }

    /// <summary>CPU usage percentage (0–100) over the last sample interval.</summary>
    public double CpuPercent { get; set; }

    /// <summary>Bytes reclaimed when the process was last snoozed.</summary>
    public long ReclaimedBytes { get; set; }

    /// <summary>Current snooze state.</summary>
    public SnoozeState State { get; set; } = SnoozeState.Active;

    /// <summary>UTC timestamp of the last time this process was the foreground window.</summary>
    public DateTime LastForegroundUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Session ID that owns this process.</summary>
    public int SessionId { get; set; }

    /// <summary>Whether the process is currently streaming audio.</summary>
    public bool IsPlayingAudio { get; set; }

    /// <summary>Disk write throughput in bytes/sec over the last sample.</summary>
    public long DiskWriteBytesPerSec { get; set; }

    /// <summary>Network send+recv throughput in bytes/sec over the last sample.</summary>
    public long NetBytesPerSec { get; set; }
}

