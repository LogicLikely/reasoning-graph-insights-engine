using System.Text.Json;
using System.Text.RegularExpressions;
using Backend.Insights.Contracts;
using Backend.Insights.Measurement;

namespace Backend.Insights.Export;

public sealed partial class RunExportValidator
{
    public RunExportValidationResult Validate(VersionedRunExport? export)
    {
        var issues = new List<RunExportValidationIssue>();
        Validate(export, issues, checkSectionDigests: true, requireNormalizedOrder: true);
        return issues.Count == 0
            ? RunExportValidationResult.Valid
            : new RunExportValidationResult(issues.AsReadOnly());
    }

    public void ValidateOrThrow(VersionedRunExport? export)
    {
        var result = Validate(export);
        if (!result.IsValid)
        {
            throw new RunExportValidationException(result.Issues);
        }
    }

    internal IReadOnlyList<RunExportValidationIssue> ValidateForCreation(
        VersionedRunExport export)
    {
        var issues = new List<RunExportValidationIssue>();
        Validate(export, issues, checkSectionDigests: false, requireNormalizedOrder: false);
        return issues.AsReadOnly();
    }

    private static void Validate(
        VersionedRunExport? export,
        ICollection<RunExportValidationIssue> issues,
        bool checkSectionDigests,
        bool requireNormalizedOrder)
    {
        if (export is null)
        {
            Add(issues, "$", "required", "Export cannot be null.");
            return;
        }

        if (!string.Equals(
                export.SchemaIdentity,
                VersionedRunExport.CurrentSchemaIdentity,
                StringComparison.Ordinal))
        {
            Add(issues, "$.schemaIdentity", "const", "Unexpected export schema identity.");
        }

        if (export.SchemaVersion != VersionedRunExport.CurrentSchemaVersion)
        {
            Add(issues, "$.schemaVersion", "const", "Unexpected export schema version.");
        }

        if (export.Manifest is null)
        {
            Add(issues, "$.manifest", "required", "Manifest is required.");
            return;
        }

        if (export.Samples is null)
        {
            Add(issues, "$.samples", "required", "Samples are required.");
            return;
        }

        if (export.Outputs is null)
        {
            Add(issues, "$.outputs", "required", "Outputs are required.");
            return;
        }

        if (export.Digests is null)
        {
            Add(issues, "$.digests", "required", "Section digests are required.");
            return;
        }

        ValidateManifest(export.Manifest, issues);
        ValidateSamples(export, issues);
        ValidateOutputs(export, issues);

        if (requireNormalizedOrder)
        {
            ValidateNormalizedOrder(export, issues);
        }

        ValidateDigestSyntax(export.Digests.ManifestDigest, "$.digests.manifestDigest", issues);
        ValidateDigestSyntax(export.Digests.SamplesDigest, "$.digests.samplesDigest", issues);
        ValidateDigestSyntax(export.Digests.OutputsDigest, "$.digests.outputsDigest", issues);

        if (checkSectionDigests)
        {
            CheckDigest(
                export.Digests.ManifestDigest,
                ComputeDigest(export.Manifest, "$.manifest", issues),
                "$.digests.manifestDigest",
                issues);
            CheckDigest(
                export.Digests.SamplesDigest,
                ComputeDigest(export.Samples, "$.samples", issues),
                "$.digests.samplesDigest",
                issues);
            CheckDigest(
                export.Digests.OutputsDigest,
                ComputeDigest(export.Outputs, "$.outputs", issues),
                "$.digests.outputsDigest",
                issues);
        }
    }

