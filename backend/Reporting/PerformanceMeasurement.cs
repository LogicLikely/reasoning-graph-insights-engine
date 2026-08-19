using System.Diagnostics;

namespace Backend.Reporting;

public sealed class PerformanceMeasurement : IDisposable
{
    private readonly Stopwatch _stopwatch;
    private readonly TimeSpan _startingCpuTime;
    private readonly long _startingAllocatedBytes;
    private readonly int _startingGen0Collections;
    private readonly int _startingGen1Collections;
    private readonly int _startingGen2Collections;
    private readonly int _startingManagedThreadId;
    private PerformanceMeasurementResult? _result;

    private PerformanceMeasurement()
    {
        StartedAtUtc = DateTimeOffset.UtcNow;
        _startingCpuTime = ReadProcessCpuTime();
        _startingAllocatedBytes = GC.GetAllocatedBytesForCurrentThread();
        _startingGen0Collections = GC.CollectionCount(0);
        _startingGen1Collections = GC.CollectionCount(1);
        _startingGen2Collections = GC.CollectionCount(2);
        _startingManagedThreadId = Environment.CurrentManagedThreadId;
        _stopwatch = Stopwatch.StartNew();
    }

    public DateTimeOffset StartedAtUtc { get; }

    public static PerformanceMeasurement Start() => new();

    public PerformanceMeasurementResult Stop()
    {
        if (_result is not null)
        {
            return _result;
        }

        _stopwatch.Stop();

        var remainedOnStartingThread =
            Environment.CurrentManagedThreadId == _startingManagedThreadId;
        long? allocatedBytes = remainedOnStartingThread
            ? Math.Max(0, GC.GetAllocatedBytesForCurrentThread() - _startingAllocatedBytes)
            : null;
        var gen0Collections = Math.Max(
            0,
            GC.CollectionCount(0) - _startingGen0Collections);
        var gen1Collections = Math.Max(
            0,
            GC.CollectionCount(1) - _startingGen1Collections);
        var gen2Collections = Math.Max(
            0,
            GC.CollectionCount(2) - _startingGen2Collections);
        var cpuTime = ReadProcessCpuTime() - _startingCpuTime;

        _result = new PerformanceMeasurementResult
        {
            StartedAtUtc = StartedAtUtc,
            ElapsedMilliseconds = _stopwatch.Elapsed.TotalMilliseconds,
            Resources = new PerformanceResourceInfo
            {
                CpuTimeMilliseconds = Math.Max(0, cpuTime.TotalMilliseconds),
                AllocatedBytes = allocatedBytes,
                Gen0Collections = gen0Collections,
                Gen1Collections = gen1Collections,
                Gen2Collections = gen2Collections,
                AllocationMeasurement = remainedOnStartingThread
                    ? "currentThreadAllocatedBytesDelta"
                    : "unavailableThreadChanged"
            }
        };

        return _result;
    }

    public void Dispose()
    {
        Stop();
    }

    private static TimeSpan ReadProcessCpuTime()
    {
        using var process = Process.GetCurrentProcess();
        return process.TotalProcessorTime;
    }
}

public sealed record PerformanceMeasurementResult
{
    public required DateTimeOffset StartedAtUtc { get; init; }

    public required double ElapsedMilliseconds { get; init; }

    public required PerformanceResourceInfo Resources { get; init; }
}
