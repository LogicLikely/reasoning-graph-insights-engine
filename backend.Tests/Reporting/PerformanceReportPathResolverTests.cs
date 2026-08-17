using Backend.Reporting;

namespace backend.Tests.Reporting;

[TestClass]
public class PerformanceReportPathResolverTests
{
    [TestMethod]
    public void ResolveFromContentRoot_ReturnsHardcodedRepositoryArtifactPath()
    {
        var repositoryPath = Path.Combine(Path.GetTempPath(), "reasoning-graph-repository");
        var backendContentRoot = Path.Combine(repositoryPath, "backend");

        var result = PerformanceReportPathResolver.ResolveFromContentRoot(backendContentRoot);

        Assert.AreEqual(
            Path.Combine(
                repositoryPath,
                "artifacts",
                "performance",
                "performance-runs.json"),
            result);
    }

    [TestMethod]
    public void ResolveFromContentRoot_RejectsBlankPath()
    {
        Assert.ThrowsException<ArgumentException>(() =>
            PerformanceReportPathResolver.ResolveFromContentRoot(" "));
    }
}