    private static void ValidateManifest(
        RunManifest manifest,
        ICollection<RunExportValidationIssue> issues)
    {
        ValidateGuid(manifest.RunId, "$.manifest.runId", issues);
        Required(manifest.Name, "$.manifest.name", issues);
        ValidateExecution(manifest.Execution, "$.manifest.execution", issues);

        var terminal = manifest.Execution is not null && IsTerminal(manifest.Execution.Status);
        if (terminal && manifest.CompletedAt is null)
        {
            Add(issues, "$.manifest.completedAt", "status-timestamp", "A terminal run requires completedAt.");
        }
        else if (!terminal && manifest.CompletedAt is not null)
        {
            Add(issues, "$.manifest.completedAt", "status-timestamp", "A queued or running run cannot have completedAt.");
        }
        else if (manifest.CompletedAt < manifest.StartedAt)
        {
            Add(issues, "$.manifest.completedAt", "timestamp-order", "completedAt cannot precede startedAt.");
        }

        if (!Enum.IsDefined(manifest.RunnerType))
        {
            Add(issues, "$.manifest.runnerType", "enum", "Unknown runner type.");
        }

        Required(manifest.ScenarioKey, "$.manifest.scenarioKey", issues);
        Required(manifest.OperationKey, "$.manifest.operationKey", issues);

        OperationContract? operation = null;
        if (!string.IsNullOrWhiteSpace(manifest.OperationKey) &&
            !InsightOperationRegistry.TryGet(manifest.OperationKey, out operation))
        {
            Add(issues, "$.manifest.operationKey", "operation", "Unknown operation key.");
        }

        ValidateGraph(manifest.Graph, "$.manifest.graph", issues);
        ValidateDataset(manifest.Dataset, "$.manifest.dataset", issues);
        ValidateAlgorithm(manifest.Algorithm, operation, "$.manifest.algorithm", issues);

        if (manifest.Strategy is null)
        {
            Add(issues, "$.manifest.strategy", "required", "Strategy is required, including explicit null members.");
        }
        else if (operation is not null && manifest.Execution is not null)
        {
            try
            {
                InsightOperationRegistry.ValidateResultStrategySelection(
                    operation.Key,
                    manifest.Strategy,
                    manifest.Execution.Status);
            }
            catch (ArgumentException exception)
            {
                Add(issues, "$.manifest.strategy", "strategy", exception.Message);
            }
        }

        ValidateCanonicalParameters(manifest.CanonicalParameters, "$.manifest.canonicalParameters", issues);
        ValidateTargets(manifest.Targets, "$.manifest.targets", issues);
        ValidateSourceRevision(manifest.SourceRevision, "$.manifest.sourceRevision", issues);
        Required(manifest.BuildConfiguration, "$.manifest.buildConfiguration", issues);
        Required(manifest.BuildMode, "$.manifest.buildMode", issues);
        ValidateDependencies(manifest.Dependencies, "$.manifest.dependencies", issues);
        ValidateHost(manifest.Host, "$.manifest.host", issues);
        Required(manifest.EnvironmentProfile, "$.manifest.environmentProfile", issues);
        ValidateSamplingPolicy(manifest.SamplingPolicy, "$.manifest.samplingPolicy", issues);
        ValidateExecutionPolicy(manifest.ExecutionPolicy, "$.manifest.executionPolicy", issues);
        ValidateUnits(manifest.MeasurementUnits, "$.manifest.measurementUnits", issues);
    }

    private static void ValidateSamples(
        VersionedRunExport export,
        ICollection<RunExportValidationIssue> issues)
    {
        for (var index = 0; index < export.Samples.Count; index++)
        {
            var sample = export.Samples[index];
            var path = $"$.samples[{index}]";
            if (sample is null)
            {
                Add(issues, path, "required", "Sample cannot be null.");
                continue;
            }

            ValidateGuid(sample.RunId, $"{path}.runId", issues);
            ValidateGuid(sample.SampleId, $"{path}.sampleId", issues);
            Equal(sample.RunId, export.Manifest.RunId, $"{path}.runId", "correlation", issues);
            Equal(sample.ScenarioKey, export.Manifest.ScenarioKey, $"{path}.scenarioKey", "identity", issues);
            Equal(sample.OperationKey, export.Manifest.OperationKey, $"{path}.operationKey", "identity", issues);

            Required(sample.Layer, $"{path}.layer", issues);
            Required(sample.Phase, $"{path}.phase", issues);
            if (!InsightPhaseRegistry.IsKnown(sample.Layer, sample.Phase))
            {
                Add(issues, $"{path}.phase", "phase", "Layer and phase must be a registered pair.");
            }

            Nonnegative(sample.WallClockDuration, $"{path}.wallClockDuration", issues);
            Nonnegative(sample.Iteration, $"{path}.iteration", issues);
            ValidateClassification(sample.Classification, $"{path}.classification", issues);
            ValidateNodeCounts(sample.NodeCounts, $"{path}.nodeCounts", issues);
            ValidateEdgeCounts(sample.EdgeCounts, $"{path}.edgeCounts", issues);
            ValidateSearchCounts(sample.SearchCounts, $"{path}.searchCounts", issues);
            Nonnegative(sample.ResultCardinality, $"{path}.resultCardinality", issues);
            ValidateTransport(sample.Transport, $"{path}.transport", issues);
            ValidateResources(sample.Resources, $"{path}.resources", issues);
            ValidateExecution(sample.Execution, $"{path}.execution", issues);
            ValidateUnits(sample.MeasurementUnits, $"{path}.measurementUnits", issues);
            if (sample.MeasurementUnits is not null &&
                export.Manifest.MeasurementUnits is not null &&
                sample.MeasurementUnits != export.Manifest.MeasurementUnits)
            {
                Add(issues, $"{path}.measurementUnits", "identity", "Sample units must match manifest units.");
            }
        }
    }

