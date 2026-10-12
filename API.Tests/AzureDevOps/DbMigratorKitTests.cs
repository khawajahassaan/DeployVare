using API.Utilities;

namespace API.Tests.AzureDevOps;

public class DbMigratorKitTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "DeployVareTests", Guid.NewGuid().ToString("N"));

    // A published API folder: DbMigrator ships inside it (project reference) next to every API assembly
    private const string DepsJson = """
    {
      "runtimeTarget": { "name": ".NETCoreApp,Version=v10.0" },
      "targets": {
        ".NETCoreApp,Version=v10.0": {
          "DbMigrator/1.0.0": {
            "dependencies": { "dbup-sqlserver": "7.2.0" },
            "runtime": { "DbMigrator.dll": {} }
          },
          "dbup-sqlserver/7.2.0": {
            "dependencies": { "Microsoft.Data.SqlClient": "6.0.2" },
            "runtime": { "lib/net8.0/dbup-sqlserver.dll": {} }
          },
          "Microsoft.Data.SqlClient/6.0.2": {
            "runtime": { "lib/net9.0/Microsoft.Data.SqlClient.dll": {} },
            "runtimeTargets": {
              "runtimes/win/lib/net9.0/Microsoft.Data.SqlClient.dll": { "rid": "win", "assetType": "runtime" }
            }
          },
          "Newtonsoft.Json/13.0.3": {
            "runtime": { "lib/net6.0/Newtonsoft.Json.dll": {} }
          }
        }
      },
      "libraries": {}
    }
    """;

    private string PublishedApi(bool withDeps = true, bool dropSqlClient = false)
    {
        var app = Path.Combine(_root, "app");
        Directory.CreateDirectory(Path.Combine(app, "runtimes", "win", "lib", "net9.0"));
        var files = new List<string>
        {
            "API.dll", "appsettings.json", "Newtonsoft.Json.dll",
            "DbMigrator.exe", "DbMigrator.dll", "DbMigrator.runtimeconfig.json",
            "dbup-sqlserver.dll", Path.Combine("runtimes", "win", "lib", "net9.0", "Microsoft.Data.SqlClient.dll")
        };
        if (!dropSqlClient) files.Add("Microsoft.Data.SqlClient.dll");
        foreach (var file in files) File.WriteAllText(Path.Combine(app, file), "x");
        if (withDeps) File.WriteAllText(Path.Combine(app, "DbMigrator.deps.json"), DepsJson);
        return app;
    }

    private static List<string> Listing(string directory) =>
        Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(directory, f).Replace('\\', '/'))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

    [Fact]
    public void Copy_TakesOnlyDbMigratorAndItsDependencies()
    {
        var destination = Path.Combine(_root, "kit");

        var result = DbMigratorKit.Copy(PublishedApi(), destination);

        Assert.True(result.Found);
        Assert.True(result.Trimmed);
        Assert.Equal(new[]
        {
            "DbMigrator.deps.json", "DbMigrator.dll", "DbMigrator.exe", "DbMigrator.runtimeconfig.json",
            "Microsoft.Data.SqlClient.dll", "dbup-sqlserver.dll",
            "runtimes/win/lib/net9.0/Microsoft.Data.SqlClient.dll"
        }, Listing(destination));
    }

    [Fact]
    public void Copy_WithoutADependencyList_FallsBackToTheWholeFolder()
    {
        var destination = Path.Combine(_root, "kit");

        var result = DbMigratorKit.Copy(PublishedApi(withDeps: false), destination);

        Assert.True(result.Found);
        Assert.False(result.Trimmed);
        Assert.Contains("API.dll", Listing(destination));
        Assert.Contains("DbMigrator.exe", Listing(destination));
    }

    [Fact]
    public void Copy_WhenAListedAssemblyIsMissing_DoesNotShipABrokenTrimmedTool()
    {
        var destination = Path.Combine(_root, "kit");

        var result = DbMigratorKit.Copy(PublishedApi(dropSqlClient: true), destination);

        Assert.False(result.Trimmed);
        Assert.Contains("appsettings.json", Listing(destination));
    }

    [Fact]
    public void Copy_WithoutDbMigrator_ReportsNotFound()
    {
        var app = Path.Combine(_root, "frontend");
        Directory.CreateDirectory(app);
        File.WriteAllText(Path.Combine(app, "index.html"), "x");

        var result = DbMigratorKit.Copy(app, Path.Combine(_root, "kit"));

        Assert.False(result.Found);
        Assert.False(Directory.Exists(Path.Combine(_root, "kit")));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch (IOException) { }
    }
}
