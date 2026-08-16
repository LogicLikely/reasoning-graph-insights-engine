using Backend.Calculation;
using Backend.Insights.Benchmarking;
using Backend.Seeding;
using BenchmarkDotNet.Attributes;

namespace Backend.Benchmarks;

[MemoryDiagnoser]
[BenchmarkCategory(BenchmarkCategories.Quick)]
public class SinglePairPathBenchmarks
{
    private const string BoundedTargetNodeId = "n-00935";

    private readonly GraphLikelihoodCalculator _calculator = new();
    private GraphCalculationContext _context = null!;
    private string _startNodeId = string.Empty;

    [GlobalSetup]
    public void GlobalSetup()
    {
        var fixture = DeterministicStressGraphFixtureFactory.Create(
            StressGraphSeedIds.Deep1K);
        var graph = fixture.CreateGraph();
        _context = GraphCalculationContext.From(graph.Nodes, graph.Edges);
        _startNodeId = fixture.DeepestNodeId;
    }

    [Benchmark(Description = "single-pair-v0 minimum / deep 1K / bounded 64 edges")]
    public decimal? MinimumPath()
    {
        return _calculator.GetMinLogPath(
            _context,
            _startNodeId,
            BoundedTargetNodeId);
    }

    [Benchmark(Description = "single-pair-v0 maximum / deep 1K / bounded 64 edges")]
    public decimal? MaximumPath()
    {
        return _calculator.GetMaxLogPath(
            _context,
            _startNodeId,
            BoundedTargetNodeId);
    }
}
