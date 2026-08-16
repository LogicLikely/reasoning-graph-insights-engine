using System.Diagnostics;
using Backend.Insights.Contracts;

namespace Backend.Insights.Measurement;

public sealed record InsightPhaseTimingRecord(
    long Sequence,
    string Layer,
    string Phase,
    decimal Duration,
    string Unit,
    TimingBoundaryProvenance TimingBoundaryProvenance);

public interface IMonotonicClock
{
    long Frequency { get; }

    long GetTimestamp();
}

public sealed class StopwatchMonotonicClock : IMonotonicClock
{
    public static StopwatchMonotonicClock Instance { get; } = new();

    private StopwatchMonotonicClock()
    {
    }

    public long Frequency => Stopwatch.Frequency;

    public long GetTimestamp() => Stopwatch.GetTimestamp();
}

public interface IInsightPhaseTimingCollector
{
    IDisposable Measure(string layer, string phase);

    InsightPhaseTimingRecord Record(
        string layer,
        string phase,
        decimal durationMilliseconds,
        TimingBoundaryProvenance timingBoundaryProvenance);

    IReadOnlyList<InsightPhaseTimingRecord> Snapshot();
}

/// <summary>
/// Register as scoped. Sequence is assigned when a scope starts, so nested scopes
/// retain deterministic invocation order even when they complete in reverse.
/// </summary>
public sealed class InsightPhaseTimingCollector : IInsightPhaseTimingCollector
{
    public const string DurationUnit = "ms";

    private readonly object _gate = new();
    private readonly IMonotonicClock _clock;
    private readonly List<InsightPhaseTimingRecord> _records = [];
    private long _nextSequence;

    public InsightPhaseTimingCollector()
        : this(StopwatchMonotonicClock.Instance)
    {
    }

    public InsightPhaseTimingCollector(IMonotonicClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (clock.Frequency <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(clock), "Clock frequency must be positive.");
        }

        _clock = clock;
    }

    public IDisposable Measure(string layer, string phase)
    {
        ValidatePhase(layer, phase);
        var sequence = Interlocked.Increment(ref _nextSequence) - 1;
        return new TimingScope(this, sequence, layer, phase, _clock.GetTimestamp());
    }

    public InsightPhaseTimingRecord Record(
        string layer,
        string phase,
        decimal durationMilliseconds,
        TimingBoundaryProvenance timingBoundaryProvenance)
    {
        ValidatePhase(layer, phase);
        ArgumentOutOfRangeException.ThrowIfNegative(durationMilliseconds);
        ValidateProvenance(timingBoundaryProvenance);
        var sequence = Interlocked.Increment(ref _nextSequence) - 1;
        return Add(sequence, layer, phase, durationMilliseconds, timingBoundaryProvenance);
    }

    public IReadOnlyList<InsightPhaseTimingRecord> Snapshot()
    {
        lock (_gate)
        {
            return Array.AsReadOnly(_records
                .OrderBy(record => record.Sequence)
                .ToArray());
        }
    }

    private InsightPhaseTimingRecord Complete(
        long sequence,
        string layer,
        string phase,
        long startedAt)
    {
        var completedAt = _clock.GetTimestamp();
        if (completedAt < startedAt)
        {
            throw new InvalidOperationException("The monotonic clock moved backwards.");
        }

        var duration = (completedAt - startedAt) * 1000m / _clock.Frequency;
        return Add(
            sequence,
            layer,
            phase,
            duration,
            TimingBoundaryProvenance.DirectlyInstrumented);
    }

    private InsightPhaseTimingRecord Add(
        long sequence,
        string layer,
        string phase,
        decimal duration,
        TimingBoundaryProvenance timingBoundaryProvenance)
    {
        var record = new InsightPhaseTimingRecord(
            sequence,
            layer,
            phase,
            duration,
            DurationUnit,
            timingBoundaryProvenance);
        lock (_gate)
        {
            _records.Add(record);
        }

        return record;
    }

    private static void ValidatePhase(string layer, string phase)
    {
        if (!InsightPhaseRegistry.IsKnown(layer, phase))
        {
            throw new ArgumentException(
                $"Unknown Insights measurement phase '{layer}/{phase}'.",
                nameof(phase));
        }
    }

    private static void ValidateProvenance(TimingBoundaryProvenance timingBoundaryProvenance)
    {
        if (!Enum.IsDefined(timingBoundaryProvenance))
        {
            throw new ArgumentOutOfRangeException(
                nameof(timingBoundaryProvenance),
                timingBoundaryProvenance,
                "Unknown timing-boundary provenance.");
        }
    }

    private sealed class TimingScope : IDisposable
    {
        private readonly InsightPhaseTimingCollector _owner;
        private readonly long _sequence;
        private readonly string _layer;
        private readonly string _phase;
        private readonly long _startedAt;
        private int _disposed;

        public TimingScope(
            InsightPhaseTimingCollector owner,
            long sequence,
            string layer,
            string phase,
            long startedAt)
        {
            _owner = owner;
            _sequence = sequence;
            _layer = layer;
            _phase = phase;
            _startedAt = startedAt;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _owner.Complete(_sequence, _layer, _phase, _startedAt);
            }
        }
    }
}
