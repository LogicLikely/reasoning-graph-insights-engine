using Backend.Insights.Analysis;
using Backend.Insights.Benchmarking;
using Backend.Models.Domain;
using Backend.Seeding;
using BenchmarkDotNet.Attributes;

namespace Backend.Benchmarks;

[MemoryDiagnoser]
[BenchmarkCategory(BenchmarkCategories.Quick)]
public class EvidenceImpactBenchmarks
{
    private readonly EvidenceImpactV0Analysis _analyzer = new();
    private Graph _graph = null!;
    private string _targetNodeId = string.Empty;

    [GlobalSetup]
    public void GlobalSetup()
    {
        var fixture = DeterministicStressGraphFixtureFactory.Create(
            StressGraphSeedIds.Wide1K);
        _graph = fixture.CreateGraph();
        _targetNodeId = fixture.RootNodeId;
    }

    [Benchmark(Description = "evidence-impact-v0 / wide 1K / root")]
    public EvidenceImpactV0Result EvidenceImpactRanking()
    {
        return _analyzer.Analyze(_graph, _targetNodeId);
    }
}
