using API.DTOs.Responses;
using API.Models;
using API.Models.Entities;
using API.Models.Enums;
using API.Services.Implementations;
using API.Utilities;

namespace API.Tests.ServerSetup;

public class ServerReadinessEvaluatorTests
{
    private static readonly DateTime Now = new(2026, 9, 15, 0, 0, 0, DateTimeKind.Utc);
    private static readonly ServerSetupOptions Options = new();
    private const string Thumbprint = "0123456789ABCDEF0123456789ABCDEF01234567";

    [Fact]
    public void ApiServer_WithEverythingInstalled_IsReady()
    {
        var report = Evaluate(ApiServer(), HealthyApiFacts());

        Assert.True(report.IsReady);
        Assert.All(report.Items.Where(i => i.Required), i => Assert.Equal(ReadinessStatus.Pass, i.Status));
        Assert.Contains(report.Items, i => i.Key == "hosting-bundle");
        Assert.Contains(report.Items, i => i.Key == "dotnet-sdk");
        Assert.Contains(report.Items, i => i.Key == "redis");
        Assert.DoesNotContain(report.Items, i => i.Key == "url-rewrite");
    }

    [Fact]
    public void MissingIis_OffersInstall_AndSkipsSiteChecks()
    {
        var facts = HealthyApiFacts();
        facts.IisInstalled = false;
        facts.IisRunning = false;
        facts.SiteExists = false;
        facts.AppPoolExists = false;
        facts.Bindings.Clear();

        var report = Evaluate(ApiServer(), facts);

        Assert.False(report.IsReady);
        Assert.Equal(ReadinessFix.InstallIis, Item(report, "iis").Fix);
        Assert.Equal(ReadinessStatus.Skipped, Item(report, "site").Status);
        Assert.Equal(ReadinessStatus.Skipped, Item(report, "bindings").Status);
    }

    [Fact]
    public void MissingSite_OffersCreateSite()
    {
        var facts = HealthyApiFacts();
        facts.SiteExists = false;
        facts.AppPoolExists = false;
        facts.Bindings.Clear();

        var report = Evaluate(ApiServer(), facts);

        Assert.False(report.IsReady);
        Assert.Equal(ReadinessFix.CreateSite, Item(report, "site").Fix);
        Assert.Equal(ReadinessFix.CreateSite, Item(report, "app-pool").Fix);
    }

    [Fact]
    public void HostingBundleRuntimeWithoutIisModule_Fails()
    {
        var facts = HealthyApiFacts();
        facts.AspNetCoreModule = false;

        var item = Item(Evaluate(ApiServer(), facts), "hosting-bundle");

        Assert.Equal(ReadinessStatus.Fail, item.Status);
        Assert.Equal(nameof(ServerComponent.HostingBundle), item.Component);
    }

    [Fact]
    public void OlderRuntime_FailsHostingBundle_ButSdk8OrNewerPasses()
    {
        var facts = HealthyApiFacts();
        facts.AspNetCoreRuntimes = new() { "8.0.11" };
        facts.Sdks = new() { "3.1.426", "6.0.428", "8.0.414", "9.0.305" };

        var report = Evaluate(ApiServer(), facts);

        Assert.Equal(ReadinessStatus.Fail, Item(report, "hosting-bundle").Status);
        Assert.Equal(ReadinessStatus.Pass, Item(report, "dotnet-sdk").Status);
        Assert.Contains("8.0.414", Item(report, "dotnet-sdk").Detail);
    }

    [Fact]
    public void OnlySdksOlderThanMinimum_FailsAndOffersInstall()
    {
        var facts = HealthyApiFacts();
        facts.Sdks = new() { "3.1.426", "6.0.428" };

        var item = Item(Evaluate(ApiServer(), facts), "dotnet-sdk");

        Assert.Equal(ReadinessStatus.Fail, item.Status);
        Assert.Equal(nameof(ServerComponent.DotNetSdk), item.Component);
        Assert.Contains("6.0.428", item.Detail);
    }

