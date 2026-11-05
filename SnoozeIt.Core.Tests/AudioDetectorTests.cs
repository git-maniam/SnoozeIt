using SnoozeIt.Core.Native;

namespace SnoozeIt.Core.Tests;

[TestClass]
public class AudioDetectorTests
{
    [TestMethod]
    public void GetActiveAudioProcessIds_RunsWithoutThrowing()
    {
        // Must never throw, even if audio services or audio endpoints are missing
        var activePids = AudioDetector.GetActiveAudioProcessIds();
        Assert.IsNotNull(activePids);
    }
}

