using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace Backend.Insights.Measurement;

public static class InsightMeasurementLayers
{
    public const string PostgreSqlRepository = "postgresql-repository";
    public const string BackendServiceApi = "backend-service-api";
    public const string BenchmarkOrchestration = "benchmark-orchestration";
    public const string Transport = "transport";
    public const string BrowserData = "browser-data";
    public const string GraphMap = "graph-map";
    public const string LabResult = "lab-result";
    public const string EndToEnd = "end-to-end";
}

public static class InsightMeasurementPhases
{
    public const string ConnectionOpenWait = "connection-open-wait";
    public const string GraphLookup = "graph-lookup";
    public const string NodeQuery = "node-query";
    public const string EdgeQuery = "edge-query";
    public const string EvidenceJsonMaterialization = "evidence-json-materialization";
    public const string GraphConstruction = "graph-construction";
    public const string CatalogAggregation = "catalog-aggregation";

    public const string DtoMapping = "dto-mapping";
    public const string Validation = "validation";
    public const string CalculationContextConstruction = "calculation-context-construction";
    public const string Algorithm = "algorithm";
    public const string Ranking = "ranking";
    public const string ResultShaping = "result-shaping";
    public const string DigestGeneration = "digest-generation";
    public const string Serialization = "serialization";

    public const string FixtureConstruction = "fixture-construction";
    public const string OperationExecution = "operation-execution";
    public const string WorkerSupervision = "worker-supervision";
    public const string ExactGreedyQualityComparison = "exact-greedy-quality-comparison";
    public const string Persistence = "persistence";
    public const string ExportValidation = "export-validation";

    public const string ResponseBytes = "response-bytes";
    public const string TimeToFirstByte = "time-to-first-byte";
    public const string FullTransfer = "full-transfer";

    public const string AxiosReceiptParse = "axios-receipt-parse";
    public const string JsonParse = "json-parse";
    public const string DomainMapping = "domain-mapping";
    public const string GraphMapAdapter = "graph-map-adapter";
    public const string SearchIndexConstruction = "search-index-construction";
    public const string SearchCompletion = "search-completion";

    public const string NodeEdgeMaterialization = "node-edge-materialization";
    public const string DagreLayout = "dagre-layout";
    public const string ReactCommit = "react-commit";
    public const string DeferredEdgeCommit = "deferred-edge-commit";
    public const string ViewportFit = "viewport-fit";

    public const string ResultRender = "result-render";

    public const string ActionToStableResultAndView = "action-to-stable-result-and-view";

    public static string AlgorithmSubphase(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var phase = $"{Algorithm}.{name}";
        if (!InsightPhaseRegistry.IsKnown(InsightMeasurementLayers.BackendServiceApi, phase))
        {
            throw new ArgumentException(
                "Algorithm subphase names must be lowercase dot-separated kebab-case tokens.",
                nameof(name));
        }

        return phase;
    }
}

public sealed record InsightPhaseDefinition(
    int Order,
    string Layer,
    string Phase,
    bool ServerSideMeasurable,
    bool IsPhasePrefix = false);

public static partial class InsightPhaseRegistry
{
    private static readonly ReadOnlyCollection<InsightPhaseDefinition> OrderedDefinitions =
        Array.AsReadOnly<InsightPhaseDefinition>(
        [
            Phase(InsightMeasurementLayers.PostgreSqlRepository, InsightMeasurementPhases.ConnectionOpenWait, true),
            Phase(InsightMeasurementLayers.PostgreSqlRepository, InsightMeasurementPhases.GraphLookup, true),
            Phase(InsightMeasurementLayers.PostgreSqlRepository, InsightMeasurementPhases.NodeQuery, true),
            Phase(InsightMeasurementLayers.PostgreSqlRepository, InsightMeasurementPhases.EdgeQuery, true),
            Phase(InsightMeasurementLayers.PostgreSqlRepository, InsightMeasurementPhases.EvidenceJsonMaterialization, true),
            Phase(InsightMeasurementLayers.PostgreSqlRepository, InsightMeasurementPhases.GraphConstruction, true),
            Phase(InsightMeasurementLayers.PostgreSqlRepository, InsightMeasurementPhases.CatalogAggregation, true),

            Phase(InsightMeasurementLayers.BackendServiceApi, InsightMeasurementPhases.DtoMapping, true),
            Phase(InsightMeasurementLayers.BackendServiceApi, InsightMeasurementPhases.Validation, true),
            Phase(InsightMeasurementLayers.BackendServiceApi, InsightMeasurementPhases.CalculationContextConstruction, true),
            Phase(InsightMeasurementLayers.BackendServiceApi, InsightMeasurementPhases.Algorithm, true, isPrefix: true),
            Phase(InsightMeasurementLayers.BackendServiceApi, InsightMeasurementPhases.Ranking, true),
            Phase(InsightMeasurementLayers.BackendServiceApi, InsightMeasurementPhases.ResultShaping, true),
            Phase(InsightMeasurementLayers.BackendServiceApi, InsightMeasurementPhases.DigestGeneration, true),
            Phase(InsightMeasurementLayers.BackendServiceApi, InsightMeasurementPhases.Serialization, true),

            Phase(InsightMeasurementLayers.BenchmarkOrchestration, InsightMeasurementPhases.FixtureConstruction, false),
            Phase(InsightMeasurementLayers.BenchmarkOrchestration, InsightMeasurementPhases.OperationExecution, false),
            Phase(InsightMeasurementLayers.BenchmarkOrchestration, InsightMeasurementPhases.WorkerSupervision, false),
            Phase(InsightMeasurementLayers.BenchmarkOrchestration, InsightMeasurementPhases.ExactGreedyQualityComparison, false),
            Phase(InsightMeasurementLayers.BenchmarkOrchestration, InsightMeasurementPhases.Persistence, false),
            Phase(InsightMeasurementLayers.BenchmarkOrchestration, InsightMeasurementPhases.ExportValidation, false),

            Phase(InsightMeasurementLayers.Transport, InsightMeasurementPhases.ResponseBytes, true),
            Phase(InsightMeasurementLayers.Transport, InsightMeasurementPhases.TimeToFirstByte, false),
            Phase(InsightMeasurementLayers.Transport, InsightMeasurementPhases.FullTransfer, false),

            Phase(InsightMeasurementLayers.BrowserData, InsightMeasurementPhases.AxiosReceiptParse, false),
            Phase(InsightMeasurementLayers.BrowserData, InsightMeasurementPhases.JsonParse, false),
            Phase(InsightMeasurementLayers.BrowserData, InsightMeasurementPhases.DomainMapping, false),
            Phase(InsightMeasurementLayers.BrowserData, InsightMeasurementPhases.GraphMapAdapter, false),
            Phase(InsightMeasurementLayers.BrowserData, InsightMeasurementPhases.SearchIndexConstruction, false),
            Phase(InsightMeasurementLayers.BrowserData, InsightMeasurementPhases.SearchCompletion, false),

            Phase(InsightMeasurementLayers.GraphMap, InsightMeasurementPhases.NodeEdgeMaterialization, false),
            Phase(InsightMeasurementLayers.GraphMap, InsightMeasurementPhases.DagreLayout, false),
            Phase(InsightMeasurementLayers.GraphMap, InsightMeasurementPhases.ReactCommit, false),
            Phase(InsightMeasurementLayers.GraphMap, InsightMeasurementPhases.DeferredEdgeCommit, false),
            Phase(InsightMeasurementLayers.GraphMap, InsightMeasurementPhases.ViewportFit, false),

            Phase(InsightMeasurementLayers.LabResult, InsightMeasurementPhases.ResultRender, false),
            Phase(InsightMeasurementLayers.LabResult, InsightMeasurementPhases.ReactCommit, false),

            Phase(InsightMeasurementLayers.EndToEnd, InsightMeasurementPhases.ActionToStableResultAndView, false)
        ]);

