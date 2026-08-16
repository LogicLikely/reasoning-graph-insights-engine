namespace Backend.Insights.Persistence;

public interface IBenchmarkSchemaInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
