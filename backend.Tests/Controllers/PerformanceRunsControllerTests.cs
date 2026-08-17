using Backend.Controllers;
using Backend.Reporting;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace backend.Tests.Controllers;

[TestClass]
public sealed class PerformanceRunsControllerTests
{
    [TestMethod]
    public async Task Get_ReturnsTheCompleteStoredDocument()
    {
        var expected = new PerformanceReportDocument();
        var store = new Mock<IPerformanceRunStore>();
        store
            .Setup(candidate => candidate.ReadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);
        var controller = new PerformanceRunsController(store.Object);

        var result = await controller.Get(CancellationToken.None);

        var ok = result as OkObjectResult;
        Assert.IsNotNull(ok);
        Assert.AreSame(expected, ok.Value);
        store.Verify(
            candidate => candidate.ReadAsync(CancellationToken.None),
            Times.Once);
    }

    [TestMethod]
    public async Task Get_ReturnsEmptySchemaVersionTwoDocument_WhenStoreIsEmpty()
    {
        var controller = new PerformanceRunsController(
            NullPerformanceRunStore.Instance);

        var result = await controller.Get(CancellationToken.None);

        var ok = result as OkObjectResult;
        Assert.IsNotNull(ok);
        var report = ok.Value as PerformanceReportDocument;
        Assert.IsNotNull(report);
        Assert.AreEqual(PerformanceReportDocument.CurrentSchemaVersion, report.SchemaVersion);
        Assert.AreEqual(0, report.BenchmarkSets.Count);
        Assert.AreEqual(0, report.Runs.Count);
    }

    [TestMethod]
    public async Task CreateBenchmarkSet_ReturnsTheBackendGeneratedSet()
    {
        var expected = new PerformanceBenchmarkSet
        {
            Id = "8dbb714ef1da429d9a58d1cbcd4eecf5",
            Name = "LL-699 baseline",
            CreatedAtUtc = new DateTimeOffset(2026, 8, 17, 15, 30, 0, TimeSpan.Zero)
        };
        var store = new Mock<IPerformanceRunStore>();
        store
            .Setup(candidate => candidate.CreateBenchmarkSetAsync(
                expected.Name,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);
        var controller = new PerformanceRunsController(store.Object);

        var result = await controller.CreateBenchmarkSet(
            new CreatePerformanceBenchmarkSetRequest { Name = expected.Name },
            CancellationToken.None);

        var created = result as ObjectResult;
        Assert.IsNotNull(created);
        Assert.AreEqual(201, created.StatusCode);
        Assert.AreSame(expected, created.Value);
    }

    [DataTestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public async Task CreateBenchmarkSet_RejectsMissingName(string? name)
    {
        var store = new Mock<IPerformanceRunStore>();
        var controller = new PerformanceRunsController(store.Object);

        var result = await controller.CreateBenchmarkSet(
            new CreatePerformanceBenchmarkSetRequest { Name = name },
            CancellationToken.None);

        Assert.IsInstanceOfType<BadRequestObjectResult>(result);
        store.Verify(candidate => candidate.CreateBenchmarkSetAsync(
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }
}