    private static readonly IReadOnlyDictionary<(string Layer, string Phase), InsightPhaseDefinition>
        ExactDefinitions = new ReadOnlyDictionary<(string Layer, string Phase), InsightPhaseDefinition>(
            OrderedDefinitions
                .Where(definition => !definition.IsPhasePrefix)
                .ToDictionary(definition => (definition.Layer, definition.Phase)));

    private static readonly InsightPhaseDefinition AlgorithmPrefixDefinition =
        OrderedDefinitions.Single(definition => definition.IsPhasePrefix);

    public static IReadOnlyList<InsightPhaseDefinition> Definitions => OrderedDefinitions;

    public static bool IsKnown(string layer, string phase) =>
        TryGetDefinition(layer, phase, out _);

    public static bool TryGetDefinition(
        string layer,
        string phase,
        out InsightPhaseDefinition? definition)
    {
        definition = null;
        if (string.IsNullOrWhiteSpace(layer) || string.IsNullOrWhiteSpace(phase))
        {
            return false;
        }

        if (ExactDefinitions.TryGetValue((layer, phase), out definition))
        {
            return true;
        }

        if (string.Equals(layer, AlgorithmPrefixDefinition.Layer, StringComparison.Ordinal) &&
            string.Equals(phase, AlgorithmPrefixDefinition.Phase, StringComparison.Ordinal))
        {
            definition = AlgorithmPrefixDefinition;
            return true;
        }

        if (string.Equals(layer, InsightMeasurementLayers.BackendServiceApi, StringComparison.Ordinal) &&
            phase.StartsWith($"{InsightMeasurementPhases.Algorithm}.", StringComparison.Ordinal) &&
            PhaseTokenPattern().IsMatch(phase))
        {
            definition = AlgorithmPrefixDefinition;
            return true;
        }

        return false;
    }

    public static int Compare(
        string leftLayer,
        string leftPhase,
        string rightLayer,
        string rightPhase)
    {
        var leftKnown = TryGetDefinition(leftLayer, leftPhase, out var leftDefinition);
        var rightKnown = TryGetDefinition(rightLayer, rightPhase, out var rightDefinition);

        if (leftKnown && rightKnown)
        {
            var order = leftDefinition!.Order.CompareTo(rightDefinition!.Order);
            if (order != 0)
            {
                return order;
            }
        }
        else if (leftKnown != rightKnown)
        {
            return leftKnown ? -1 : 1;
        }

        var layerOrder = StringComparer.Ordinal.Compare(leftLayer, rightLayer);
        return layerOrder != 0
            ? layerOrder
            : StringComparer.Ordinal.Compare(leftPhase, rightPhase);
    }

    private static InsightPhaseDefinition Phase(
        string layer,
        string phase,
        bool serverSideMeasurable,
        bool isPrefix = false) =>
        new(NextOrder++, layer, phase, serverSideMeasurable, isPrefix);

    private static int NextOrder { get; set; }

    [GeneratedRegex("^algorithm\\.[a-z0-9]+(?:-[a-z0-9]+)*(?:\\.[a-z0-9]+(?:-[a-z0-9]+)*)*$", RegexOptions.CultureInvariant)]
    private static partial Regex PhaseTokenPattern();
}
