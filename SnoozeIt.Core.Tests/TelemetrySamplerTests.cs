using SnoozeIt.Core.Engine;

namespace SnoozeIt.Core.Tests;

[TestClass]
public class TelemetrySamplerTests
{
    [TestMethod]
    public void SampleAllProcesses_ReturnsActiveProcessesWithRecommendations()
    {
        var sampler = new TelemetrySampler();
        var samples = sampler.SampleAllProcesses();

        Assert.IsNotNull(samples);
        Assert.IsNotEmpty(samples, "Expected at least one accessible process.");

        foreach (var (process, recommendation) in samples)
        {
            Assert.IsGreaterThan(0, process.ProcessId);
            Assert.IsNotNull(process.ProcessName);
            Assert.IsNotNull(recommendation);
            Assert.IsTrue(recommendation.Score >= 0 && recommendation.Score <= 100);
        }
    }
}