    private static void ValidateOutputs(
        VersionedRunExport export,
        ICollection<RunExportValidationIssue> issues)
    {
        var sampleIds = export.Samples
            .Where(sample => sample is not null)
            .Select(sample => sample.SampleId)
            .ToHashSet();

        for (var index = 0; index < export.Outputs.Count; index++)
        {
            var output = export.Outputs[index];
            var path = $"$.outputs[{index}]";
            if (output is null)
            {
                Add(issues, path, "required", "Output cannot be null.");
                continue;
            }

            ValidateGuid(output.RunId, $"{path}.runId", issues);
            ValidateGuid(output.SampleId, $"{path}.sampleId", issues);
            Equal(output.RunId, export.Manifest.RunId, $"{path}.runId", "correlation", issues);
            if (!sampleIds.Contains(output.SampleId))
            {
                Add(issues, $"{path}.sampleId", "correlation", "Output sampleId must reference a sample in the export.");
            }

            Equal(output.ScenarioKey, export.Manifest.ScenarioKey, $"{path}.scenarioKey", "identity", issues);
            Equal(output.OperationKey, export.Manifest.OperationKey, $"{path}.operationKey", "identity", issues);
            Equal(
                output.AlgorithmSemanticIdentity,
                export.Manifest.Algorithm?.SemanticIdentity,
                $"{path}.algorithmSemanticIdentity",
                "identity",
                issues);

            ValidateSemanticIdentity(output.AlgorithmSemanticIdentity, $"{path}.algorithmSemanticIdentity", issues);
            ValidateExecution(output.Execution, $"{path}.execution", issues);

            if (output.Strategy is null)
            {
                Add(issues, $"{path}.strategy", "required", "Strategy is required.");
            }
            else
            {
                if (export.Manifest.Strategy is not null && output.Strategy != export.Manifest.Strategy)
                {
                    Add(issues, $"{path}.strategy", "identity", "Output strategy must match manifest strategy.");
                }

                try
                {
                    InsightOperationRegistry.ValidateResultStrategySelection(
                        output.OperationKey,
                        output.Strategy,
                        output.Execution.Status);
                }
                catch (Exception exception) when (exception is ArgumentException or KeyNotFoundException)
                {
                    Add(issues, $"{path}.strategy", "strategy", exception.Message);
                }
            }

            ValidateIdentifiers(output.Identifiers, export.Manifest, $"{path}.identifiers", issues);
            ValidateCanonicalParameters(output.CanonicalParameters, $"{path}.canonicalParameters", issues);
            if (output.CanonicalParameters is not null && export.Manifest.CanonicalParameters is not null)
            {
                Equal(
                    output.CanonicalParameters.Digest,
                    export.Manifest.CanonicalParameters.Digest,
                    $"{path}.canonicalParameters.digest",
                    "identity",
                    issues);
                EqualCanonicalJson(
                    output.CanonicalParameters.Value,
                    export.Manifest.CanonicalParameters.Value,
                    $"{path}.canonicalParameters.value",
                    issues);
            }

            JsonValue(output.Summary, $"{path}.summary", issues);
            JsonValue(output.Distribution, $"{path}.distribution", issues);
            Nonnegative(output.TotalResultCardinality, $"{path}.totalResultCardinality", issues);
            if (output.Items is null)
            {
                Add(issues, $"{path}.items", "required", "Items are required.");
            }
            else
            {
                if (output.Items.Count > OperationResultEnvelope.MaximumRetainedItems)
                {
                    Add(issues, $"{path}.items", "max-items", "At most 100 items may be retained.");
                }

                if (output.Items.Count > output.TotalResultCardinality)
                {
                    Add(issues, $"{path}.items", "cardinality", "Retained items cannot exceed total cardinality.");
                }

                for (var itemIndex = 0; itemIndex < output.Items.Count; itemIndex++)
                {
                    JsonValue(output.Items[itemIndex], $"{path}.items[{itemIndex}]", issues);
                }
            }

            ValidateDigestSyntax(output.ResultDigest, $"{path}.resultDigest", issues);
            if (output.Items is not null && output.TotalResultCardinality == output.Items.Count)
            {
                CheckDigest(
                    output.ResultDigest,
                    ComputeDigest(output.Items, $"{path}.items", issues),
                    $"{path}.resultDigest",
                    issues);
            }

            if (output.FullResultArtifactReference is not null)
            {
                Required(output.FullResultArtifactReference, $"{path}.fullResultArtifactReference", issues);
            }

            ValidateOrderedPaths(output.OrderedPaths, $"{path}.orderedPaths", issues);
        }
    }

    private static void ValidateGraph(
        GraphRunIdentity? graph,
        string path,
        ICollection<RunExportValidationIssue> issues)
    {
        if (graph is null)
        {
            Add(issues, path, "required", "Graph identity is required.");
            return;
        }

        Required(graph.Slug, $"{path}.slug", issues);
        if (graph.GraphId is not null) Required(graph.GraphId, $"{path}.graphId", issues);
        Required(graph.Shape, $"{path}.shape", issues);
        Nonnegative(graph.ActualNodeCount, $"{path}.actualNodeCount", issues);
        Nonnegative(graph.ActualEdgeCount, $"{path}.actualEdgeCount", issues);
        Nonnegative(graph.MaximumDepth, $"{path}.maximumDepth", issues);
    }

