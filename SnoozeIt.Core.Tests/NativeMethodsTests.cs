namespace SnoozeIt.Core.Tests;

using System.Runtime.InteropServices;
using SnoozeIt.Core.Native;

[TestClass]
public class NativeMethodsTests
{
    [TestMethod]
    public void GlobalMemoryStatusEx_ReturnsValidSystemMemoryMetrics()
    {
        var memStatus = new NativeMethods.MEMORYSTATUSEX
        {
            dwLength = (uint)Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>()
        };

        bool success = NativeMethods.GlobalMemoryStatusEx(ref memStatus);

        Assert.IsTrue(success, "GlobalMemoryStatusEx should return true on Windows.");
        Assert.IsGreaterThan(0, (long)memStatus.ullTotalPhys, "Total physical RAM should be greater than 0.");
        Assert.IsGreaterThan(0, (long)memStatus.ullAvailPhys, "Available physical RAM should be greater than 0.");
        Assert.IsGreaterThanOrEqualTo(memStatus.ullAvailPhys, memStatus.ullTotalPhys, "Total physical RAM should be >= available RAM.");
        Assert.IsLessThanOrEqualTo(100u, memStatus.dwMemoryLoad, "Memory load percentage should be <= 100.");
    }
}
