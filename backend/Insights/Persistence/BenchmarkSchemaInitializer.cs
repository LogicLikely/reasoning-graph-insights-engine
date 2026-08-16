using Backend.Data;
using Dapper;

namespace Backend.Insights.Persistence;

public sealed class BenchmarkSchemaInitializer : IBenchmarkSchemaInitializer
{
    public static readonly string[] SchemaPathSegments = ["Data", "Sql", "benchmark_schema.sql"];

    private const int InitializationCommandTimeoutSeconds = 30;

    private readonly DbConnectionFactory _dbConnectionFactory;
    private readonly IHostEnvironment _hostEnvironment;

    public BenchmarkSchemaInitializer(
        DbConnectionFactory dbConnectionFactory,
        IHostEnvironment hostEnvironment)
    {
        _dbConnectionFactory = dbConnectionFactory;
        _hostEnvironment = hostEnvironment;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var schemaPath = Path.Combine(
            [_hostEnvironment.ContentRootPath, .. SchemaPathSegments]);
        if (!File.Exists(schemaPath))
        {
            throw new FileNotFoundException(
                "Benchmark schema SQL file was not found.",
                schemaPath);
        }

        var schemaSql = await File.ReadAllTextAsync(schemaPath, cancellationToken);
        if (string.IsNullOrWhiteSpace(schemaSql))
        {
            throw new InvalidDataException("Benchmark schema SQL file is empty.");
        }

        using var connection = _dbConnectionFactory.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                schemaSql,
                transaction: transaction,
                commandTimeout: InitializationCommandTimeoutSeconds,
                cancellationToken: cancellationToken));
            cancellationToken.ThrowIfCancellationRequested();
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }
}
