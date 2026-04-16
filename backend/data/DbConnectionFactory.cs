using Backend.Configuration;
using System.Data;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Backend.Data;

public class DbConnectionFactory
{
    private readonly DatabaseOptions _databaseOptions;

    public DbConnectionFactory(IOptions<DatabaseOptions> databaseOptions)
    {
        _databaseOptions = databaseOptions.Value;
    }

    public IDbConnection CreateConnection()
    {
        if (string.IsNullOrWhiteSpace(_databaseOptions.ConnectionString))
        {
            throw new InvalidOperationException(
                "Database:ConnectionString is not configured.");
        }

        return new NpgsqlConnection(_databaseOptions.ConnectionString);
    }
}
