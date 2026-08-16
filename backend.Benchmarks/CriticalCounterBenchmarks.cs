using Backend.Insights.Analysis;
using Backend.Insights.Benchmarking;
using Backend.Insights.Contracts;
using Backend.Seeding;
using BenchmarkDotNet.Attributes;

namespace Backend.Benchmarks;

[MemoryDiagnoser]
[BenchmarkCategory(BenchmarkCategories.Quick)]
[Config(typeof(CriticalCounterBenchmarkConfig))]
public class CriticalCounterBenchmarks
{
    public const int QuickMaximumCandidateCount = 8;

    // This is a bounded smoke-run selector input, not a calibrated cutoff.
    public const int QuickUncalibratedAutoCandidateCutoff = 8;

    private const string TargetNodeId = "n-00015";

    private readonly CriticalCounterV1Analyzer _analyzer = new();
    private CriticalCounterV1AnalysisRequest _exactRequest = null!;
    private CriticalCounterV1AnalysisRequest _greedyRequest = null!;
    private CriticalCounterV1AnalysisRequest _autoExactRequest = null!;
    private CriticalCounterV1AnalysisRequest _autoGreedyRequest = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        var graph = DeterministicStressGraphFixtureFactory
            .Create(StressGraphSeedIds.Balanced1K)
            .CreateGraph();
        _ = CriticalCounterCandidateGuard.RequireAtMost(
            graph,
            TargetNodeId,
            QuickMaximumCandidateCount);

        _exactRequest = Request(graph, OperationStrategyNames.Exact, null);
        _greedyRequest = Request(graph, OperationStrategyNames.Greedy, null);
        _autoExactRequest = Request(
            graph,
            OperationStrategyNames.Auto,
            QuickUncalibratedAutoCandidateCutoff);
        _autoGreedyRequest = Request(
            graph,
            OperationStrategyNames.Auto,
            autoCandidateCutoff: 1);

        // This preflight is outside the measured benchmark loop. It keeps the
        // deterministic operation-counter columns honest if fixture or
        // algorithm semantics change.
        CriticalCounterBenchmarkMetadataCatalog.Verify(
            nameof(Exact),
            _analyzer.Analyze(_exactRequest));
        CriticalCounterBenchmarkMetadataCatalog.Verify(
            nameof(Greedy),
            _analyzer.Analyze(_greedyRequest));
        CriticalCounterBenchmarkMetadataCatalog.Verify(
            nameof(AutoSelectionAndExecution),
            _analyzer.Analyze(_autoExactRequest));
        CriticalCounterBenchmarkMetadataCatalog.Verify(
            nameof(AutoGreedySelectionAndExecution),
            _analyzer.Analyze(_autoGreedyRequest));
    }

    [Benchmark(Description = "critical-counter-v1 exact / balanced 1K / max 8 candidates / threshold -1 log-odds")]
    public CriticalCounterV1AnalysisResult Exact()
    {
        return _analyzer.Analyze(_exactRequest);
    }

    [Benchmark(Description = "critical-counter-v1 greedy / balanced 1K / max 8 candidates / threshold -1 log-odds")]
    public CriticalCounterV1AnalysisResult Greedy()
    {
        return _analyzer.Analyze(_greedyRequest);
    }

    [Benchmark(Description = "critical-counter-v1 auto -> exact / balanced 1K / uncalibrated cutoff 8")]
    public CriticalCounterV1AnalysisResult AutoSelectionAndExecution()
    {
        return _analyzer.Analyze(_autoExactRequest);
    }

    [Benchmark(Description = "critical-counter-v1 auto -> greedy / balanced 1K / uncalibrated cutoff 1")]
    public CriticalCounterV1AnalysisResult AutoGreedySelectionAndExecution()
    {
        return _analyzer.Analyze(_autoGreedyRequest);
    }

    private static CriticalCounterV1AnalysisRequest Request(
        Backend.Models.Domain.Graph graph,
        string requestedStrategy,
        int? autoCandidateCutoff)
    {
        return new CriticalCounterV1AnalysisRequest(
            graph,
            TargetNodeId,
            requestedStrategy,
            CriticalCounterV1Contract.DefaultThresholdLogOdds,
            autoCandidateCutoff);
    }
}
