using SnoozeIt.Core.Engine;
using SnoozeIt.Core.Models;

namespace SnoozeIt.Core.Tests;

[TestClass]
public class HeuristicEngineTests
{
    [TestMethod]
    public void Evaluate_CriticalProcess_ReturnsZeroScore()
    {
        var process = new ProcessInfoModel
        {
            ProcessId = 4,
            ProcessName = "system",
            WorkingSetBytes = 500 * 1024 * 1024
        };

        var result = HeuristicEngine.Evaluate(process);

        Assert.AreEqual(0, result.Score);
        Assert.IsTrue(result.IsCritical);
        Assert.AreEqual(RecommendationTier.DoNotSnooze, result.Tier);
    }

    [TestMethod]
    public void Evaluate_IdleBrowser_HighScore()
    {
        // Simulate an idle browser: 10 minutes idle, 400 MB RAM, no audio/IO/network
        var process = new ProcessInfoModel
        {
            ProcessId = 5000,
            ProcessName = "chrome.exe",
            WorkingSetBytes = 400L * 1024 * 1024,
            LastForegroundUtc = DateTime.UtcNow.AddMinutes(-10),
            IsPlayingAudio = false,
            DiskWriteBytesPerSec = 0,
            NetBytesPerSec = 0
        };

        var result = HeuristicEngine.Evaluate(process);

        // W_idle = min(40, 10*5) = 40
        // W_mem  = min(30, 400/50) = 8
        // No penalties
        // Score  = 40 + 8 = 48 (but let's just assert > 40)
        Assert.IsTrue(result.Score >= 40, $"Expected score >= 40 but got {result.Score}");
        Assert.IsFalse(result.IsCritical);
    }

    [TestMethod]
    public void Evaluate_ActiveMediaPlayer_LowScore()
    {
        // Simulate Spotify playing audio and recently focused
        var process = new ProcessInfoModel
        {
            ProcessId = 6000,
            ProcessName = "spotify.exe",
            WorkingSetBytes = 200L * 1024 * 1024,
            LastForegroundUtc = DateTime.UtcNow.AddSeconds(-30),
            IsPlayingAudio = true,
            DiskWriteBytesPerSec = 0,
            NetBytesPerSec = 100_000
        };

        var result = HeuristicEngine.Evaluate(process);

        // Audio penalty = 80, focus penalty = 90, net penalty = 40
        // Score should be clamped to 0
        Assert.IsTrue(result.Score < 20, $"Expected score < 20 but got {result.Score}");
        Assert.AreEqual(RecommendationTier.DoNotSnooze, result.Tier);
    }

    [TestMethod]
    public void Evaluate_LongIdleHeavyProcess_SafeToSnooze()
    {
        // Simulate a very idle, memory-heavy Electron app
        var process = new ProcessInfoModel
        {
            ProcessId = 7000,
            ProcessName = "teams.exe",
            WorkingSetBytes = 2000L * 1024 * 1024, // 2 GB
            LastForegroundUtc = DateTime.UtcNow.AddMinutes(-20),
            IsPlayingAudio = false,
            DiskWriteBytesPerSec = 0,
            NetBytesPerSec = 0
        };

        var result = HeuristicEngine.Evaluate(process);

        // W_idle = 40 (capped), W_mem = 30 (capped), no penalties
        // Score = 70 => SafeToSnooze
        Assert.IsTrue(result.Score >= 70, $"Expected score >= 70 but got {result.Score}");
        Assert.AreEqual(RecommendationTier.SafeToSnooze, result.Tier);
    }

    [TestMethod]
    public void Evaluate_RecentlyFocused_PenaltyApplied()
    {
        var process = new ProcessInfoModel
        {
            ProcessId = 8000,
            ProcessName = "notepad.exe",
            WorkingSetBytes = 10L * 1024 * 1024,
            LastForegroundUtc = DateTime.UtcNow.AddSeconds(-10),
            IsPlayingAudio = false,
            DiskWriteBytesPerSec = 0,
            NetBytesPerSec = 0
        };

        var result = HeuristicEngine.Evaluate(process);

        // Focus penalty = 90, so score should be clamped to 0
        Assert.AreEqual(0, result.Score);
        Assert.AreEqual(90, result.FocusPenalty);
    }

    [TestMethod]
    public void Evaluate_HeavyDiskIO_PenaltyApplied()
    {
        var process = new ProcessInfoModel
        {
            ProcessId = 9000,
            ProcessName = "devenv.exe",
            WorkingSetBytes = 800L * 1024 * 1024,
            LastForegroundUtc = DateTime.UtcNow.AddMinutes(-5),
            IsPlayingAudio = false,
            DiskWriteBytesPerSec = 1_000_000, // 1 MB/s
            NetBytesPerSec = 0
        };

        var result = HeuristicEngine.Evaluate(process);

        Assert.AreEqual(50, result.DiskIoPenalty);
    }
}

