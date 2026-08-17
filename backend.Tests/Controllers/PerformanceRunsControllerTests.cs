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
    public async Task Get_ReturnsEmptySchemaVersionOneDocument_WhenStoreIsEmpty()
    {
        var controller = new PerformanceRunsController(
            NullPerformanceRunStore.Instance);

        var result = await controller.Get(CancellationToken.None);

        var ok = result as OkObjectResult;
        Assert.IsNotNull(ok);
        var report = ok.Value as PerformanceReportDocument;
        Assert.IsNotNull(report);
        Assert.AreEqual(PerformanceReportDocument.CurrentSchemaVersion, report.SchemaVersion);
        Assert.AreEqual(0, report.Runs.Count);
    }
}