    [Fact]
    public void InstallerCatalog_FindsInstallersFolderAboveBuildOutput()
    {
        var root = Path.Combine(Path.GetTempPath(), "DeployVareTests", Guid.NewGuid().ToString("N"));
        var installers = Directory.CreateDirectory(Path.Combine(root, "Installers")).FullName;
        var contentRoot = Directory.CreateDirectory(Path.Combine(root, "API", "bin", "Debug", "net8.0")).FullName;

        try
        {
            Assert.Equal(installers, InstallerCatalog.ResolveDirectory("../Installers", contentRoot, Array.Empty<string>()));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void SqlServerPattern_MatchesSseiExprBootstrapper()
    {
        var root = Path.Combine(Path.GetTempPath(), "DeployVareTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "SQL2025-SSEI-Expr.exe"), "x");
        try
        {
            var matches = Directory.GetFiles(root, "SQL*SSEI*Expr*.exe");
            Assert.Contains(matches, f => Path.GetFileName(f) == "SQL2025-SSEI-Expr.exe");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void InstallerAvailability_IsReported()
    {
        var facts = HealthyApiFacts();
        facts.Sdks.Clear();
        facts.RedisServiceExists = false;

        var report = Evaluate(ApiServer(), facts, Installers(sdk: true, redis: false));

        Assert.True(Item(report, "dotnet-sdk").InstallerAvailable);
        Assert.False(Item(report, "redis").InstallerAvailable);
        Assert.Contains("Memurai*.msi", Item(report, "redis").Detail);
    }

    [Fact]
    public void MemuraiService_PassesRedisCheck()
    {
        var facts = HealthyApiFacts();
        facts.RedisServiceName = "Memurai";
        facts.RedisVersion = "7.2.7";
        var item = Item(Evaluate(ApiServer(), facts), "redis");
        Assert.Equal(ReadinessStatus.Pass, item.Status);
        Assert.Contains("Memurai", item.Name);
        Assert.Contains("6379", item.Detail);
    }

    [Fact]
    public void WrongRedisVersion_Fails_AndStoppedRedis_BlocksReadiness()
    {
        var wrongVersion = HealthyApiFacts();
        wrongVersion.RedisServiceName = "Redis";
        wrongVersion.RedisVersion = "5.0.14.1";
        Assert.Equal(ReadinessStatus.Fail, Item(Evaluate(ApiServer(), wrongVersion), "redis").Status);

        var stopped = HealthyApiFacts();
        stopped.RedisRunning = false;
        var report = Evaluate(ApiServer(), stopped);
        Assert.Equal(ReadinessStatus.Warning, Item(report, "redis").Status);
        Assert.False(report.IsReady);
    }

    [Fact]
    public void FrontendServer_ChecksUrlRewrite_NotDotNetOrRedis()
    {
        var facts = HealthyApiFacts();
        facts.AspNetCoreRuntimes.Clear();
        facts.Sdks.Clear();
        facts.RedisServiceExists = false;
        facts.UrlRewrite = false;

        var report = Evaluate(FrontendServer(), facts);

        Assert.DoesNotContain(report.Items, i => i.Key is "hosting-bundle" or "dotnet-sdk" or "redis");
        Assert.Equal(ReadinessStatus.Fail, Item(report, "url-rewrite").Status);
        Assert.False(report.IsReady);

        facts.UrlRewrite = true;
        Assert.True(Evaluate(FrontendServer(), facts).IsReady);
    }

    [Fact]
    public void FrontendWithoutRuntimeSettings_WarnsButStaysReady()
    {
        var facts = HealthyApiFacts();

        var withoutSettings = Evaluate(FrontendServer(), facts);
        var item = Item(withoutSettings, "runtime-config");
        Assert.Equal(ReadinessStatus.Warning, item.Status);
        Assert.False(item.Required);
        Assert.Equal(ReadinessFix.ConfigureRuntimeSettings, item.Fix);
        Assert.True(withoutSettings.IsReady);

        var server = FrontendServer();
        server.RuntimeConfigJson = "[{\"Key\":\"VITE_API_URL\",\"Value\":\"https://pprod-api.auditvare.cloud/v1\"}]";
        Assert.Equal(ReadinessStatus.Pass, Item(Evaluate(server, facts), "runtime-config").Status);

        Assert.DoesNotContain(Evaluate(ApiServer(), facts).Items, i => i.Key == "runtime-config");
    }

    [Fact]
    public void NoUsableCertificate_OffersCertificateSetup()
    {
        var facts = HealthyApiFacts();
        facts.Certificates = new()
        {
            new() { Thumbprint = Thumbprint, FriendlyName = "WMSVC-SHA2", NotAfter = "2025-01-01T00:00:00Z", HasPrivateKey = true },
            new() { Thumbprint = "FFFF", NotAfter = "2030-01-01T00:00:00Z", HasPrivateKey = false }
        };

        var report = Evaluate(ApiServer(), facts);

        Assert.Equal(ReadinessFix.ConfigureCertificate, Item(report, "certificate").Fix);
        Assert.Equal(ReadinessStatus.Fail, Item(report, "bindings").Status); // bound certificate is expired
    }

    [Fact]
    public void MissingHttpsBinding_OffersBindingSetup()
    {
        var facts = HealthyApiFacts();
        facts.Bindings.RemoveAll(b => b.Protocol == "https");

        var item = Item(Evaluate(ApiServer(), facts), "bindings");

        Assert.Equal(ReadinessStatus.Fail, item.Status);
        Assert.Equal(ReadinessFix.ConfigureBindings, item.Fix);
        Assert.Contains("443", item.Detail);
    }

    [Fact]
    public void DeploymentPathDifferentFromSiteFolder_WarnsWithoutBlocking()
    {
        var server = ApiServer();
        server.DeploymentPath = @"C:\inetpub\wwwroot\DeployVareTesting";

        var report = Evaluate(server, HealthyApiFacts());

        var item = Item(report, "deployment-path");
        Assert.Equal(ReadinessStatus.Warning, item.Status);
        Assert.False(item.Required);
        Assert.Equal(ReadinessFix.UpdateDeploymentPath, item.Fix);
        Assert.True(report.IsReady);
    }

    [Fact]
    public void NonAdministratorSshUser_IsNotReady()
    {
        var facts = HealthyApiFacts();
        facts.IsAdmin = false;

        Assert.False(Evaluate(ApiServer(), facts).IsReady);
    }

    [Theory]
    [InlineData("10.0.12", 10)]
    [InlineData("v5.0.14.1", 5)]
    [InlineData("", -1)]
    [InlineData("abc", -1)]
    public void MajorOf_ParsesVersions(string version, int expected)
    {
        Assert.Equal(expected, ServerReadinessEvaluator.MajorOf(version));
    }

    [Theory]
    [InlineData(@"C:\inetpub\wwwroot\App\", @"c:/inetpub/wwwroot/app", true)]
    [InlineData(@"C:\inetpub\wwwroot\App", @"C:\inetpub\wwwroot\App2", false)]
    public void PathsEqual_IgnoresCaseSlashesAndTrailingSeparator(string left, string right, bool expected)
    {
        Assert.Equal(expected, ServerReadinessEvaluator.PathsEqual(left, right));
    }

    [Theory]
    [InlineData("dotnet-hosting-10.0.12-win.exe", "10.0.12")]
    [InlineData("dotnet-sdk-10.0.401-win-x64.exe", "10.0.401")]
    [InlineData("Memurai-for-Redis-v8.2-RC3.msi", "8.2")]
    [InlineData("SQL2025-SSEI-Expr.exe", null)]
    [InlineData("rewrite_amd64_en-US.msi", null)]
    public void InstallerCatalog_ParsesVersionFromFileName(string fileName, string? expected)
    {
        Assert.Equal(expected, InstallerCatalog.ParseVersion(fileName));
    }

    [Fact]
    public void ApiServer_WithoutLinkedDatabase_IsStillReady()
    {
        var report = Evaluate(ApiServer(), HealthyApiFacts(), database: new DatabaseAccessCheck { LoginName = @"IIS APPPOOL\AuditVareDevBackend" });

        // A database is optional; deployments skip migrations instead of failing
        var item = Item(report, "database");
        Assert.Equal(ReadinessStatus.Warning, item.Status);
        Assert.False(item.Required);
        Assert.Equal(ReadinessFix.ConfigureDatabase, item.Fix);
        Assert.True(report.IsReady);
    }

    [Fact]
    public void MissingLoginOrDbOwner_OffersGrant()
    {
        var database = HealthyDatabase();
        database.Facts!.LoginExists = false;
        database.Facts.UserExists = false;
        database.Facts.IsDbOwner = false;

        var item = Item(Evaluate(ApiServer(), HealthyApiFacts(), database: database), "database");

        Assert.Equal(ReadinessFix.GrantDatabaseAccess, item.Fix);
        Assert.Contains("server login", item.Detail);
        Assert.Contains("db_owner", item.Detail);
    }

    [Fact]
    public void MissingDatabase_WarnsWithoutBlockingDeployment()
    {
        var database = HealthyDatabase();
        database.Facts!.DatabaseExists = false;

        var report = Evaluate(ApiServer(), HealthyApiFacts(), database: database);
        var item = Item(report, "database");

        Assert.Equal(ReadinessStatus.Warning, item.Status);
        Assert.False(item.Required);
        Assert.Null(item.Fix);
        Assert.Contains("does not exist", item.Detail);
        Assert.True(report.IsReady);
    }

    [Fact]
    public void SshUserNotSysadmin_OrSqlOnOtherMachine_Fails()
    {
        var notSysadmin = HealthyDatabase();
        notSysadmin.Facts!.IsSysadmin = false;
        Assert.Contains("sysadmin", Item(Evaluate(ApiServer(), HealthyApiFacts(), database: notSysadmin), "database").Detail);

        var remote = HealthyDatabase();
        remote.Facts!.SqlOnSameServer = false;
        Assert.Contains("different machine", Item(Evaluate(ApiServer(), HealthyApiFacts(), database: remote), "database").Detail);

        var unreachable = HealthyDatabase();
        unreachable.Facts!.Connected = false;
        unreachable.Facts.ConnectionError = "Login failed";
        Assert.Contains("Login failed", Item(Evaluate(ApiServer(), HealthyApiFacts(), database: unreachable), "database").Detail);
    }

    [Fact]
    public void DatabaseAccessGranted_Passes_AndFrontendHasNoDatabaseItem()
    {
        var report = Evaluate(ApiServer(), HealthyApiFacts());
        Assert.Equal(ReadinessStatus.Pass, Item(report, "database").Status);
        Assert.Equal("AuditVare_Dev", report.Database!.DatabaseName);

        var frontend = Evaluate(FrontendServer(), HealthyApiFacts(), database: null);
        Assert.DoesNotContain(frontend.Items, i => i.Key == "database");
        Assert.Null(frontend.Database);
    }

    [Theory]
    [InlineData("ApplicationPoolIdentity", null, @"IIS APPPOOL\AuditVareDevBackend")]
    [InlineData("", null, @"IIS APPPOOL\AuditVareDevBackend")]
    [InlineData("NetworkService", null, @"NT AUTHORITY\NETWORK SERVICE")]
    [InlineData("SpecificUser", @"CORP\svc-auditvare", @"CORP\svc-auditvare")]
    [InlineData("SpecificUser", @".\svc-auditvare", @"WEB01\svc-auditvare")]
    [InlineData("SpecificUser", "", null)]
    public void DatabaseLogin_FollowsAppPoolIdentity(string identityType, string? userName, string? expected)
    {
        Assert.Equal(expected, ServerReadinessEvaluator.GetDatabaseLoginName("AuditVareDevBackend", identityType, userName, "WEB01"));
    }

    private static ServerReadinessReportDto Evaluate(ApplicationServer server, ServerFacts facts, List<InstallerFileDto>? installers = null) =>
        ServerReadinessEvaluator.Evaluate(server, facts, Options, installers ?? Installers(), Now,
            server.ServerType == ServerType.Api ? HealthyDatabase() : null);

    private static ServerReadinessReportDto Evaluate(ApplicationServer server, ServerFacts facts, DatabaseAccessCheck? database) =>
        ServerReadinessEvaluator.Evaluate(server, facts, Options, Installers(), Now, database);

    private static DatabaseAccessCheck HealthyDatabase() => new()
    {
        DatabaseInstanceId = 7,
        InstanceName = "Dev",
        DataSource = "localhost",
        DatabaseName = "AuditVare_Dev",
        LoginName = @"IIS APPPOOL\AuditVareDevBackend",
        Facts = new DatabaseFacts
        {
            SqlOnSameServer = true,
            SqlHost = "localhost",
            Connected = true,
            ConnectedAs = @"WEB01\Administrator",
            IsSysadmin = true,
            DatabaseExists = true,
            LoginExists = true,
            UserExists = true,
            IsDbOwner = true
        }
    };

    private static ReadinessItemDto Item(ServerReadinessReportDto report, string key) => Assert.Single(report.Items, i => i.Key == key);

    private static ApplicationServer ApiServer() => new()
    {
        Id = 1,
        ServerName = "Dev",
        WebsiteName = "AuditVareDevBackend",
        AppPoolName = "AuditVareDevBackend",
        DeploymentPath = @"C:\inetpub\wwwroot\AuditVareDevBackend\AuditVare-WebApi",
        ServerType = ServerType.Api
    };

    private static ApplicationServer FrontendServer()
    {
        var server = ApiServer();
        server.ServerType = ServerType.Frontend;
        return server;
    }

    private static ServerFacts HealthyApiFacts() => new()
    {
        Os = "Microsoft Windows Server 2022 Datacenter",
        IsServerOs = true,
        IsAdmin = true,
        IisInstalled = true,
        IisRunning = true,
        SiteExists = true,
        SiteState = "Started",
        SitePhysicalPath = @"C:\inetpub\wwwroot\AuditVareDevBackend\AuditVare-WebApi",
        AppPoolExists = true,
        AspNetCoreModule = true,
        UrlRewrite = true,
        AspNetCoreRuntimes = new() { "8.0.11", "10.0.12" },
        Sdks = new() { "10.0.401" },
        RedisServiceExists = true,
        RedisRunning = true,
        RedisVersion = "8.2.0",
        RedisServiceName = "Memurai",
        RedisPortOpen = true,
        Bindings = new()
        {
            new() { Protocol = "http", BindingInformation = "*:80:dev-api.auditvare.cloud" },
            new() { Protocol = "https", BindingInformation = "*:443:dev-api.auditvare.cloud", CertificateHash = Thumbprint }
        },
        Certificates = new()
        {
            new() { Thumbprint = Thumbprint, FriendlyName = "WMSVC-SHA2", Subject = "CN=WMSvc-SHA2", NotAfter = "2035-01-01T00:00:00Z", HasPrivateKey = true }
        }
    };

    private static List<InstallerFileDto> Installers(bool sdk = true, bool redis = true) => new()
    {
        new() { Component = "HostingBundle", Available = true, FileName = "dotnet-hosting-10.0.12-win.exe", Pattern = "dotnet-hosting-*-win.exe" },
        new() { Component = "DotNetSdk", Available = sdk, FileName = sdk ? "dotnet-sdk-10.0.401-win-x64.exe" : null, Pattern = "dotnet-sdk-*-win-x64.exe" },
        new() { Component = "Redis", Available = redis, FileName = redis ? "Memurai-for-Redis-v8.2-RC3.msi" : null, Pattern = "Memurai*.msi" },
        new() { Component = "UrlRewrite", Available = false, Pattern = "rewrite_amd64*.msi" }
    };
}
