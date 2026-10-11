using API.Models;
using API.Services.Implementations;
using API.Services.Interfaces;

namespace API.Tests.AzureDevOps;

public class MigrationTargetCheckTests
{
    private const string DataSource = "192.227.122.100";
    private const string Database = "AuditVare";

    private static MigrationTargetCheck Classify(MigrationTargetFacts facts) =>
        DatabaseMigrationService.ClassifyTarget(facts, DataSource, Database);

    private static MigrationTargetFacts ExistingAppDatabase() => new()
    {
        SqlServerInstalled = true,
        Connected = true,
        Login = @"WIN-NDSN01LSJAM\Administrator",
        DatabaseExists = true,
        DatabaseState = "ONLINE",
        CanOpenDatabase = true,
        UserTableCount = 412,
        JournalTableExists = true,
        AppliedScriptCount = 1830
    };

    [Fact]
    public void ExistingDbUpManagedDatabase_Runs()
    {
        var check = Classify(ExistingAppDatabase());

        Assert.Equal(MigrationTargetStatus.Ready, check.Status);
        Assert.Equal(MigrationTargetOutcome.Run, check.Outcome);
        Assert.Contains("1830 script(s) already applied", check.Message);
    }

    [Fact]
    public void JournalWithNoTablesYet_StillRuns()
    {
        var facts = ExistingAppDatabase();
        facts.UserTableCount = 0;
        facts.AppliedScriptCount = 0;

        Assert.Equal(MigrationTargetOutcome.Run, Classify(facts).Outcome);
    }

    [Fact]
    public void ReachableServerWithoutDatabase_Skips()
    {
        var check = Classify(new MigrationTargetFacts { SqlServerInstalled = true, Connected = true, DatabaseExists = false });

        Assert.Equal(MigrationTargetStatus.DatabaseMissing, check.Status);
        Assert.Equal(MigrationTargetOutcome.Skip, check.Outcome);
        Assert.Contains("does not exist", check.Message);
    }

    [Fact]
    public void EmptyDatabaseWithoutJournal_Skips()
    {
        var facts = ExistingAppDatabase();
        facts.JournalTableExists = false;
        facts.UserTableCount = 0;
        facts.AppliedScriptCount = 0;

        var check = Classify(facts);

        Assert.Equal(MigrationTargetStatus.EmptyDatabase, check.Status);
        Assert.Equal(MigrationTargetOutcome.Skip, check.Outcome);
    }

    [Fact]
    public void TablesWithoutJournal_Blocks()
    {
        var facts = ExistingAppDatabase();
        facts.JournalTableExists = false;
        facts.AppliedScriptCount = 0;

        var check = Classify(facts);

        Assert.Equal(MigrationTargetStatus.UnjournaledDatabase, check.Status);
        Assert.Equal(MigrationTargetOutcome.Block, check.Outcome);
        Assert.Contains("dbo.SchemaVersions", check.Message);
    }

    [Fact]
    public void SqlServerNotInstalledLocally_Blocks()
    {
        var check = Classify(new MigrationTargetFacts { SqlServerInstalled = false, ConnectionError = "A network-related error occurred" });

        Assert.Equal(MigrationTargetStatus.SqlServerNotInstalled, check.Status);
        Assert.Equal(MigrationTargetOutcome.Block, check.Outcome);
    }

    [Fact]
    public void UnreachableServer_BlocksWithConnectionError()
    {
        var check = Classify(new MigrationTargetFacts
        {
            SqlServerInstalled = null,
            ConnectionError = "A network-related or instance-specific error occurred while establishing a connection to SQL Server."
        });

        Assert.Equal(MigrationTargetStatus.ServerUnreachable, check.Status);
        Assert.Equal(MigrationTargetOutcome.Block, check.Outcome);
        Assert.Contains("Could not connect", check.Message);
    }

    [Fact]
    public void RejectedLogin_IsReportedSeparately()
    {
        var check = Classify(new MigrationTargetFacts { SqlServerInstalled = true, ConnectionError = "Login failed for user 'NT AUTHORITY\\ANONYMOUS LOGON'." });

        Assert.Equal(MigrationTargetStatus.LoginFailed, check.Status);
        Assert.Equal(MigrationTargetOutcome.Block, check.Outcome);
    }

    [Theory]
    [InlineData("OFFLINE")]
    [InlineData("RESTORING")]
    [InlineData(null)]
    public void DatabaseNotOnline_Blocks(string? state)
    {
        var facts = ExistingAppDatabase();
        facts.DatabaseState = state;
        facts.CanOpenDatabase = false;

        var check = Classify(facts);

        Assert.Equal(MigrationTargetStatus.DatabaseUnavailable, check.Status);
        Assert.Equal(MigrationTargetOutcome.Block, check.Outcome);
    }

    [Fact]
    public void DatabaseThatCannotBeOpened_BlocksAsAccessDenied()
    {
        var facts = ExistingAppDatabase();
        facts.CanOpenDatabase = false;
        facts.OpenError = "The server principal is not able to access the database under the current security context.";

        var check = Classify(facts);

        Assert.Equal(MigrationTargetStatus.AccessDenied, check.Status);
        Assert.Equal(MigrationTargetOutcome.Block, check.Outcome);
        Assert.Contains("cannot open it", check.Message);
    }
}
