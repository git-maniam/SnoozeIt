namespace SnoozeIt.Core.Models;

/// <summary>
/// Recommendation tier indicating how safe it is to snooze a process.
/// </summary>
public enum RecommendationTier
{
    /// <summary>Score ≥ 70 — safe to snooze (highlighted green).</summary>
    SafeToSnooze,

    /// <summary>Score 40–69 — snooze with caution (highlighted yellow).</summary>
    SnoozeWithCaution,

    /// <summary>Score &lt; 40 — do not snooze (highlighted red).</summary>
    DoNotSnooze
}

/// <summary>
/// Encapsulates the snoozeability score and its component breakdown.
/// </summary>
public sealed class SnoozeRecommendation
{
    /// <summary>The composite snoozeability score (0–100).</summary>
    public int Score { get; init; }

    /// <summary>Recommendation tier derived from the score.</summary>
    public RecommendationTier Tier => Score switch
    {
        >= 70 => RecommendationTier.SafeToSnooze,
        >= 40 => RecommendationTier.SnoozeWithCaution,
        _     => RecommendationTier.DoNotSnooze
    };

    // ── Score components ─────────────────────────────────────────────────────

    /// <summary>Points from inactivity (max 40).</summary>
    public int IdleWeight { get; init; }

    /// <summary>Points from RAM consumption (max 30).</summary>
    public int MemoryWeight { get; init; }

    /// <summary>Penalty for active audio playback (80).</summary>
    public int AudioPenalty { get; init; }

    /// <summary>Penalty for heavy disk I/O (50).</summary>
    public int DiskIoPenalty { get; init; }

    /// <summary>Penalty for active network traffic (40).</summary>
    public int NetworkPenalty { get; init; }

    /// <summary>Penalty for recent foreground focus (90).</summary>
    public int FocusPenalty { get; init; }

    /// <summary>Whether the process is on the critical blacklist.</summary>
    public bool IsCritical { get; init; }
}

