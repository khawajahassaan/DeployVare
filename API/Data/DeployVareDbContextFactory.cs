using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace API.Data;

/// <summary>
/// Builds a DbContext for the EF tooling ("dotnet ef migrations add", "dotnet ef database update") without
/// starting the application.
///
/// Without this, the tooling runs Program.cs, which creates the uploads and backups folders under the content
/// root at startup. Under the tooling the content root is the SDK's own directory, so it tries to create
/// folders inside the dotnet installation and fails before the services are registered.
///
/// Fixing that in Program.cs instead would be wrong: a real server that cannot create its uploads folder should
/// fail loudly rather than start without one. This type is used only by the tooling and changes nothing at
/// runtime.
/// </summary>
public class DeployVareDbContextFactory : IDesignTimeDbContextFactory<DeployVareDbContext>
{
    public DeployVareDbContext CreateDbContext(string[] args)
    {
        // Same resolution order as Program.cs, so migrations are generated against the real schema
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING")
            ?? configuration.GetConnectionString("DeployVareDb")
            // The tooling only needs a provider and a schema to compare against; it never opens this connection
            // when generating a migration, so a placeholder keeps "migrations add" working on a machine with no
            // database configured.
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=DeployVare;Trusted_Connection=True;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<DeployVareDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new DeployVareDbContext(options);
    }
}
