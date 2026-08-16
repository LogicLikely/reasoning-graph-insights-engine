using Backend.Calculation;
using Backend.Insights.Analysis;
using Backend.Insights.Benchmarking;
using Backend.Models.Domain;
using Backend.Seeding;
using BenchmarkDotNet.Attributes;

namespace Backend.Benchmarks;

[MemoryDiagnoser]
[BenchmarkCategory(BenchmarkCategories.Quick)]
public class StrongestPathBenchmarks
{
    private readonly StrongestPathV1Analysis _analyzer = new();
    private Graph _graph = null!;
    private string _startNodeId = string.Empty;

    [GlobalSetup]
    public void GlobalSetup()
    {
        var fixture = DeterministicStressGraphFixtureFactory.Create(
            StressGraphSeedIds.SharedDiamond1K);
        _graph = fixture.CreateGraph();
        _startNodeId = fixture.RootNodeId;
    }

    [Benchmark(Description = "strongest-path-v1 / shared-diamond 1K / root down")]
    public StrongestPathV1Result StrongestPaths()
    {
        return _analyzer.Analyze(_graph, _startNodeId, PathDirection.Down);
    }
}
