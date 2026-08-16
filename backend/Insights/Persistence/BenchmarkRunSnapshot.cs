using Backend.Insights.Contracts;

namespace Backend.Insights.Persistence;

public sealed record BenchmarkRunSnapshot
{
    public BenchmarkRunSnapshot(
        RunManifest manifest,
        IReadOnlyList<RunSample> samples,
        IReadOnlyList<CompactRunOutput> outputs)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(outputs);

        var frozenSamples = samples.ToArray();
        var frozenOutputs = outputs.ToArray();
        if (frozenSamples.Any(sample => sample.RunId != manifest.RunId))
        {
            throw new ArgumentException("Every sample must belong to the snapshot run.", nameof(samples));
        }

        if (frozenOutputs.Any(output => output.RunId != manifest.RunId))
        {
            throw new ArgumentException("Every output must belong to the snapshot run.", nameof(outputs));
        }

        Manifest = manifest;
        Samples = Array.AsReadOnly(frozenSamples);
        Outputs = Array.AsReadOnly(frozenOutputs);
    }

    public RunManifest Manifest { get; }

    /// <summary>
    /// Samples in append order. Multiple phase rows may intentionally share a sample ID.
    /// </summary>
    public IReadOnlyList<RunSample> Samples { get; }

    /// <summary>
    /// Compact outputs in append order, including any partial outputs captured before failure.
    /// </summary>
    public IReadOnlyList<CompactRunOutput> Outputs { get; }
}
