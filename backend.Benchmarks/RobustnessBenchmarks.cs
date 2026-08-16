using Backend.Insights.Analysis;
using Backend.Insights.Benchmarking;
using Backend.Models.Domain;
using Backend.Seeding;
using BenchmarkDotNet.Attributes;

namespace Backend.Benchmarks;

[MemoryDiagnoser]
[BenchmarkCategory(BenchmarkCategories.Quick)]
public class RobustnessBenchmarks
{
    private readonly RobustnessV0Analyzer _analyzer = new();
    private Graph _graph = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _graph = DeterministicStressGraphFixtureFactory
            .Create(StressGraphSeedIds.Balanced1K)
            .CreateGraph();
    }

    [Benchmark(Description = "robustness-v0 / balanced 1K")]
    public RobustnessV0AnalysisResult NodeRobustness()
    {
        return _analyzer.Analyze(_graph);
    }
}
