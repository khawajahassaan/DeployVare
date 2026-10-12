using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace API.Data;

/// <summary>
/// The same model on PostgreSQL (e.g. Supabase). A separate type so it gets its own migrations
/// (Migrations/Postgres): EF matches migrations to the exact context type, so the SQL Server migrations
/// keep belonging to <see cref="DeployVareDbContext"/> and the two sets never mix.
/// Selected at startup with DB_PROVIDER=postgres; the application keeps injecting DeployVareDbContext.
/// </summary>
public class PostgresDeployVareDbContext : DeployVareDbContext
{
    public PostgresDeployVareDbContext(DbContextOptions<PostgresDeployVareDbContext> options)
        : base(options)
    {
    }
}

/// <summary>Design-time factory for "dotnet ef migrations add ... --context PostgresDeployVareDbContext".</summary>
public class PostgresDeployVareDbContextFactory : IDesignTimeDbContextFactory<PostgresDeployVareDbContext>
{
    public PostgresDeployVareDbContext CreateDbContext(string[] args)
    {
        PostgresSetup.ConfigureNpgsql();
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        // Generating a migration never opens this connection, so a placeholder works without a server
        var connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING")
            ?? configuration.GetConnectionString("DeployVarePostgres")
            ?? "Host=localhost;Database=deployvare;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<PostgresDeployVareDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new PostgresDeployVareDbContext(options);
    }
}

public static class PostgresSetup
{
    /// <summary>
    /// DeployVare stores DateTime values as plain UTC timestamps (the SQL Server datetime2 behaviour) and
    /// sets them from both DateTime.UtcNow and DateTime.Now. Npgsql 6+ rejects non-UTC values for
    /// "timestamp with time zone"; the legacy mapping keeps "timestamp without time zone" and accepts both,
    /// so the code and the copied data behave exactly as on SQL Server.
    /// </summary>
    public static void ConfigureNpgsql() =>
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
}
