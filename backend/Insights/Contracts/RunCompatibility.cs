namespace Backend.Insights.Contracts;

public enum RunCompatibilityField
{
    ScenarioKey,
    ProfileKey,
    OperationKey,
    DatasetInputFingerprint,
    AlgorithmSemanticIdentity,
    ParameterDigest,
    ActualStrategy,
    EnvironmentProfile,
    BuildMode,
    SampleMode,
    MeasurementUnits
}

public sealed record RunComparisonIdentity(
    string ScenarioKey,
    string OperationKey,
    string DatasetInputFingerprint,
    string AlgorithmSemanticIdentity,
    string ParameterDigest,
    string EnvironmentProfile,
    string BuildMode,
    MeasurementUnitContract MeasurementUnits,
    string ProfileKey = RunProfileKeys.LegacyUnspecified,
    string? ActualStrategy = null,
    string SampleMode = RunSampleModeTokens.LegacyUnspecified)
{
    public static RunComparisonIdentity FromManifest(RunManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return new RunComparisonIdentity(
            manifest.ScenarioKey,
            manifest.OperationKey,
            manifest.Dataset.DatasetInputFingerprint,
            manifest.Algorithm.SemanticIdentity,
            manifest.CanonicalParameters.Digest,
            manifest.EnvironmentProfile,
            manifest.BuildMode,
            manifest.MeasurementUnits,
            manifest.ProfileKey,
            manifest.Strategy.Used,
            manifest.SamplingPolicy.SampleMode);
    }
}

public sealed record RunCompatibilityMismatch(
    RunCompatibilityField Field,
    string BaselineValue,
    string CandidateValue,
    string Message);

public sealed record RunCompatibilityResult(IReadOnlyList<RunCompatibilityMismatch> Mismatches)
{
    public bool IsCompatible => Mismatches.Count == 0;
}

public static class RunCompatibilityEvaluator
{
    public static RunCompatibilityResult Evaluate(
        RunComparisonIdentity baseline,
        RunComparisonIdentity candidate)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(candidate);

        var mismatches = new List<RunCompatibilityMismatch>();
        Compare(
            RunCompatibilityField.ScenarioKey,
            baseline.ScenarioKey,
            candidate.ScenarioKey,
            "Scenario keys differ.",
            mismatches);
        Compare(
            RunCompatibilityField.ProfileKey,
            baseline.ProfileKey,
            candidate.ProfileKey,
            "Benchmark profile keys differ.",
            mismatches);
        Compare(
            RunCompatibilityField.OperationKey,
            baseline.OperationKey,
            candidate.OperationKey,
            "Operation keys differ.",
            mismatches);
        Compare(
            RunCompatibilityField.DatasetInputFingerprint,
            baseline.DatasetInputFingerprint,
            candidate.DatasetInputFingerprint,
            "Dataset/input fingerprints differ.",
            mismatches);
        Compare(
            RunCompatibilityField.AlgorithmSemanticIdentity,
            baseline.AlgorithmSemanticIdentity,
            candidate.AlgorithmSemanticIdentity,
            "Algorithm semantic identities differ.",
            mismatches);
        Compare(
            RunCompatibilityField.ParameterDigest,
            baseline.ParameterDigest,
            candidate.ParameterDigest,
            "Canonical parameter digests differ.",
            mismatches);
        Compare(
            RunCompatibilityField.ActualStrategy,
            baseline.ActualStrategy,
            candidate.ActualStrategy,
            "Actual behavior-changing strategies differ.",
            mismatches);
        Compare(
            RunCompatibilityField.EnvironmentProfile,
            baseline.EnvironmentProfile,
            candidate.EnvironmentProfile,
            "Environment profiles differ.",
            mismatches);
        Compare(
            RunCompatibilityField.BuildMode,
            baseline.BuildMode,
            candidate.BuildMode,
            "Build modes differ.",
            mismatches);
        Compare(
            RunCompatibilityField.SampleMode,
            baseline.SampleMode,
            candidate.SampleMode,
            "Run-level sample modes differ.",
            mismatches);

        if (baseline.MeasurementUnits != candidate.MeasurementUnits)
        {
            mismatches.Add(new RunCompatibilityMismatch(
                RunCompatibilityField.MeasurementUnits,
                CanonicalJson.Canonicalize(baseline.MeasurementUnits),
                CanonicalJson.Canonicalize(candidate.MeasurementUnits),
                "Measurement unit contracts differ."));
        }

        return new RunCompatibilityResult(mismatches.AsReadOnly());
    }

    private static void Compare(
        RunCompatibilityField field,
        string? baseline,
        string? candidate,
        string message,
        ICollection<RunCompatibilityMismatch> mismatches)
    {
        if (!string.Equals(baseline, candidate, StringComparison.Ordinal))
        {
            mismatches.Add(new RunCompatibilityMismatch(
                field,
                baseline ?? "<none>",
                candidate ?? "<none>",
                message));
        }
    }
}
