using Backend.Controllers;
using Backend.Models.Dto;
using Backend.Services;
using Backend.Seeding;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Moq;
using System.Text.Json;

namespace backend.Tests.Controllers;

[TestClass]
public class GraphsControllerTests
{
    [TestMethod]
    public async Task GetSummaries_ReturnsOkWithCatalog()
    {
        IReadOnlyList<GraphSummaryDto> summaries =
        [
            new()
            {
                Slug = "sample-medium",
                Title = "Sample Medium Reasoning Graph",
                Description = "Seed graph"
            },
            new()
            {
                Slug = "flat-earth-large",
                Title = "Large Flat-Earth Reasoning Graph"
            }
        ];
        var serviceMock = new Mock<IGraphService>();
        serviceMock
            .Setup(service => service.GetSummariesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(summaries);
        var controller = new GraphsController(serviceMock.Object);

        var result = await controller.GetSummaries(CancellationToken.None);

        var okResult = result as OkObjectResult;
        Assert.IsNotNull(okResult);
        Assert.AreSame(summaries, okResult.Value);
    }

    [TestMethod]
    public async Task GetSummaries_ReturnsOkWithEmptyList_WhenNoGraphsExist()
    {
        IReadOnlyList<GraphSummaryDto> summaries = Array.Empty<GraphSummaryDto>();
        var serviceMock = new Mock<IGraphService>();
        serviceMock
            .Setup(service => service.GetSummariesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(summaries);
        var controller = new GraphsController(serviceMock.Object);

        var result = await controller.GetSummaries(CancellationToken.None);

        var okResult = result as OkObjectResult;
        Assert.IsNotNull(okResult);
        var payload = okResult.Value as IReadOnlyList<GraphSummaryDto>;
        Assert.IsNotNull(payload);
        Assert.AreEqual(0, payload.Count);
    }

    [TestMethod]
    public async Task ResetDatabase_NoBody_RequestsBaseSeedOnly()
    {
        var serviceMock = new Mock<IGraphService>();
        var controller = new GraphsController(serviceMock.Object);

        var result = await controller.ResetDatabase(null, CancellationToken.None);

        Assert.IsInstanceOfType<NoContentResult>(result);
        serviceMock.Verify(service => service.ResetDatabaseAsync(
            It.Is<IReadOnlyCollection<string>>(ids => ids.Count == 0),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task ResetDatabase_PassesSelectedStressGraphIds()
    {
        var serviceMock = new Mock<IGraphService>();
        var controller = new GraphsController(serviceMock.Object);
        var request = new ResetDatabaseRequestDto
        {
            StressGraphIds =
            [
                StressGraphSeedIds.Balanced1K,
                StressGraphSeedIds.Deep10K
            ]
        };

        var result = await controller.ResetDatabase(request, CancellationToken.None);

        Assert.IsInstanceOfType<NoContentResult>(result);
        serviceMock.Verify(service => service.ResetDatabaseAsync(
            It.Is<IReadOnlyCollection<string>>(ids => ids.SequenceEqual(request.StressGraphIds)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task ResetDatabase_CompleteTargetExpectation_UsesGuardedServiceOverload()
    {
        var serviceMock = new Mock<IGraphService>();
        var controller = new GraphsController(serviceMock.Object);
        var expectation = new DatabaseResetTargetExpectation(
            "logiclikely_benchmark_test",
            DatabaseResetTargetIdentity.ComputeFingerprint("stable-target-tuple"));
        var request = new ResetDatabaseRequestDto
        {
            StressGraphIds = [StressGraphSeedIds.Balanced1K],
            ExpectedDatabaseName = expectation.DatabaseName,
            ExpectedDatabaseFingerprint = expectation.Fingerprint
        };

        var result = await controller.ResetDatabase(request, CancellationToken.None);

        Assert.IsInstanceOfType<NoContentResult>(result);
        serviceMock.Verify(service => service.ResetDatabaseAsync(
            It.Is<IReadOnlyCollection<string>>(ids =>
                ids.SequenceEqual(new[] { StressGraphSeedIds.Balanced1K })),
            It.Is<DatabaseResetTargetExpectation>(value => value == expectation),
            It.IsAny<CancellationToken>()), Times.Once);
        serviceMock.Verify(service => service.ResetDatabaseAsync(
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task ResetDatabase_IncompleteTargetExpectation_ReturnsStructuredBadRequestWithoutReset()
    {
        var serviceMock = new Mock<IGraphService>();
        var controller = new GraphsController(serviceMock.Object);

        var result = await controller.ResetDatabase(
            new ResetDatabaseRequestDto
            {
                StressGraphIds = [StressGraphSeedIds.Balanced1K],
                ExpectedDatabaseName = "logiclikely_benchmark_test"
            },
            CancellationToken.None);

        var badRequest = result as BadRequestObjectResult;
        Assert.IsNotNull(badRequest);
        var payload = JsonSerializer.SerializeToElement(badRequest.Value);
        Assert.AreEqual(
            "database-reset-identity-expectation-incomplete",
            payload.GetProperty("code").GetString());
        serviceMock.Verify(service => service.ResetDatabaseAsync(
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<CancellationToken>()), Times.Never);
        serviceMock.Verify(service => service.ResetDatabaseAsync(
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<DatabaseResetTargetExpectation>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task ResetDatabase_TargetMismatch_ReturnsOpaqueStructuredConflict()
    {
        var expectation = new DatabaseResetTargetExpectation(
            "logiclikely_benchmark_test",
            DatabaseResetTargetIdentity.ComputeFingerprint("expected-target-tuple"));
        var serviceMock = new Mock<IGraphService>();
        serviceMock
            .Setup(service => service.ResetDatabaseAsync(
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<DatabaseResetTargetExpectation>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DatabaseResetIdentityMismatchException(
                DatabaseResetIdentityMismatchKind.TargetFingerprint));
        var controller = new GraphsController(serviceMock.Object);

        var result = await controller.ResetDatabase(
            new ResetDatabaseRequestDto
            {
                ExpectedDatabaseName = expectation.DatabaseName,
                ExpectedDatabaseFingerprint = expectation.Fingerprint
            },
            CancellationToken.None);

        var conflict = result as ConflictObjectResult;
        Assert.IsNotNull(conflict);
        var payload = JsonSerializer.SerializeToElement(conflict.Value);
        Assert.AreEqual("database-reset-identity-mismatch", payload.GetProperty("code").GetString());
        var raw = payload.GetRawText();
        Assert.IsFalse(raw.Contains(expectation.DatabaseName, StringComparison.Ordinal));
        Assert.IsFalse(raw.Contains(expectation.Fingerprint, StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ResetDatabase_UnknownStressGraphId_ReturnsBadRequest()
    {
        var serviceMock = new Mock<IGraphService>();
        serviceMock
            .Setup(service => service.ResetDatabaseAsync(
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidStressGraphSeedSelectionException(["unknown"]));
        var controller = new GraphsController(serviceMock.Object);

        var result = await controller.ResetDatabase(
            new ResetDatabaseRequestDto { StressGraphIds = ["unknown"] },
            CancellationToken.None);

        Assert.IsInstanceOfType<BadRequestObjectResult>(result);
    }

    [TestMethod]
    public void ResetDatabase_AllowsAnEmptyHttpRequestBody()
    {
        var method = typeof(GraphsController).GetMethod(nameof(GraphsController.ResetDatabase));
        Assert.IsNotNull(method);
        var bodyAttribute = method.GetParameters()[0]
            .GetCustomAttributes(typeof(FromBodyAttribute), inherit: true)
            .Cast<FromBodyAttribute>()
            .Single();

        Assert.AreEqual(EmptyBodyBehavior.Allow, bodyAttribute.EmptyBodyBehavior);
    }

    [DataTestMethod]
    [DataRow(nameof(GraphsController.GetMinimalCounterSet))]
    [DataRow(nameof(GraphsController.GetEvidenceImpactRanking))]
    [DataRow(nameof(GraphsController.GetLeastRobustNode))]
    [DataRow(nameof(GraphsController.GetNodeRobustnessRanking))]
    public void LegacyAnalysisEndpoints_AllowAnEmptyGraphContextBody(string methodName)
    {
        var method = typeof(GraphsController).GetMethod(methodName);
        Assert.IsNotNull(method);
        var bodyAttribute = method.GetParameters()
            .SelectMany(parameter => parameter
                .GetCustomAttributes(typeof(FromBodyAttribute), inherit: true)
                .Cast<FromBodyAttribute>())
            .Single();

        Assert.AreEqual(EmptyBodyBehavior.Allow, bodyAttribute.EmptyBodyBehavior);
    }

    [TestMethod]
    public async Task GetBySlug_ReturnsOk_WhenGraphExists()
    {
        // arrange
        var dto = CreateGraphDto();
        var controller = CreateControllerWithServiceMock(slug: "sample-medium", dto);

        // act
        var result = await controller.GetBySlug("sample-medium", CancellationToken.None);

        // assert
        Assert.IsInstanceOfType<OkObjectResult>(result);
    }

    [TestMethod]
    public async Task GetBySlug_ReturnsNotFound_WhenGraphDoesNotExist()
    {
        // arrange
        var controller = CreateControllerWithServiceMock(slug: "missing", dto: null);

        // act
        var result = await controller.GetBySlug("missing", CancellationToken.None);

        // assert
        Assert.IsInstanceOfType<NotFoundResult>(result);
    }

    [TestMethod]
    public async Task GetBySlug_ReturnsDtoPayload_WhenGraphExists()
    {
        // arrange
        var dto = CreateGraphDto();
        var controller = CreateControllerWithServiceMock(slug: "sample-medium", dto);

        // act
        var result = await controller.GetBySlug("sample-medium", CancellationToken.None);

        // assert
        var okResult = result as OkObjectResult;

        Assert.IsNotNull(okResult);
        Assert.AreSame(dto, okResult.Value);
    }

    [TestMethod]
    public async Task UpdateNode_ReturnsNoContent_WhenUpdateSucceeds()
    {
        var serviceMock = new Mock<IGraphService>();
        var update = new GraphNodeUpdateDto
        {
            Kind = "claim",
            Title = "Updated title",
            BodyText = "Updated body",
            PriorOdds = 0.75m
        };

        serviceMock
            .Setup(service => service.UpdateNodeAsync(
                "sample-medium",
                "P1",
                update,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var controller = new GraphsController(serviceMock.Object);

        var result = await controller.UpdateNode("sample-medium", "P1", update, CancellationToken.None);

        Assert.IsInstanceOfType<NoContentResult>(result);
    }

    [TestMethod]
    public async Task UpdateNode_ReturnsNotFound_WhenUpdateFails()
    {
        var serviceMock = new Mock<IGraphService>();
        var update = new GraphNodeUpdateDto
        {
            Title = "Updated title"
        };

        serviceMock
            .Setup(service => service.UpdateNodeAsync(
                "sample-medium",
                "missing",
                update,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var controller = new GraphsController(serviceMock.Object);

        var result = await controller.UpdateNode("sample-medium", "missing", update, CancellationToken.None);

        Assert.IsInstanceOfType<NotFoundResult>(result);
    }

    private static GraphsController CreateControllerWithServiceMock(string slug, GraphDto? dto)
    {
        var serviceMock = new Mock<IGraphService>();

        serviceMock
            .Setup(service => service.GetBySlugAsync(slug, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        return new GraphsController(serviceMock.Object);
    }

    private static GraphDto CreateGraphDto()
    {
        return new GraphDto
        {
            Slug = "sample-medium",
            Title = "Sample Medium Reasoning Graph",
            Description = "Seed graph",
            Nodes =
            [
                new GraphNodeDto
                {
                    Id = "R1",
                    Kind = "root",
                    Title = "Earth is flat",
                    BodyText = "The Earth is flat."
                }
            ],
            Edges =
            [
                new GraphEdgeDto
                {
                    Id = "E-R-C1",
                    From = "C1",
                    To = "R1",
                    Kind = "support"
                }
            ]
        };
    }
}
