namespace Backend.Insights.Contracts;

public enum RunCompatibilityField
{
    ScenarioKey,
    OperationKey,
    DatasetInputFingerprint,
    AlgorithmSemanticIdentity,
    ParameterDigest,
    EnvironmentProfile,
    BuildMode,
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
    MeasurementUnitContract MeasurementUnits)
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
            manifest.MeasurementUnits);
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
        string baseline,
        string candidate,
        string message,
        ICollection<RunCompatibilityMismatch> mismatches)
    {
        if (!string.Equals(baseline, candidate, StringComparison.Ordinal))
        {
            mismatches.Add(new RunCompatibilityMismatch(field, baseline, candidate, message));
        }
    }
}
