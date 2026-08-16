using Backend.Insights.Contracts;
using backend.Tests.Insights.Persistence;

namespace backend.Tests.Insights.Contracts;

[TestClass]
public class RunCompatibilityEvaluatorTests
{
    [TestMethod]
    public void Evaluate_ReturnsCompatibleWhenEveryFrozenIdentityFieldMatches()
    {
        var identity = Baseline();

        var result = RunCompatibilityEvaluator.Evaluate(identity, identity with { });

        Assert.IsTrue(result.IsCompatible);
        Assert.AreEqual(0, result.Mismatches.Count);
    }

    [TestMethod]
    public void FromManifest_IncludesProfileActualStrategyAndRunLevelSampleMode()
    {
        var manifest = BenchmarkPersistenceTestData.Manifest() with
        {
            Strategy = new StrategySelection(null, null)
        };

        var identity = RunComparisonIdentity.FromManifest(manifest);

        Assert.AreEqual("quick", identity.ProfileKey);
        Assert.IsNull(identity.ActualStrategy);
        Assert.AreEqual(RunSampleModeTokens.Warm, identity.SampleMode);
    }

    [TestMethod]
    public void Evaluate_ReturnsExactlyOneReasonForEachIndependentlyChangedField()
    {
        var baseline = Baseline();
        var changes = new (RunCompatibilityField Field, RunComparisonIdentity Candidate)[]
        {
            (RunCompatibilityField.ScenarioKey, baseline with { ScenarioKey = "wide-1k" }),
            (RunCompatibilityField.ProfileKey, baseline with { ProfileKey = "standard" }),
            (RunCompatibilityField.OperationKey, baseline with { OperationKey = OperationKeys.GraphFetch }),
            (RunCompatibilityField.DatasetInputFingerprint, baseline with { DatasetInputFingerprint = "sha256:other-input" }),
            (RunCompatibilityField.AlgorithmSemanticIdentity, baseline with { AlgorithmSemanticIdentity = "robustness-v1" }),
            (RunCompatibilityField.ParameterDigest, baseline with { ParameterDigest = "sha256:other-parameters" }),
            (RunCompatibilityField.ActualStrategy, baseline with { ActualStrategy = OperationStrategyNames.Greedy }),
            (RunCompatibilityField.EnvironmentProfile, baseline with { EnvironmentProfile = "different-host" }),
            (RunCompatibilityField.BuildMode, baseline with { BuildMode = "Debug" }),
            (RunCompatibilityField.SampleMode, baseline with { SampleMode = RunSampleModeTokens.Cold }),
            (RunCompatibilityField.MeasurementUnits, baseline with
            {
                MeasurementUnits = baseline.MeasurementUnits with { WallClockDuration = "us" }
            })
        };

        foreach (var (field, candidate) in changes)
        {
            var result = RunCompatibilityEvaluator.Evaluate(baseline, candidate);

            Assert.IsFalse(result.IsCompatible, field.ToString());
            Assert.AreEqual(1, result.Mismatches.Count, field.ToString());
            Assert.AreEqual(field, result.Mismatches[0].Field);
            Assert.AreNotEqual(result.Mismatches[0].BaselineValue, result.Mismatches[0].CandidateValue);
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.Mismatches[0].Message));
        }
    }

    [TestMethod]
    public void Evaluate_ReturnsEveryMismatchInFrozenComparisonOrder()
    {
        var baseline = Baseline();
        var candidate = new RunComparisonIdentity(
            "different-scenario",
            "different.operation",
            "sha256:different-input",
            "different-algorithm-v1",
            "sha256:different-parameters",
            "different-environment",
            "Debug",
            new MeasurementUnitContract("us", "us", "kb", "kb", "items", "percent"),
            "different-profile",
            OperationStrategyNames.Greedy,
            RunSampleModeTokens.Cold);

        var result = RunCompatibilityEvaluator.Evaluate(baseline, candidate);

        Assert.IsFalse(result.IsCompatible);
        CollectionAssert.AreEqual(
            Enum.GetValues<RunCompatibilityField>(),
            result.Mismatches.Select(mismatch => mismatch.Field).ToArray());
    }

    private static RunComparisonIdentity Baseline()
    {
        return new RunComparisonIdentity(
            "balanced-1k",
            OperationKeys.NodeRobustness,
            "sha256:dataset-input",
            AlgorithmSemanticIdentities.RobustnessV0,
            "sha256:parameters",
            "ll-arm64-mac-primary",
            "Release",
            new MeasurementUnitContract("ms", "ms", "bytes", "bytes", "count", "ratio"),
            "quick",
            OperationStrategyNames.Exact,
            RunSampleModeTokens.Warm);
    }
}
