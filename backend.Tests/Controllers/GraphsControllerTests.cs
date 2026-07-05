using Backend.Controllers;
using Backend.Models.Dto;
using Backend.Services;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace backend.Tests.Controllers;

[TestClass]
public class GraphsControllerTests
{
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
            LogOdds = 0.75m
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
