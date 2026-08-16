using Backend.Calculation;
using Backend.Insights.Benchmarking;
using Backend.Models.Domain;
using Backend.Seeding;
using BenchmarkDotNet.Attributes;

namespace Backend.Benchmarks;

[MemoryDiagnoser]
[BenchmarkCategory(BenchmarkCategories.Quick)]
public class LikelihoodRecalculationBenchmarks
{
    private readonly GraphLikelihoodCalculator _calculator = new();
    private Graph _graph = null!;
    private GraphCalculationContext _context = null!;
    private string _changedNodeId = string.Empty;

    [GlobalSetup]
    public void GlobalSetup()
    {
        var fixture = DeterministicStressGraphFixtureFactory.Create(
            StressGraphSeedIds.Balanced1K);
        _graph = fixture.CreateGraph();
        _changedNodeId = fixture.DeepestNodeId;
    }

    [IterationSetup]
    public void IterationSetup()
    {
        _context = GraphCalculationContext.From(_graph.Nodes, _graph.Edges);
    }

    [Benchmark(Description = "likelihood-recalculate-v0 / balanced 1K / deepest leaf")]
    public Dictionary<string, decimal> RecalculateAncestors()
    {
        return _calculator.RecalculateAncestors(_context, _changedNodeId);
    }
}
