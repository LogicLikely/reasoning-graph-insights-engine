using Backend.Calculation;
using Backend.Insights.Benchmarking;
using Backend.Models.Domain;
using Backend.Seeding;
using BenchmarkDotNet.Attributes;

namespace Backend.Benchmarks;

[MemoryDiagnoser]
[BenchmarkCategory(BenchmarkCategories.Quick)]
public class GraphCalculationContextBenchmarks
{
    private Graph _graph = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _graph = DeterministicStressGraphFixtureFactory
            .Create(StressGraphSeedIds.Balanced1K)
            .CreateGraph();
    }

    [Benchmark(Description = "calculation-context / balanced 1K")]
    public GraphCalculationContext ConstructCalculationContext()
    {
        return GraphCalculationContext.From(_graph.Nodes, _graph.Edges);
    }
}
