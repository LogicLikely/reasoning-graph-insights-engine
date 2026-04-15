using Backend.Configuration;
using Microsoft.Extensions.Options;

namespace Backend.Data;

public class DbConnectionFactory
{
    private readonly DatabaseOptions _databaseOptions;

    public DbConnectionFactory(IOptions<DatabaseOptions> databaseOptions)
    {
        _databaseOptions = databaseOptions.Value;
    }

    public string? ConnectionString => _databaseOptions.ConnectionString;
}
