using Backend.Controllers;
using Backend.Models.Dto;
using Backend.Services;
using Backend.Seeding;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Moq;

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
    public async Task GetBoundedMinimalCounterSet_UsesPersistedGraph_WhenContextIsNotProvided()
    {
        var expected = new BoundedMinimalCounterSetDto
        {
            CounterNodeIds = ["O1"],
            ProofStatus = "proven",
            RunNumber = 12
        };
        var serviceMock = new Mock<IGraphService>();
        serviceMock
            .Setup(service => service.GetBoundedMinimalCounterSetAsync(
                "sample-medium",
                "R1",
                (string?)null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);
        var controller = new GraphsController(serviceMock.Object);

        var result = await controller.GetBoundedMinimalCounterSet(
            "sample-medium",
            "R1",
            null,
            CancellationToken.None);

        var okResult = result as OkObjectResult;
        Assert.IsNotNull(okResult);
        Assert.AreSame(expected, okResult.Value);
        serviceMock.Verify(service => service.GetBoundedMinimalCounterSetAsync(
            "sample-medium",
            "R1",
            (string?)null,
            It.IsAny<CancellationToken>()), Times.Once);
        serviceMock.Verify(service => service.GetBoundedMinimalCounterSetAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<GraphDto>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task GetBoundedMinimalCounterSet_UsesProvidedGraphContext()
    {
        var graphContext = CreateGraphDto();
        var expected = new BoundedMinimalCounterSetDto
        {
            CounterNodeIds = null,
            ProofStatus = "notProven",
            RunNumber = 13
        };
        var serviceMock = new Mock<IGraphService>();
        serviceMock
            .Setup(service => service.GetBoundedMinimalCounterSetAsync(
                "sample-medium",
                "R1",
                graphContext,
                (string?)null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);
        var controller = new GraphsController(serviceMock.Object);

        var result = await controller.GetBoundedMinimalCounterSet(
            "sample-medium",
            "R1",
            graphContext,
            CancellationToken.None);

        var okResult = result as OkObjectResult;
        Assert.IsNotNull(okResult);
        Assert.AreSame(expected, okResult.Value);
        serviceMock.Verify(service => service.GetBoundedMinimalCounterSetAsync(
            "sample-medium",
            "R1",
            graphContext,
            (string?)null,
            It.IsAny<CancellationToken>()), Times.Once);
        serviceMock.Verify(service => service.GetBoundedMinimalCounterSetAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task GetBoundedMinimalCounterSet_ReturnsNotFound_WhenGraphOrTargetDoesNotExist()
    {
        var serviceMock = new Mock<IGraphService>();
        serviceMock
            .Setup(service => service.GetBoundedMinimalCounterSetAsync(
                "missing",
                "R1",
                (string?)null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((BoundedMinimalCounterSetDto?)null);
        var controller = new GraphsController(serviceMock.Object);

        var result = await controller.GetBoundedMinimalCounterSet(
            "missing",
            "R1",
            null,
            CancellationToken.None);

        Assert.IsInstanceOfType<NotFoundResult>(result);
    }

    [TestMethod]
    public async Task GetBoundedMinimalCounterSet_ForwardsBenchmarkSetHeaderValue()
    {
        var expected = new BoundedMinimalCounterSetDto
        {
            CounterNodeIds = ["O1"],
            ProofStatus = "proven",
            RunNumber = 14
        };
        var serviceMock = new Mock<IGraphService>();
        serviceMock
            .Setup(service => service.GetBoundedMinimalCounterSetAsync(
                "sample-medium",
                "R1",
                "benchmark-set-1",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);
        var controller = new GraphsController(serviceMock.Object);

        var result = await controller.GetBoundedMinimalCounterSet(
            "sample-medium",
            "R1",
            null,
            CancellationToken.None,
            "benchmark-set-1");

        var ok = result as OkObjectResult;
        Assert.IsNotNull(ok);
        Assert.AreSame(expected, ok.Value);
        serviceMock.Verify(service => service.GetBoundedMinimalCounterSetAsync(
            "sample-medium",
            "R1",
            "benchmark-set-1",
            It.IsAny<CancellationToken>()), Times.Once);
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
                (string?)null,
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
                (string?)null,
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
