using System.IO.Compression;
using System.Text;
using API.Services;
using API.Services.Implementations;
using API.Utilities;

namespace API.Tests.AzureDevOps;

public class DatabaseMigrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "DeployVareTests", Guid.NewGuid().ToString("N"));

    public DatabaseMigrationTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [Fact]
    public void ParseOutput_Success_CountsExecutedScripts()
    {
        const string output = """
            DV_MIGRATOR: 2026-09-15 06:00:44 -04:00 [INF] Beginning database upgrade
            DV_MIGRATOR: 2026-09-15 06:00:44 -04:00 [INF] Executing Database Server script '202608132000_AlterManualRiskAssessment.sql'
            DV_MIGRATOR: 2026-09-15 06:00:45 -04:00 [INF] Executing Database Server script 'ActiveUser_GetAll.sql'
            DV_MIGRATOR: Database migration successful!
            DV_MIGRATION_SUCCESS
            """;

        var result = DatabaseMigrationService.ParseOutput(output);

        Assert.True(result.Success);
        Assert.Equal(2, result.ScriptsExecuted);
        Assert.Contains("2 script(s)", result.Message);
        Assert.Empty(result.OutputTail);
    }

    [Fact]
    public void ParseOutput_NonZeroExit_FailsWithOutputTail()
    {
        const string output = """
            DV_MIGRATOR: [INF] Executing Database Server script '202609091100_AlterRiskRegister.sql'
            DV_MIGRATOR: Invalid column name 'LifecycleState'.
            DV_MIGRATION_FAILED: DbMigrator exited with code -1
            """;

        var result = DatabaseMigrationService.ParseOutput(output);

        Assert.False(result.Success);
        Assert.Equal("DbMigrator exited with code -1", result.Message);
        Assert.Contains("Invalid column name 'LifecycleState'.", result.OutputTail);
    }

    [Fact]
    public void ParseOutput_NoMarkers_UsesErrorStream()
    {
        var result = DatabaseMigrationService.ParseOutput(string.Empty, "Failed to connect to server");

        Assert.False(result.Success);
        Assert.Equal("Failed to connect to server", result.Message);
    }

    [Fact]
    public void BuildScriptsPackage_StripsArtifactRoot_AndCountsSqlFiles()
    {
        var zip = CreateZip(
            ("sqlscripts/SQLPatch_New/202609091100_Alter.sql", "ALTER TABLE x ADD y int;"),
            ("sqlscripts/StoredProcedure_New/ActiveUser_GetAll.sql", "CREATE OR ALTER PROCEDURE p AS SELECT 1;"),
            ("sqlscripts/ManualScripts/readme.txt", "manual"));

        var (content, packageZip, sqlFileCount) = ArtifactPackageBuilder.BuildScriptsPackage(zip, Workspace(), "sqlscripts");

        Assert.Equal(2, sqlFileCount);
        Assert.True(Directory.Exists(Path.Combine(content, "SQLPatch_New")));
        using var archive = ZipFile.OpenRead(packageZip);
        Assert.Contains(archive.Entries, e => e.FullName.Replace('\\', '/') == "SQLPatch_New/202609091100_Alter.sql");
    }

    [Fact]
    public void BuildScriptsPackage_WithoutSqlFiles_IsRejected()
    {
        var zip = CreateZip(("sqlscripts/readme.txt", "no scripts"));

        var ex = Assert.Throws<AzureDevOpsException>(() => ArtifactPackageBuilder.BuildScriptsPackage(zip, Workspace(), "sqlscripts"));

        Assert.Equal(AzureDevOpsErrorCode.InvalidArtifact, ex.ErrorCode);
    }

    [Theory]
    [InlineData("appsettings.json;wwwroot", "appsettings.json;wwwroot")]
    [InlineData("appsettings.json\r\nwwwroot\\Template\n\n", "appsettings.json;wwwroot/Template")]
    [InlineData(" /Template/ ; template ", "Template")]
    [InlineData("", null)]
    public void KeepOnServerPaths_Normalize(string input, string? expected)
    {
        Assert.Equal(expected, KeepOnServerPaths.Normalize(input));
    }

    [Theory]
    [InlineData("../secrets")]
    [InlineData("wwwroot/../../Windows")]
    [InlineData(@"C:\inetpub")]
    [InlineData("*.json")]
    public void KeepOnServerPaths_RejectsUnsafePaths(string input)
    {
        Assert.Throws<InvalidOperationException>(() => KeepOnServerPaths.Normalize(input));
    }

    [Fact]
    public void KeepOnServerPaths_Parse_SplitsStoredValue()
    {
        Assert.Equal(new[] { "appsettings.json", "wwwroot" }, KeepOnServerPaths.Parse("appsettings.json;wwwroot"));
        Assert.Empty(KeepOnServerPaths.Parse(null));
    }

    private string Workspace() => Directory.CreateDirectory(Path.Combine(_root, $"ws-{Guid.NewGuid():N}")).FullName;

    private string CreateZip(params (string Name, string Content)[] entries)
    {
        var path = Path.Combine(_root, $"{Guid.NewGuid():N}.zip");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            using var stream = archive.CreateEntry(name).Open();
            stream.Write(Encoding.UTF8.GetBytes(content));
        }
        return path;
    }
}
