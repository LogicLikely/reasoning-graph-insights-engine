using System.Text.Json;
using Backend.Calculation;
using Backend.Controllers;
using Backend.Models.Domain;
using Backend.Models.Dto;
using Backend.Repositories;
using Backend.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Moq;

namespace backend.Tests.Insights.Analysis;

[TestClass]
public sealed class Phase3LegacyEndpointCompatibilityTests
{
    [TestMethod]
    public async Task EvidenceImpactEndpoint_DbAndSuppliedContextPreserveLegacyDtoParity()
    {
        var graph = EvidenceGraph();
        var repository = RepositoryReturning(graph);
        var controller = new GraphsController(CreateService(repository.Object));

        var dbAction = await controller.GetEvidenceImpactRanking(
            graph.Slug,
            "target",
            null,
            CancellationToken.None);
        var contextAction = await controller.GetEvidenceImpactRanking(
            graph.Slug,
            "target",
            ToDto(graph),
            CancellationToken.None);

        var dbResult = OkValue<EvidenceImpactRankingDto>(dbAction);
        var contextResult = OkValue<EvidenceImpactRankingDto>(contextAction);
        AssertEvidenceParity(dbResult, contextResult);
        AssertEvidenceParity(
            new GraphLikelihoodCalculator().GetEvidenceImpactRanking(
                graph,
                "target",
                CancellationToken.None),
            dbResult);

        CollectionAssert.AreEqual(
            new[] { "support-a", "support-z" },
            dbResult.SupportingEvidence.Select(item => item.NodeId).ToArray());
        CollectionAssert.AreEqual(
            new[] { "counter" },
            dbResult.CounterEvidence.Select(item => item.NodeId).ToArray());
        Assert.IsTrue(dbResult.SupportingEvidence.All(item => item.ProbabilityDifference > 0d));
        Assert.IsTrue(dbResult.CounterEvidence.All(item => item.ProbabilityDifference < 0d));

        CollectionAssert.AreEqual(
            new[] { "CounterEvidence", "SupportingEvidence" },
            typeof(EvidenceImpactRankingDto)
                .GetProperties()
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());
        CollectionAssert.AreEqual(
            new[] { "LogLr", "NodeId", "ProbabilityDifference" },
            typeof(EvidenceImpactDto)
                .GetProperties()
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());

        repository.Verify(candidate => candidate.GetBySlugAsync(
            graph.Slug,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task EvidenceImpactEndpoint_PreservesLegacyScalarBehaviorOutsideTheRichDagContract()
    {
        var graph = EvidenceGraph();
        graph.Nodes.Add(Node("cycle-a", "claim"));
        graph.Nodes.Add(Node("cycle-b", "claim"));
        graph.Edges.Add(Edge("edge-cycle-a-b", "cycle-a", "cycle-b", "custom", 2m));
        graph.Edges.Add(Edge("edge-cycle-b-a", "cycle-b", "cycle-a", "custom", 2m));
        var repository = RepositoryReturning(graph);
        var controller = new GraphsController(CreateService(repository.Object));

        var expected = new GraphLikelihoodCalculator().GetEvidenceImpactRanking(
            graph,
            "target",
            CancellationToken.None);
        var actual = OkValue<EvidenceImpactRankingDto>(
            await controller.GetEvidenceImpactRanking(
                graph.Slug,
                "target",
                null,
                CancellationToken.None));

        AssertEvidenceParity(expected, actual);
    }

    [TestMethod]
    public async Task RobustnessEndpoints_UseScoreThenOrdinalLeastAndPreserveFullLegacyShape()
    {
        var graph = RobustnessTieGraph();
        var repository = RepositoryReturning(graph);
        var controller = new GraphsController(CreateService(repository.Object));
        var graphContext = ToDto(graph);

        var leastDb = OkValue<NodeRobustnessDto>(await controller.GetLeastRobustNode(
            graph.Slug,
            null,
            CancellationToken.None));
        var leastContext = OkValue<NodeRobustnessDto>(await controller.GetLeastRobustNode(
            graph.Slug,
            graphContext,
            CancellationToken.None));
        var rankingDb = OkValue<List<NodeRobustnessDto>>(await controller.GetNodeRobustnessRanking(
            graph.Slug,
            null,
            CancellationToken.None));
        var rankingContext = OkValue<List<NodeRobustnessDto>>(await controller.GetNodeRobustnessRanking(
            graph.Slug,
            graphContext,
            CancellationToken.None));

        AssertNodeRobustnessEqual(leastDb, leastContext);
        Assert.AreEqual("a", leastDb.NodeId,
            "Equal least scores must resolve by ordinal node ID, not graph insertion order.");
        Assert.AreEqual("a title", leastDb.NodeTitle);
        Assert.AreEqual(graph.Nodes.Count, rankingDb.Count,
            "The legacy ranking remains the complete graph-wide ranking, not the retained top 100.");
        CollectionAssert.AreEqual(
            new[] { "a", "z", "leaf-a", "leaf-z" },
            rankingDb.Select(item => item.NodeId).ToArray());
        AssertRobustnessParity(rankingDb, rankingContext);
        AssertNodeRobustnessEqual(rankingDb[0], leastDb);

        CollectionAssert.AreEqual(
            new[] { "NodeId", "NodeTitle", "Robustness" },
            typeof(NodeRobustnessDto)
                .GetProperties()
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());

        repository.Verify(candidate => candidate.GetBySlugAsync(
            graph.Slug,
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [TestMethod]
    public async Task AnalysisEndpoints_MissingGraphAndSuppliedSlugMismatchRemainNotFound()
    {
        var repository = new Mock<IGraphRepository>();
        repository
            .Setup(candidate => candidate.GetBySlugAsync(
                "missing",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Graph?)null);
        var controller = new GraphsController(CreateService(repository.Object));

        Assert.IsInstanceOfType<NotFoundResult>(await controller.GetEvidenceImpactRanking(
            "missing",
            "target",
            null,
            CancellationToken.None));
        Assert.IsInstanceOfType<NotFoundResult>(await controller.GetLeastRobustNode(
            "missing",
            null,
            CancellationToken.None));
        Assert.IsInstanceOfType<NotFoundResult>(await controller.GetNodeRobustnessRanking(
            "missing",
            null,
            CancellationToken.None));

        var mismatchedContext = ToDto(EvidenceGraph());
        Assert.AreNotEqual("route-slug", mismatchedContext.Slug);
        Assert.IsInstanceOfType<NotFoundResult>(await controller.GetEvidenceImpactRanking(
            "route-slug",
            "target",
            mismatchedContext,
            CancellationToken.None));
        Assert.IsInstanceOfType<NotFoundResult>(await controller.GetLeastRobustNode(
            "route-slug",
            mismatchedContext,
            CancellationToken.None));
        Assert.IsInstanceOfType<NotFoundResult>(await controller.GetNodeRobustnessRanking(
            "route-slug",
            mismatchedContext,
            CancellationToken.None));

        repository.Verify(candidate => candidate.GetBySlugAsync(
            "route-slug",
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task MinimalCounterEndpoint_PreservesSinglePropertyAndOkForValueOrNull()
    {
        var service = new Mock<IGraphService>();
        service
            .SetupSequence(candidate => candidate.GetMinimalCounterSetAsync(
                "legacy",
                "target",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "counter-a", "counter-b" })
            .ReturnsAsync((List<string>?)null);
        var controller = new GraphsController(service.Object);

        var populated = MinimalCounterPayload(await controller.GetMinimalCounterSet(
            "legacy",
            "target",
            null,
            CancellationToken.None));
        var empty = MinimalCounterPayload(await controller.GetMinimalCounterSet(
            "legacy",
            "target",
            null,
            CancellationToken.None));

        Assert.AreEqual(JsonValueKind.Array, populated.ValueKind);
        CollectionAssert.AreEqual(
            new[] { "counter-a", "counter-b" },
            populated.EnumerateArray().Select(item => item.GetString()).ToArray());
        Assert.AreEqual(JsonValueKind.Null, empty.ValueKind,
            "A null legacy heuristic result remains a successful 200 response.");
        service.Verify(candidate => candidate.GetMinimalCounterSetAsync(
            "legacy",
            "target",
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [TestMethod]
    public async Task LegacyControllersPropagatePreCancelledAnalysisRequests()
    {
        var graph = EvidenceGraph();
        var controller = new GraphsController(CreateService(new Mock<IGraphRepository>().Object));
        var context = ToDto(graph);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() =>
            controller.GetEvidenceImpactRanking(
                graph.Slug,
                "target",
                context,
                cancellation.Token));
        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() =>
            controller.GetEvidenceImpactRanking(
                graph.Slug,
                "missing-target",
                context,
                cancellation.Token));
        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() =>
            controller.GetLeastRobustNode(
                graph.Slug,
                context,
                cancellation.Token));
        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() =>
            controller.GetNodeRobustnessRanking(
                graph.Slug,
                context,
                cancellation.Token));
        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() =>
            controller.GetMinimalCounterSet(
                graph.Slug,
                "target",
                context,
                cancellation.Token));
    }

    [TestMethod]
    public void LegacyOptionalGraphContextsContinueToAllowAnEmptyRequestBody()
    {
        var methodNames = new[]
        {
            nameof(GraphsController.GetMinimalCounterSet),
            nameof(GraphsController.GetEvidenceImpactRanking),
            nameof(GraphsController.GetLeastRobustNode),
            nameof(GraphsController.GetNodeRobustnessRanking)
        };

        foreach (var methodName in methodNames)
        {
            var method = typeof(GraphsController).GetMethod(methodName);
            Assert.IsNotNull(method);
            var graphContextParameter = method.GetParameters()
                .Single(parameter => parameter.ParameterType == typeof(GraphDto));
            var fromBody = graphContextParameter
                .GetCustomAttributes(typeof(FromBodyAttribute), inherit: true)
                .Cast<FromBodyAttribute>()
                .Single();

            Assert.AreEqual(
                EmptyBodyBehavior.Allow,
                fromBody.EmptyBodyBehavior,
                $"{methodName} must continue accepting a body-less database-backed POST.");
        }
    }

    private static GraphService CreateService(IGraphRepository repository)
    {
        return new GraphService(repository, new GraphLikelihoodCalculator());
    }

    private static Mock<IGraphRepository> RepositoryReturning(Graph graph)
    {
        var repository = new Mock<IGraphRepository>();
        repository
            .Setup(candidate => candidate.GetBySlugAsync(
                graph.Slug,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(graph);
        return repository;
    }

    private static T OkValue<T>(IActionResult actionResult)
    {
        Assert.IsInstanceOfType<OkObjectResult>(actionResult);
        var ok = (OkObjectResult)actionResult;
        Assert.AreEqual(StatusCodes.Status200OK, ok.StatusCode);
        Assert.IsInstanceOfType<T>(ok.Value);
        return (T)ok.Value;
    }

    private static JsonElement MinimalCounterPayload(IActionResult actionResult)
    {
        Assert.IsInstanceOfType<OkObjectResult>(actionResult);
        var ok = (OkObjectResult)actionResult;
        Assert.AreEqual(StatusCodes.Status200OK, ok.StatusCode);
        Assert.IsNotNull(ok.Value);

        var payload = JsonSerializer.SerializeToElement(ok.Value);
        var properties = payload.EnumerateObject().ToArray();
        Assert.AreEqual(1, properties.Length);
        Assert.AreEqual("counterNodeIds", properties[0].Name);
        return properties[0].Value.Clone();
    }

    private static void AssertEvidenceParity(
        EvidenceImpactRankingDto expected,
        EvidenceImpactRankingDto actual)
    {
        Assert.AreEqual(expected.SupportingEvidence.Count, actual.SupportingEvidence.Count);
        Assert.AreEqual(expected.CounterEvidence.Count, actual.CounterEvidence.Count);
        for (var index = 0; index < expected.SupportingEvidence.Count; index++)
        {
            AssertEvidenceItemEqual(
                expected.SupportingEvidence[index],
                actual.SupportingEvidence[index]);
        }

        for (var index = 0; index < expected.CounterEvidence.Count; index++)
        {
            AssertEvidenceItemEqual(
                expected.CounterEvidence[index],
                actual.CounterEvidence[index]);
        }
    }

    private static void AssertEvidenceItemEqual(EvidenceImpactDto expected, EvidenceImpactDto actual)
    {
        Assert.AreEqual(expected.NodeId, actual.NodeId);
        Assert.AreEqual(expected.LogLr, actual.LogLr);
        Assert.AreEqual(expected.ProbabilityDifference, actual.ProbabilityDifference);
    }

    private static void AssertRobustnessParity(
        IReadOnlyList<NodeRobustnessDto> expected,
        IReadOnlyList<NodeRobustnessDto> actual)
    {
        Assert.AreEqual(expected.Count, actual.Count);
        for (var index = 0; index < expected.Count; index++)
        {
            AssertNodeRobustnessEqual(expected[index], actual[index]);
        }
    }

    private static void AssertNodeRobustnessEqual(
        NodeRobustnessDto expected,
        NodeRobustnessDto actual)
    {
        Assert.AreEqual(expected.NodeId, actual.NodeId);
        Assert.AreEqual(expected.NodeTitle, actual.NodeTitle);
        Assert.AreEqual(expected.Robustness, actual.Robustness);
    }

    private static Graph EvidenceGraph()
    {
        return GraphWith(
            [
                Node("target", "claim", priorOdds: 0.1m, posteriorOdds: 0.7m),
                Node("support-z", "evidence"),
                Node("support-a", "evidence"),
                Node("counter", "objection")
            ],
            [
                Edge("edge-support-z", "support-z", "target", "support", 2m),
                Edge("edge-support-a", "support-a", "target", "support", 2m),
                Edge("edge-counter", "counter", "target", "rebut", 0.25m)
            ]);
    }

    private static Graph RobustnessTieGraph()
    {
        return GraphWith(
            [
                Node("z", "claim", posteriorOdds: 0.4m),
                Node("leaf-z", "evidence"),
                Node("a", "claim", posteriorOdds: 0.4m),
                Node("leaf-a", "evidence")
            ],
            [
                Edge("edge-leaf-z", "leaf-z", "z", "support", 2m),
                Edge("edge-leaf-a", "leaf-a", "a", "support", 2m)
            ]);
    }

    private static Graph GraphWith(List<GraphNode> nodes, List<GraphEdge> edges)
    {
        return new Graph
        {
            Id = 42,
            Slug = "legacy-analysis",
            Title = "Legacy analysis compatibility fixture",
            Description = "Phase 3 adapter characterization",
            Nodes = nodes,
            Edges = edges
        };
    }

    private static GraphNode Node(
        string id,
        string kind,
        decimal priorOdds = 0m,
        decimal? posteriorOdds = null)
    {
        return new GraphNode
        {
            Id = id,
            Kind = kind,
            Title = $"{id} title",
            BodyText = id,
            PriorOdds = priorOdds,
            PosteriorOdds = posteriorOdds ?? priorOdds
        };
    }

    private static GraphEdge Edge(
        string id,
        string from,
        string to,
        string kind,
        decimal importanceToParent)
    {
        return new GraphEdge
        {
            Id = id,
            From = from,
            To = to,
            Kind = kind,
            ImportanceToParent = importanceToParent
        };
    }

    private static GraphDto ToDto(Graph graph)
    {
        return new GraphDto
        {
            Slug = graph.Slug,
            Title = graph.Title,
            Description = graph.Description,
            Nodes = graph.Nodes.Select(node => new GraphNodeDto
            {
                Id = node.Id,
                Kind = node.Kind,
                Title = node.Title,
                BodyText = node.BodyText,
                Category = node.Category,
                Tags = node.Tags.ToList(),
                PriorOdds = node.PriorOdds,
                PosteriorOdds = node.PosteriorOdds
            }).ToList(),
            Edges = graph.Edges.Select(edge => new GraphEdgeDto
            {
                Id = edge.Id,
                From = edge.From,
                To = edge.To,
                Kind = edge.Kind,
                ImportanceToParent = edge.ImportanceToParent
            }).ToList()
        };
    }
}
