using Backend.Reporting;

namespace backend.Tests.Reporting;

[TestClass]
public class PerformanceBuildInfoCaptureTests
{
    [TestMethod]
    public void Capture_RecordsActualBuildModeAndRuntimeDescription()
    {
        var build = PerformanceBuildInfoCapture.Capture(
            "abc123",
            dirty: true,
            gitBranch: "feature/reporting");

        Assert.IsTrue(build.Configuration is "Debug" or "Release");
        Assert.IsFalse(string.IsNullOrWhiteSpace(build.DotNetVersion));
        Assert.IsFalse(string.IsNullOrWhiteSpace(build.OperatingSystem));
        Assert.IsFalse(string.IsNullOrWhiteSpace(build.ProcessArchitecture));
        Assert.IsTrue(build.LogicalProcessorCount > 0);
        Assert.AreEqual("abc123", build.GitCommit);
        Assert.AreEqual(true, build.Dirty);
        Assert.AreEqual("feature/reporting", build.GitBranch);
    }
}
