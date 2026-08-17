using Backend.Reporting;

namespace backend.Tests.Reporting;

[TestClass]
public class PerformanceMeasurementTests
{
    [TestMethod]
    public void Stop_CapturesWallClockCpuAllocationsAndGarbageCollections()
    {
        using var measurement = PerformanceMeasurement.Start();
        var allocation = GC.AllocateUninitializedArray<byte>(4_096);
        allocation[0] = 1;

        var result = measurement.Stop();
        GC.KeepAlive(allocation);

        Assert.AreEqual(TimeSpan.Zero, result.StartedAtUtc.Offset);
        Assert.IsTrue(result.ElapsedMilliseconds >= 0);
        Assert.IsTrue(result.Resources.CpuTimeMilliseconds >= 0);
        Assert.IsNotNull(result.Resources.AllocatedBytes);
        Assert.IsTrue(result.Resources.AllocatedBytes.Value >= 4_096L);
        Assert.IsTrue(result.Resources.Gen0Collections >= 0);
        Assert.IsTrue(result.Resources.Gen1Collections >= 0);
        Assert.IsTrue(result.Resources.Gen2Collections >= 0);
        Assert.AreEqual(
            "processCpuTimeDelta",
            result.Resources.CpuMeasurement);
        Assert.AreEqual(
            "currentThreadAllocatedBytesDelta",
            result.Resources.AllocationMeasurement);
    }

    [TestMethod]
    public void Stop_IsIdempotent()
    {
        using var measurement = PerformanceMeasurement.Start();

        var first = measurement.Stop();
        var second = measurement.Stop();

        Assert.AreSame(first, second);
    }

    [TestMethod]
    public void Stop_MarksAllocationMeasurementUnavailableAfterThreadChange()
    {
        using var measurement = PerformanceMeasurement.Start();
        PerformanceMeasurementResult? result = null;
        var thread = new Thread(() => result = measurement.Stop());

        thread.Start();
        thread.Join();

        Assert.IsNotNull(result);
        Assert.IsNull(result.Resources.AllocatedBytes);
        Assert.AreEqual(
            "unavailableThreadChanged",
            result.Resources.AllocationMeasurement);
    }
}