    private static void ValidateDataset(
        DatasetRunIdentity? dataset,
        string path,
        ICollection<RunExportValidationIssue> issues)
    {
        if (dataset is null)
        {
            Add(issues, path, "required", "Dataset identity is required.");
            return;
        }

        Required(dataset.GeneratorVersion, $"{path}.generatorVersion", issues);
        Required(dataset.CorpusId, $"{path}.corpusId", issues);
        ValidateDigestSyntax(dataset.CorpusFingerprint, $"{path}.corpusFingerprint", issues);
        ValidateDigestSyntax(dataset.TopologyFingerprint, $"{path}.topologyFingerprint", issues);
        ValidateDigestSyntax(dataset.InputFingerprint, $"{path}.inputFingerprint", issues);
        ValidateDigestSyntax(dataset.DatasetInputFingerprint, $"{path}.datasetInputFingerprint", issues);
    }

    private static void ValidateAlgorithm(
        AlgorithmRunIdentity? algorithm,
        OperationContract? operation,
        string path,
        ICollection<RunExportValidationIssue> issues)
    {
        if (algorithm is null)
        {
            Add(issues, path, "required", "Algorithm identity is required.");
            return;
        }

        Required(algorithm.Key, $"{path}.key", issues);
        ValidateSemanticIdentity(algorithm.SemanticIdentity, $"{path}.semanticIdentity", issues);
        if (operation is not null)
        {
            Equal(algorithm.Key, operation.Key, $"{path}.key", "identity", issues);
            Equal(algorithm.SemanticIdentity, operation.SemanticIdentity, $"{path}.semanticIdentity", "identity", issues);
        }
    }

    private static void ValidateCanonicalParameters(
        CanonicalParameters? parameters,
        string path,
        ICollection<RunExportValidationIssue> issues)
    {
        if (parameters is null)
        {
            Add(issues, path, "required", "Canonical parameters are required.");
            return;
        }

        JsonValue(parameters.Value, $"{path}.value", issues);
        ValidateDigestSyntax(parameters.Digest, $"{path}.digest", issues);
        CheckDigest(
            parameters.Digest,
            ComputeDigest(parameters.Value, $"{path}.value", issues),
            $"{path}.digest",
            issues);
    }

    private static void ValidateTargets(
        RunTargets? targets,
        string path,
        ICollection<RunExportValidationIssue> issues)
    {
        if (targets is null)
        {
            Add(issues, path, "required", "Targets are required.");
            return;
        }

        ValidateStringArray(targets.NodeIds, $"{path}.nodeIds", allowEmpty: false, issues);
        ValidateStringArray(targets.PathIds, $"{path}.pathIds", allowEmpty: false, issues);
    }

    private static void ValidateSourceRevision(
        SourceRevision? source,
        string path,
        ICollection<RunExportValidationIssue> issues)
    {
        if (source is null)
        {
            Add(issues, path, "required", "Source revision is required.");
            return;
        }

        if (string.IsNullOrWhiteSpace(source.GitCommitSha) || !GitShaPattern().IsMatch(source.GitCommitSha))
        {
            Add(issues, $"{path}.gitCommitSha", "format", "Git commit SHA must contain 7 to 64 hexadecimal characters.");
        }
    }

    private static void ValidateDependencies(
        DependencyVersions? dependencies,
        string path,
        ICollection<RunExportValidationIssue> issues)
    {
        if (dependencies is null)
        {
            Add(issues, path, "required", "Dependency versions are required.");
            return;
        }

        Required(dependencies.DotNet, $"{path}.dotNet", issues);
        Required(dependencies.Node, $"{path}.node", issues);
        Required(dependencies.Browser, $"{path}.browser", issues);
        Required(dependencies.GraphMap, $"{path}.graphMap", issues);
        Required(dependencies.PostgreSql, $"{path}.postgreSql", issues);
        if (dependencies.RelevantDependencies is null)
        {
            Add(issues, $"{path}.relevantDependencies", "required", "Relevant dependencies are required.");
        }
        else
        {
            foreach (var entry in dependencies.RelevantDependencies)
            {
                Required(entry.Value, $"{path}.relevantDependencies.{entry.Key}", issues);
            }
        }
    }

    private static void ValidateHost(
        HostEnvironment? host,
        string path,
        ICollection<RunExportValidationIssue> issues)
    {
        if (host is null)
        {
            Add(issues, path, "required", "Host environment is required.");
            return;
        }

        Required(host.OperatingSystem, $"{path}.operatingSystem", issues);
        Required(host.Architecture, $"{path}.architecture", issues);
        Required(host.Cpu, $"{path}.cpu", issues);
        Positive(host.LogicalCoreCount, $"{path}.logicalCoreCount", issues);
        Positive(host.MemoryBytes, $"{path}.memoryBytes", issues);
    }

