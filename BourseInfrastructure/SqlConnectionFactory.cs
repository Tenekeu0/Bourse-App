using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Data.Common;

namespace BourseInfrastructure;

public sealed class SqlConnectionFactory
{
    private readonly string _connectionString;

    public SqlConnectionFactory(IConfiguration configuration)
    {
        var configuredConnectionString = configuration.GetConnectionString("BourseDb")
            ?? configuration["ConnectionStrings:BourseDb"]
            ?? configuration["BourseDb"]
            ?? throw new InvalidOperationException(
                "Missing SQL connection string 'BourseDb'. Configure ConnectionStrings:BourseDb or BourseDb.");

        var wrapper = new DbConnectionStringBuilder
        {
            ConnectionString = configuredConnectionString
        };
        if (wrapper.TryGetValue("ConnectionString", out var nestedConnectionString))
        {
            configuredConnectionString = nestedConnectionString as string
                ?? throw new InvalidOperationException("BourseDb must contain a SQL Server connection string.");
        }

        try
        {
            _connectionString = new SqlConnectionStringBuilder(configuredConnectionString).ConnectionString;
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException("BourseDb must contain a valid SQL Server connection string.", exception);
        }
    }

    public SqlConnection Create()
    {
        return new SqlConnection(_connectionString);
    }
}
