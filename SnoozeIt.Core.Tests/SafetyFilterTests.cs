using SnoozeIt.Core.Engine;

namespace SnoozeIt.Core.Tests;

[TestClass]
public class SafetyFilterTests
{
    [TestMethod]
    [DataRow(0, "idle")]
    [DataRow(4, "system")]
    public void IsSafeToSuspend_KernelPids_ReturnsFalse(int pid, string name)
    {
        bool safe = SafetyFilter.IsSafeToSuspend(pid, name, out var reason);
        Assert.IsFalse(safe);
        Assert.IsNotNull(reason);
    }

    [TestMethod]
    [DataRow("csrss.exe")]
    [DataRow("lsass.exe")]
    [DataRow("svchost.exe")]
    [DataRow("dwm.exe")]
    [DataRow("explorer.exe")]
    [DataRow("snoozeit.service.exe")]
    [DataRow("snoozeit.client.exe")]
    public void IsSafeToSuspend_BlacklistedBinary_ReturnsFalse(string processName)
    {
        bool safe = SafetyFilter.IsSafeToSuspend(100, processName, out var reason);
        Assert.IsFalse(safe);
        Assert.IsNotNull(reason);
    }

    [TestMethod]
    [DataRow("chrome.exe")]
    [DataRow("slack.exe")]
    [DataRow("steam.exe")]
    [DataRow("notepad.exe")]
    public void IsSafeToSuspend_NormalProcess_ReturnsTrue(string processName)
    {
        bool safe = SafetyFilter.IsSafeToSuspend(1234, processName, out var reason);
        Assert.IsTrue(safe);
        Assert.IsNull(reason);
    }

    [TestMethod]
    public void IsSafeToSuspend_EmptyName_ReturnsFalse()
    {
        bool safe = SafetyFilter.IsSafeToSuspend(1234, "", out var reason);
        Assert.IsFalse(safe);
        Assert.IsNotNull(reason);
    }

    [TestMethod]
    public void IsSafeToSuspend_CaseInsensitive_ReturnsFalse()
    {
        // Blacklist should be case-insensitive
        bool safe = SafetyFilter.IsSafeToSuspend(100, "SVCHOST.EXE", out _);
        Assert.IsFalse(safe);
    }
}