    private static void ValidateSamplingPolicy(
        WarmupSampleCachePolicy? policy,
        string path,
        ICollection<RunExportValidationIssue> issues)
    {
        if (policy is null)
        {
            Add(issues, path, "required", "Sampling policy is required.");
            return;
        }

        Nonnegative(policy.WarmupIterations, $"{path}.warmupIterations", issues);
        Nonnegative(policy.SampleIterations, $"{path}.sampleIterations", issues);
        Required(policy.WarmupPolicy, $"{path}.warmupPolicy", issues);
        Required(policy.SamplePolicy, $"{path}.samplePolicy", issues);
        Required(policy.JitPolicy, $"{path}.jitPolicy", issues);
        Required(policy.CachePolicy, $"{path}.cachePolicy", issues);
    }

    private static void ValidateExecutionPolicy(
        TimeoutCancellationPolicy? policy,
        string path,
        ICollection<RunExportValidationIssue> issues)
    {
        if (policy is null)
        {
            Add(issues, path, "required", "Execution policy is required.");
            return;
        }

        if (policy.Timeout <= TimeSpan.Zero)
        {
            Add(issues, $"{path}.timeout", "positive", "Timeout must be positive.");
        }

        Required(policy.CancellationPolicy, $"{path}.cancellationPolicy", issues);
    }

    private static void ValidateUnits(
        MeasurementUnitContract? units,
        string path,
        ICollection<RunExportValidationIssue> issues)
    {
        if (units is null)
        {
            Add(issues, path, "required", "Measurement units are required.");
            return;
        }

        Required(units.WallClockDuration, $"{path}.wallClockDuration", issues);
        Required(units.CpuTime, $"{path}.cpuTime", issues);
        Required(units.Memory, $"{path}.memory", issues);
        Required(units.PayloadSize, $"{path}.payloadSize", issues);
        Required(units.Counts, $"{path}.counts", issues);
        Required(units.Density, $"{path}.density", issues);
    }

    private static void ValidateClassification(
        IterationClassification? value,
        string path,
        ICollection<RunExportValidationIssue> issues)
    {
        if (value is null)
        {
            Add(issues, path, "required", "Iteration classification is required.");
            return;
        }

        Required(value.IterationKind, $"{path}.iterationKind", issues);
        Required(value.Temperature, $"{path}.temperature", issues);
        Required(value.JitState, $"{path}.jitState", issues);
        Required(value.CacheState, $"{path}.cacheState", issues);
    }

    private static void ValidateNodeCounts(SampleNodeCounts? value, string path, ICollection<RunExportValidationIssue> issues)
    {
        if (value is null) { Add(issues, path, "required", "Node counts are required."); return; }
        Nonnegative(value.Requested, $"{path}.requested", issues);
        Nonnegative(value.Canonical, $"{path}.canonical", issues);
        Nonnegative(value.Synthetic, $"{path}.synthetic", issues);
        Nonnegative(value.Rendered, $"{path}.rendered", issues);
    }

    private static void ValidateEdgeCounts(SampleEdgeCounts? value, string path, ICollection<RunExportValidationIssue> issues)
    {
        if (value is null) { Add(issues, path, "required", "Edge counts are required."); return; }
        Nonnegative(value.Requested, $"{path}.requested", issues);
        Nonnegative(value.Rendered, $"{path}.rendered", issues);
        Nonnegative(value.Density, $"{path}.density", issues);
    }

    private static void ValidateSearchCounts(SampleSearchCounts? value, string path, ICollection<RunExportValidationIssue> issues)
    {
        if (value is null) { Add(issues, path, "required", "Search counts are required."); return; }
        Nonnegative(value.Matches, $"{path}.matches", issues);
        Nonnegative(value.CompleteRequiredAncestorUnion, $"{path}.completeRequiredAncestorUnion", issues);
    }

    private static void ValidateTransport(SampleTransportMeasurements? value, string path, ICollection<RunExportValidationIssue> issues)
    {
        if (value is null) { Add(issues, path, "required", "Transport measurements are required."); return; }
        Nonnegative(value.RequestBytes, $"{path}.requestBytes", issues);
        Nonnegative(value.ResponseBytes, $"{path}.responseBytes", issues);
        Nonnegative(value.TimeToFirstByte, $"{path}.timeToFirstByte", issues);
        Nonnegative(value.FullTransferDuration, $"{path}.fullTransferDuration", issues);
    }

