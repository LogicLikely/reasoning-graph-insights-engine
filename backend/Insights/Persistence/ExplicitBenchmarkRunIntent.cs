namespace Backend.Insights.Persistence;

/// <summary>
/// Required capability for benchmark-store mutations. Creating the capability is
/// a deliberate declaration that the caller is executing an explicit Lab or CLI
/// run rather than observing an ambient graph request.
/// </summary>
public sealed class ExplicitBenchmarkRunIntent
{
    private ExplicitBenchmarkRunIntent(Guid runId)
    {
        RunId = runId;
    }

    public Guid RunId { get; }

    public static ExplicitBenchmarkRunIntent ForRun(Guid runId)
    {
        if (runId == Guid.Empty)
        {
            throw new ArgumentException("An explicit benchmark run ID cannot be empty.", nameof(runId));
        }

        return new ExplicitBenchmarkRunIntent(runId);
    }
}
