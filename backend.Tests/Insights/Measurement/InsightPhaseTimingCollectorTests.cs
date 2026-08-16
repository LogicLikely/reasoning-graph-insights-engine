using Backend.Insights.Contracts;
using Backend.Insights.Measurement;

namespace backend.Tests.Insights.Measurement;

[TestClass]
public sealed class InsightPhaseTimingCollectorTests
{
    [TestMethod]
    public void Measure_UsesMonotonicClockAndRecordsOnceOnDispose()
    {
        var clock = new FakeMonotonicClock(frequency: 1_000, 10, 35);
        var collector = new InsightPhaseTimingCollector(clock);

        var scope = collector.Measure(
            InsightMeasurementLayers.PostgreSqlRepository,
            InsightMeasurementPhases.GraphLookup);
        Assert.AreEqual(0, collector.Snapshot().Count);

        scope.Dispose();
        scope.Dispose();

        var timing = collector.Snapshot().Single();
        Assert.AreEqual(0, timing.Sequence);
        Assert.AreEqual(25m, timing.Duration);
        Assert.AreEqual("ms", timing.Unit);
        Assert.AreEqual(
            TimingBoundaryProvenance.DirectlyInstrumented,
            timing.TimingBoundaryProvenance);
    }

    [TestMethod]
    public void Snapshot_UsesScopeStartSequenceEvenWhenNestedScopesCompleteInReverse()
    {
        var clock = new FakeMonotonicClock(frequency: 1_000, 0, 5, 15, 30);
        var collector = new InsightPhaseTimingCollector(clock);

        var outer = collector.Measure(
            InsightMeasurementLayers.BackendServiceApi,
            InsightMeasurementPhases.DtoMapping);
        var inner = collector.Measure(
            InsightMeasurementLayers.BackendServiceApi,
            InsightMeasurementPhases.Ranking);
        inner.Dispose();
        outer.Dispose();

        var snapshot = collector.Snapshot();
        CollectionAssert.AreEqual(new long[] { 0, 1 }, snapshot.Select(value => value.Sequence).ToArray());
        CollectionAssert.AreEqual(
            new[] { InsightMeasurementPhases.DtoMapping, InsightMeasurementPhases.Ranking },
            snapshot.Select(value => value.Phase).ToArray());
        CollectionAssert.AreEqual(new decimal[] { 30, 10 }, snapshot.Select(value => value.Duration).ToArray());
    }

    [TestMethod]
    public void Record_IsDeterministicAndRejectsUnknownOrNegativeMeasurements()
    {
        var collector = new InsightPhaseTimingCollector(new FakeMonotonicClock(1_000));

        var first = collector.Record(
            InsightMeasurementLayers.Transport,
            InsightMeasurementPhases.ResponseBytes,
            4.125m,
            TimingBoundaryProvenance.Estimated);

        Assert.AreEqual(0, first.Sequence);
        Assert.AreEqual(4.125m, collector.Snapshot().Single().Duration);
        Assert.AreEqual(TimingBoundaryProvenance.Estimated, first.TimingBoundaryProvenance);
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => collector.Record(
            InsightMeasurementLayers.Transport,
            InsightMeasurementPhases.ResponseBytes,
            -1m,
            TimingBoundaryProvenance.Estimated));
        Assert.ThrowsException<ArgumentException>(() => collector.Record(
            "unknown",
            "unknown",
            1m,
            TimingBoundaryProvenance.Estimated));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => collector.Record(
            InsightMeasurementLayers.Transport,
            InsightMeasurementPhases.ResponseBytes,
            1m,
            (TimingBoundaryProvenance)999));
    }

    private sealed class FakeMonotonicClock : IMonotonicClock
    {
        private readonly Queue<long> _timestamps;

        public FakeMonotonicClock(long frequency, params long[] timestamps)
        {
            Frequency = frequency;
            _timestamps = new Queue<long>(timestamps);
        }

        public long Frequency { get; }

        public long GetTimestamp() => _timestamps.Dequeue();
    }
}