    private static void ValidateResources(RuntimeResourceMeasurements? value, string path, ICollection<RunExportValidationIssue> issues)
    {
        if (value is null) { Add(issues, path, "required", "Resource measurements are required."); return; }
        Nonnegative(value.AllocatedBytes, $"{path}.allocatedBytes", issues);
        Nonnegative(value.Generation0Collections, $"{path}.generation0Collections", issues);
        Nonnegative(value.Generation1Collections, $"{path}.generation1Collections", issues);
        Nonnegative(value.Generation2Collections, $"{path}.generation2Collections", issues);
        Nonnegative(value.CpuTime, $"{path}.cpuTime", issues);
        Required(value.CpuTimeUnit, $"{path}.cpuTimeUnit", issues);
    }

    private static void ValidateExecution(
        ExecutionOutcome? execution,
        string path,
        ICollection<RunExportValidationIssue> issues)
    {
        if (execution is null)
        {
            Add(issues, path, "required", "Execution outcome is required.");
            return;
        }

        if (!Enum.IsDefined(execution.Status))
        {
            Add(issues, $"{path}.status", "enum", "Unknown execution status.");
            return;
        }

        if (execution.Failure is null)
        {
            if (IsTerminalFailure(execution.Status))
            {
                Add(issues, $"{path}.failure", "status-failure", "Failure details are required for this status.");
            }

            return;
        }

        if (!IsTerminalFailure(execution.Status))
        {
            Add(issues, $"{path}.failure", "status-failure", "This status cannot carry failure details.");
        }

        Required(execution.Failure.Code, $"{path}.failure.code", issues);
        Required(execution.Failure.Message, $"{path}.failure.message", issues);
        if (execution.Failure.ExceptionType is not null)
        {
            Required(execution.Failure.ExceptionType, $"{path}.failure.exceptionType", issues);
        }

        if (!Enum.IsDefined(execution.Failure.Kind))
        {
            Add(issues, $"{path}.failure.kind", "enum", "Unknown failure kind.");
        }

        if (execution.Failure.ValidationFailures is null)
        {
            Add(issues, $"{path}.failure.validationFailures", "required", "Validation failures are required.");
            return;
        }

        if (execution.Failure.Kind == FailureKind.Validation && execution.Failure.ValidationFailures.Count == 0)
        {
            Add(issues, $"{path}.failure.validationFailures", "min-items", "Validation failure requires at least one issue.");
        }
        else if (execution.Failure.Kind != FailureKind.Validation && execution.Failure.ValidationFailures.Count != 0)
        {
            Add(issues, $"{path}.failure.validationFailures", "max-items", "Only validation failures may contain validation issues.");
        }

        for (var index = 0; index < execution.Failure.ValidationFailures.Count; index++)
        {
            var value = execution.Failure.ValidationFailures[index];
            Required(value?.Field, $"{path}.failure.validationFailures[{index}].field", issues);
            Required(value?.Code, $"{path}.failure.validationFailures[{index}].code", issues);
            Required(value?.Message, $"{path}.failure.validationFailures[{index}].message", issues);
        }
    }

    private static void ValidateIdentifiers(
        GraphTargetIdentifiers? identifiers,
        RunManifest manifest,
        string path,
        ICollection<RunExportValidationIssue> issues)
    {
        if (identifiers is null)
        {
            Add(issues, path, "required", "Identifiers are required.");
            return;
        }

        Required(identifiers.GraphSlug, $"{path}.graphSlug", issues);
        Equal(identifiers.GraphSlug, manifest.Graph?.Slug, $"{path}.graphSlug", "identity", issues);
        Equal(identifiers.GraphId, manifest.Graph?.GraphId, $"{path}.graphId", "identity", issues);
        if (identifiers.GraphId is not null) Required(identifiers.GraphId, $"{path}.graphId", issues);
        if (identifiers.TargetNodeId is not null)
        {
            Required(identifiers.TargetNodeId, $"{path}.targetNodeId", issues);
            if (manifest.Targets?.NodeIds is not null &&
                !manifest.Targets.NodeIds.Contains(identifiers.TargetNodeId, StringComparer.Ordinal))
            {
                Add(issues, $"{path}.targetNodeId", "identity", "Output target node must occur in manifest targets.");
            }
        }

        ValidateStringArray(identifiers.TargetPathIds, $"{path}.targetPathIds", allowEmpty: false, issues);
        if (identifiers.TargetPathIds is not null && manifest.Targets?.PathIds is not null &&
            identifiers.TargetPathIds.Any(id => !manifest.Targets.PathIds.Contains(id, StringComparer.Ordinal)))
        {
            Add(issues, $"{path}.targetPathIds", "identity", "Output path targets must occur in manifest targets.");
        }
    }

