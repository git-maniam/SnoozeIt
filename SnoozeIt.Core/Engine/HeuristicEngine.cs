namespace SnoozeIt.Core.Engine;

using System;
using SnoozeIt.Core.Models;

/// <summary>
/// Computes the snoozeability score S ∈ [0, 100] for a process using the
/// formula defined in the specification (Section 4.1).
///
/// S = Clamp(W_idle + W_mem − P_audio − P_io − P_net − P_focus − P_critical, 0, 100)
/// </summary>
public static class HeuristicEngine
{
    // ── Tunable constants ────────────────────────────────────────────────────

    private const int MaxIdlePoints        = 40;
    private const int IdlePointsPerMinute  = 5;

    private const int MaxMemoryPoints      = 30;
    private const long MemoryBytesPerPoint = 50L * 1024 * 1024; // 50 MB

    private const int AudioPenaltyValue    = 80;

    private const int DiskIoPenaltyValue   = 50;
    private const long DiskIoThreshold     = 500 * 1024; // 500 KB/s

    private const int NetworkPenaltyValue  = 40;
    private const long NetworkThreshold    = 50 * 1024;  // 50 KB/s

    private const int FocusPenaltyValue    = 90;
    private const int FocusRecencySeconds  = 60;

    /// <summary>
    /// Computes a <see cref="SnoozeRecommendation"/> for the given process snapshot.
    /// </summary>
    public static SnoozeRecommendation Evaluate(ProcessInfoModel process)
    {
        ArgumentNullException.ThrowIfNull(process);

        // ── Critical blacklist check ─────────────────────────────────────────
        bool isCritical = !SafetyFilter.IsSafeToSuspend(
            process.ProcessId, process.ProcessName, out _);

        if (isCritical)
        {
            return new SnoozeRecommendation
            {
                Score          = 0,
                IdleWeight     = 0,
                MemoryWeight   = 0,
                AudioPenalty   = 0,
                DiskIoPenalty  = 0,
                NetworkPenalty = 0,
                FocusPenalty   = 0,
                IsCritical     = true
            };
        }

        // ── W_idle: 5 pts per minute since last foreground, capped at 40 ────
        double idleMinutes = (DateTime.UtcNow - process.LastForegroundUtc).TotalMinutes;
        int wIdle = Math.Min(MaxIdlePoints, (int)(idleMinutes * IdlePointsPerMinute));

        // ── W_mem: 1 pt per 50 MB of private working set, capped at 30 ─────
        int wMem = Math.Min(MaxMemoryPoints,
            (int)(process.WorkingSetBytes / MemoryBytesPerPoint));

        // ── Penalties ────────────────────────────────────────────────────────
        int pAudio = process.IsPlayingAudio ? AudioPenaltyValue : 0;
        int pIo    = process.DiskWriteBytesPerSec > DiskIoThreshold ? DiskIoPenaltyValue : 0;
        int pNet   = process.NetBytesPerSec > NetworkThreshold ? NetworkPenaltyValue : 0;
        int pFocus = idleMinutes < (FocusRecencySeconds / 60.0) ? FocusPenaltyValue : 0;

        // ── Composite score ──────────────────────────────────────────────────
        int raw = wIdle + wMem - pAudio - pIo - pNet - pFocus;
        int score = Math.Clamp(raw, 0, 100);

        return new SnoozeRecommendation
        {
            Score          = score,
            IdleWeight     = wIdle,
            MemoryWeight   = wMem,
            AudioPenalty   = pAudio,
            DiskIoPenalty  = pIo,
            NetworkPenalty = pNet,
            FocusPenalty   = pFocus,
            IsCritical     = false
        };
    }
}