    private static void ValidateOrderedPaths(
        IReadOnlyList<OrderedPathProjection>? paths,
        string path,
        ICollection<RunExportValidationIssue> issues)
    {
        if (paths is null)
        {
            Add(issues, path, "required", "Ordered paths are required.");
            return;
        }

        for (var index = 0; index < paths.Count; index++)
        {
            var value = paths[index];
            var itemPath = $"{path}[{index}]";
            if (value is null)
            {
                Add(issues, itemPath, "required", "Ordered path cannot be null.");
                continue;
            }

            ValidateStringArray(value.NodeIds, $"{itemPath}.nodeIds", allowEmpty: false, issues);
            ValidateStringArray(value.EdgeIds, $"{itemPath}.edgeIds", allowEmpty: false, issues);
        }
    }

    private static void ValidateNormalizedOrder(
        VersionedRunExport export,
        ICollection<RunExportValidationIssue> issues)
    {
        if (!RunExportOrdering.IsNormalized(export.Samples))
        {
            Add(issues, "$.samples", "order", "Samples are not in the frozen deterministic export order.");
        }

        if (!RunExportOrdering.IsNormalized(export.Outputs))
        {
            Add(issues, "$.outputs", "order", "Outputs are not in the frozen deterministic export order.");
        }
    }

    private static void ValidateStringArray(
        IReadOnlyList<string>? values,
        string path,
        bool allowEmpty,
        ICollection<RunExportValidationIssue> issues)
    {
        if (values is null)
        {
            Add(issues, path, "required", "Array is required.");
            return;
        }

        for (var index = 0; index < values.Count; index++)
        {
            if (!allowEmpty || values[index] is null)
            {
                Required(values[index], $"{path}[{index}]", issues);
            }
        }
    }

    private static void ValidateSemanticIdentity(
        string? value,
        string path,
        ICollection<RunExportValidationIssue> issues)
    {
        if (!SemanticIdentity.TryParse(value, out _))
        {
            Add(issues, path, "format", "Invalid semantic identity.");
        }
    }

    private static void ValidateDigestSyntax(
        string? value,
        string path,
        ICollection<RunExportValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value) || !DigestPattern().IsMatch(value))
        {
            Add(issues, path, "format", "Digest must be 'sha256:' followed by 64 lowercase hexadecimal characters.");
        }
    }

    private static string? ComputeDigest<T>(
        T value,
        string path,
        ICollection<RunExportValidationIssue> issues)
    {
        try
        {
            return CanonicalJson.ComputeSha256(value);
        }
        catch (Exception exception) when (exception is JsonException or FormatException or InvalidOperationException)
        {
            Add(issues, path, "canonical-json", exception.Message);
            return null;
        }
    }

    private static void CheckDigest(
        string? actual,
        string? expected,
        string path,
        ICollection<RunExportValidationIssue> issues)
    {
        if (expected is not null && !string.Equals(actual, expected, StringComparison.Ordinal))
        {
            Add(issues, path, "digest-mismatch", "Digest does not match the canonical logical value.");
        }
    }

    private static void EqualCanonicalJson(
        JsonElement left,
        JsonElement right,
        string path,
        ICollection<RunExportValidationIssue> issues)
    {
        try
        {
            if (!string.Equals(CanonicalJson.Canonicalize(left), CanonicalJson.Canonicalize(right), StringComparison.Ordinal))
            {
                Add(issues, path, "identity", "Canonical parameter values must match the manifest.");
            }
        }
        catch (Exception exception) when (exception is FormatException or InvalidOperationException)
        {
            Add(issues, path, "canonical-json", exception.Message);
        }
    }

    private static void JsonValue(JsonElement value, string path, ICollection<RunExportValidationIssue> issues)
    {
        if (value.ValueKind == JsonValueKind.Undefined)
        {
            Add(issues, path, "required", "A JSON value, including explicit null, is required.");
        }
    }

    private static bool IsTerminal(ExecutionStatus status) =>
        status is ExecutionStatus.Succeeded or ExecutionStatus.Failed or ExecutionStatus.TimedOut or
            ExecutionStatus.Cancelled or ExecutionStatus.Crashed or ExecutionStatus.Skipped;

    private static bool IsTerminalFailure(ExecutionStatus status) =>
        status is ExecutionStatus.Failed or ExecutionStatus.TimedOut or ExecutionStatus.Cancelled or
            ExecutionStatus.Crashed or ExecutionStatus.Skipped;

    private static void ValidateGuid(Guid value, string path, ICollection<RunExportValidationIssue> issues)
    {
        if (value == Guid.Empty) Add(issues, path, "format", "UUID cannot be empty.");
    }

    private static void Required(string? value, string path, ICollection<RunExportValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value)) Add(issues, path, "min-length", "Value must be a non-empty string.");
    }

    private static void Positive(long value, string path, ICollection<RunExportValidationIssue> issues)
    {
        if (value <= 0) Add(issues, path, "positive", "Value must be positive.");
    }

    private static void Nonnegative(decimal value, string path, ICollection<RunExportValidationIssue> issues)
    {
        if (value < 0) Add(issues, path, "minimum", "Value cannot be negative.");
    }

    private static void Nonnegative(long value, string path, ICollection<RunExportValidationIssue> issues)
    {
        if (value < 0) Add(issues, path, "minimum", "Value cannot be negative.");
    }

    private static void Nonnegative(int value, string path, ICollection<RunExportValidationIssue> issues)
    {
        if (value < 0) Add(issues, path, "minimum", "Value cannot be negative.");
    }

    private static void Nonnegative(decimal? value, string path, ICollection<RunExportValidationIssue> issues)
    {
        if (value < 0) Add(issues, path, "minimum", "Value cannot be negative.");
    }

    private static void Nonnegative(long? value, string path, ICollection<RunExportValidationIssue> issues)
    {
        if (value < 0) Add(issues, path, "minimum", "Value cannot be negative.");
    }

    private static void Nonnegative(int? value, string path, ICollection<RunExportValidationIssue> issues)
    {
        if (value < 0) Add(issues, path, "minimum", "Value cannot be negative.");
    }

    private static void Equal<T>(T left, T right, string path, string code, ICollection<RunExportValidationIssue> issues)
    {
        if (!EqualityComparer<T>.Default.Equals(left, right))
        {
            Add(issues, path, code, "Value does not match the run manifest.");
        }
    }

    private static void Add(
        ICollection<RunExportValidationIssue> issues,
        string path,
        string code,
        string message) =>
        issues.Add(new RunExportValidationIssue(path, code, message));

    [GeneratedRegex("^sha256:[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex DigestPattern();

    [GeneratedRegex("^[0-9a-fA-F]{7,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex GitShaPattern();
}

internal static class RunExportOrdering
{
    public static IReadOnlyList<RunSample> Normalize(IEnumerable<RunSample> samples) =>
        Array.AsReadOnly(samples.OrderBy(sample => sample, SampleComparer.Instance).ToArray());

    public static IReadOnlyList<CompactRunOutput> Normalize(IEnumerable<CompactRunOutput> outputs) =>
        Array.AsReadOnly(outputs.OrderBy(output => output, OutputComparer.Instance).ToArray());

    public static bool IsNormalized(IReadOnlyList<RunSample> samples) =>
        IsSameCanonicalSequence(samples, Normalize(samples));

    public static bool IsNormalized(IReadOnlyList<CompactRunOutput> outputs) =>
        IsSameCanonicalSequence(outputs, Normalize(outputs));

    private static bool IsSameCanonicalSequence<T>(IReadOnlyList<T> left, IReadOnlyList<T> right)
    {
        if (left.Count != right.Count) return false;
        for (var index = 0; index < left.Count; index++)
        {
            if (!string.Equals(
                    CanonicalJson.Canonicalize(left[index]),
                    CanonicalJson.Canonicalize(right[index]),
                    StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private sealed class SampleComparer : IComparer<RunSample>
    {
        public static SampleComparer Instance { get; } = new();

        public int Compare(RunSample? left, RunSample? right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left is null) return -1;
            if (right is null) return 1;

            var result = left.Iteration.CompareTo(right.Iteration);
            if (result != 0) return result;
            result = StringComparer.Ordinal.Compare(left.SampleId.ToString("D"), right.SampleId.ToString("D"));
            if (result != 0) return result;
            result = InsightPhaseRegistry.Compare(left.Layer, left.Phase, right.Layer, right.Phase);
            if (result != 0) return result;
            return StringComparer.Ordinal.Compare(
                CanonicalJson.Canonicalize(left),
                CanonicalJson.Canonicalize(right));
        }
    }

    private sealed class OutputComparer : IComparer<CompactRunOutput>
    {
        public static OutputComparer Instance { get; } = new();

        public int Compare(CompactRunOutput? left, CompactRunOutput? right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left is null) return -1;
            if (right is null) return 1;

            var result = StringComparer.Ordinal.Compare(left.SampleId.ToString("D"), right.SampleId.ToString("D"));
            if (result != 0) return result;
            result = OperationOrder(left.OperationKey).CompareTo(OperationOrder(right.OperationKey));
            if (result != 0) return result;
            result = StringComparer.Ordinal.Compare(left.OperationKey, right.OperationKey);
            if (result != 0) return result;
            return StringComparer.Ordinal.Compare(
                CanonicalJson.Canonicalize(left),
                CanonicalJson.Canonicalize(right));
        }

        private static int OperationOrder(string operationKey)
        {
            for (var index = 0; index < InsightOperationRegistry.Operations.Count; index++)
            {
                if (string.Equals(
                        InsightOperationRegistry.Operations[index].Key,
                        operationKey,
                        StringComparison.Ordinal))
                {
                    return index;
                }
            }

            return int.MaxValue;
        }
    }
}
