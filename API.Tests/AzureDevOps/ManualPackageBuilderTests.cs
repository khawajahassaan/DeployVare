using System.IO.Compression;
using API.Utilities;

namespace API.Tests.AzureDevOps;

public class ManualPackageBuilderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "DeployVareTests", Guid.NewGuid().ToString("N"));

    private string NewDirectory(string name, params string[] files)
    {
        var directory = Path.Combine(_root, name);
        Directory.CreateDirectory(directory);
        foreach (var file in files)
        {
            var path = Path.Combine(directory, file);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "x");
        }
        return directory;
    }

    [Fact]
    public void Build_KeepsApp_Scripts_AndContentFoldersApart()
    {
        var app = NewDirectory("app-source", "API.dll", "web.config", Path.Combine("runtimes", "win", "native.dll"));
        var scripts = NewDirectory("scripts-source", Path.Combine("SQLPatch_New", "506_add.sql"));
        var template = NewDirectory("Template", "letter.docx");
        var zipPath = Path.Combine(_root, "package.zip");

        ManualPackageBuilder.Build(zipPath, app, scripts, new[] { ("Template", template) }, "readme text");

        using var archive = ZipFile.OpenRead(zipPath);
        var entries = archive.Entries.Select(e => e.FullName).ToList();

        Assert.Contains("app/API.dll", entries);
        Assert.Contains("app/web.config", entries);
        Assert.Contains("app/runtimes/win/native.dll", entries);
        Assert.Contains("sqlscripts/SQLPatch_New/506_add.sql", entries);
        Assert.Contains("content/Template/letter.docx", entries);
        Assert.Contains("README.txt", entries);
    }

    [Fact]
    public void Build_WithoutScriptsOrContent_ContainsOnlyTheApp()
    {
        var app = NewDirectory("frontend-build", "index.html");
        var zipPath = Path.Combine(_root, "frontend.zip");

        ManualPackageBuilder.Build(zipPath, app, scriptsDirectory: null, Array.Empty<(string, string)>(), "readme text");

        using var archive = ZipFile.OpenRead(zipPath);
        var entries = archive.Entries.Select(e => e.FullName).ToList();

        Assert.Equal(new[] { "app/index.html", "README.txt" }, entries);
    }

    [Fact]
    public void Readme_ListsKeptPathsAndMigratorUsage()
    {
        var readme = ManualPackageBuilder.BuildReadme(
            "AuditVareApi", @"C:\inetpub\wwwroot\Auditvare\Api", "1.0.0256", isFrontend: false, hasScripts: true,
            new[] { "Template" }, new[] { "appsettings.json", "wwwroot" }, preserveWebConfig: true);

        Assert.Contains("1.0.0256", readme);
        Assert.Contains(@"C:\inetpub\wwwroot\Auditvare\Api", readme);
        Assert.Contains("appsettings.json", readme);
        Assert.Contains("web.config", readme);
        Assert.Contains("Deploy.ps1", readme);
    }

    [Fact]
    public void Build_WithoutALicence_AddsNoLicenceFolder()
    {
        // The unlicensed package must stay exactly what it always was
        var app = NewDirectory("plain-app", "API.dll");
        var zipPath = Path.Combine(_root, "plain.zip");

        ManualPackageBuilder.Build(zipPath, app, scriptsDirectory: null, Array.Empty<(string, string)>(), "readme text");

        using var archive = ZipFile.OpenRead(zipPath);
        Assert.Equal(new[] { "app/API.dll", "README.txt" }, archive.Entries.Select(e => e.FullName));
    }

    [Fact]
    public void Build_WithALicence_PutsItInItsOwnFolder()
    {
        var app = NewDirectory("licensed-app", "API.dll");
        var zipPath = Path.Combine(_root, "licensed.zip");

        ManualPackageBuilder.Build(zipPath, app, scriptsDirectory: null, Array.Empty<(string, string)>(),
            "readme text", "AV-ACME001.lic", "signed-licence-content");

        using var archive = ZipFile.OpenRead(zipPath);
        var entry = archive.Entries.Single(e => e.FullName == "licence/AV-ACME001.lic");

        using var reader = new StreamReader(entry.Open());
        Assert.Equal("signed-licence-content", reader.ReadToEnd());
    }

    [Fact]
    public void Readme_WithoutALicence_SaysNothingAboutLicensing()
    {
        var readme = ManualPackageBuilder.BuildReadme(
            "AuditVareApi", @"C:\inetpub\wwwroot\Auditvare\Api", "1.0.0256", isFrontend: false, hasScripts: false,
            Array.Empty<string>(), Array.Empty<string>(), preserveWebConfig: false);

        Assert.DoesNotContain("--license-apply", readme);
        Assert.DoesNotContain("Licence:", readme);
        Assert.Contains(".\\Deploy.ps1", readme);
    }

    [Fact]
    public void Readme_WithALicence_PutsActivationBeforeStartingTheSite()
    {
        // The ordering is the point: a licensed application refuses to start until it is activated, so a reader
        // who starts the site first gets an outage with no explanation.
        var readme = ManualPackageBuilder.BuildReadme(
            "AuditVareApi", @"C:\inetpub\wwwroot\Auditvare\Api", "1.0.0256", isFrontend: false, hasScripts: false,
            Array.Empty<string>(), Array.Empty<string>(), preserveWebConfig: false,
            licenseFileName: "AV-ACME001.lic", licenseId: "AV-ACME001");

        Assert.Contains("--license-apply", readme);
        Assert.Contains("--license-status", readme);
        Assert.Contains("AV-ACME001", readme);

        var activation = readme.IndexOf("--license-apply", StringComparison.Ordinal);
        var start = readme.IndexOf("Start the app pool and site only after", StringComparison.Ordinal);
        Assert.True(activation < start, "activation must be described before starting the site");
    }

    [Theory]
    [InlineData("AuditVare Api", "1.0.0256", "AuditVare-Api_1.0.0256_manual-deploy.zip")]
    [InlineData("Dev/Server:1", "v2", "Dev-Server-1_v2_manual-deploy.zip")]
    public void FileName_IsSafeForWindowsAndBrowsers(string serverName, string buildNumber, string expected)
    {
        Assert.Equal(expected, ManualPackageBuilder.BuildFileName(serverName, buildNumber));
    }

    [Fact]
    public void Build_WithDeployScript_AddsScriptConfigAndExtraFiles()
    {
        var app = NewDirectory("app-source", "API.dll");
        var extra = Path.Combine(_root, "Memurai-for-Redis-v8.2-RC3.msi");
        File.WriteAllText(extra, "msi");
        var zipPath = Path.Combine(_root, "offline.zip");

        ManualPackageBuilder.Build(
            zipPath, app, scriptsDirectory: null, Array.Empty<(string, string)>(), "readme text",
            deployScript: "Write-Host hi",
            configJson: "{\"serverType\":\"Api\"}",
            extraFiles: new[] { ("installers/Memurai-for-Redis-v8.2-RC3.msi", extra) });

        using var archive = ZipFile.OpenRead(zipPath);
        var names = archive.Entries.Select(e => e.FullName).ToList();
        Assert.Contains("app/API.dll", names);
        Assert.Contains("Deploy.ps1", names);
        Assert.Contains("deploy-config.json", names);
        Assert.Contains("installers/Memurai-for-Redis-v8.2-RC3.msi", names);
        Assert.Contains("README.txt", names);
    }

    [Fact]
    public void LoadDeployScript_ReadsTheOfflineTemplate()
    {
        var script = ManualPackageBuilder.LoadDeployScript();
        Assert.Contains("Assert-Admin", script);
        Assert.Contains("Ensure-Memurai", script);
        Assert.Contains("deploy-config.json", script);
    }

    [Fact]
    public void TierZip_HoldsTheKitAtItsRoot()
    {
        var database = NewDirectory("database-kit", "Deploy.ps1", "deploy-config.json", "Deploy-AllTiers.ps1",
            Path.Combine("dbmigrator", "DbMigrator.exe"), Path.Combine("sqlscripts", "SQLPatch_New", "1.sql"),
            Path.Combine("backup", "AuditVareSchema.bak"));
        var zipPath = Path.Combine(_root, "AuditVare_database-1.0.1_manual-deploy.zip");

        ManualPackageBuilder.ZipDirectory(database, zipPath);

        using var archive = ZipFile.OpenRead(zipPath);
        var names = archive.Entries.Select(e => e.FullName).ToList();
        Assert.Contains("Deploy.ps1", names);
        Assert.Contains("deploy-config.json", names);
        Assert.Contains("Deploy-AllTiers.ps1", names);
        Assert.Contains("dbmigrator/DbMigrator.exe", names);
        Assert.Contains("sqlscripts/SQLPatch_New/1.sql", names);
        Assert.Contains("backup/AuditVareSchema.bak", names);
    }

    [Fact]
    public void AllTiersScript_JoinsOnlyKitsOfTheSameDownload_AndRunsDatabaseApiFrontend()
    {
        var script = ManualPackageBuilder.BuildAllTiersScript();

        Assert.Contains("packageSetId", script);
        Assert.Contains("Expand-Archive", script);
        Assert.Contains("separateDatabase", script);
        var databaseAt = script.IndexOf("Invoke-Kit 'Database'", StringComparison.Ordinal);
        var apiAt = script.IndexOf("Invoke-Kit 'Api'", StringComparison.Ordinal);
        var frontendAt = script.IndexOf("Invoke-Kit 'Frontend'", StringComparison.Ordinal);
        Assert.True(databaseAt >= 0 && databaseAt < apiAt && apiAt < frontendAt, "kits must run database, api, frontend");
        // the database settings asked once go to both the database and API kits
        Assert.Contains("'-DatabaseName'", script);
        Assert.Contains("($shared + $dbArgs)", script);
        Assert.Contains("UseDefaults", script);
        Assert.Contains("InstallersPath", script);
    }

    [Fact]
    public void TierSetReadme_ExplainsSeparateServersAndOneMachine()
    {
        var three = ManualPackageBuilder.BuildTierSetReadme(threeTier: true);
        var two = ManualPackageBuilder.BuildTierSetReadme(threeTier: false);

        Assert.Contains("database first, then API, then frontend", three);
        Assert.Contains("Deploy-AllTiers.ps1", three);
        Assert.Contains("also sets up the database", two);
        Assert.Contains("Deploy-AllTiers.ps1", two);
    }

    [Fact]
    public void TierZip_ReportsProgressUpToTheFullSize()
    {
        var api = NewDirectory("api-kit2", "Deploy.ps1", Path.Combine("app", "API.dll"), Path.Combine("app", "web.config"));
        var expectedTotal = Directory.EnumerateFiles(api, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
        var reports = new List<(long Done, long Total)>();

        ManualPackageBuilder.ZipDirectory(api, Path.Combine(_root, "progress.zip"), (done, total) => reports.Add((done, total)));

        Assert.NotEmpty(reports);
        Assert.All(reports, r => Assert.Equal(expectedTotal, r.Total));
        Assert.Equal(expectedTotal, reports[^1].Done);
    }

    [Fact]
    public void DeployScript_HandlesTheDatabaseKitAndAsksForNames()
    {
        var script = ManualPackageBuilder.LoadDeployScript();
        Assert.Contains("[switch]$UseDefaults", script);
        Assert.Contains("[string]$DatabaseName", script);
        Assert.Contains("function Confirm-Settings", script);
        Assert.Contains("serverType -eq 'Database'", script);
        Assert.Contains("function Test-RunsDatabase", script);
        Assert.Contains("separateDatabase", script);
    }

    [Fact]
    public void Readme_SaysWhetherInstallersAreIncluded()
    {
        var withInstallers = ManualPackageBuilder.BuildReadme(
            new API.Models.ManualDeployConfig { ServerName = "Dev API", ServerType = "Api", IncludesInstallers = true }, "1.0.1", Array.Empty<string>());
        var without = ManualPackageBuilder.BuildReadme(
            new API.Models.ManualDeployConfig { ServerName = "Dev Web", ServerType = "Frontend", IncludesInstallers = false }, "1.0.1", Array.Empty<string>());

        Assert.Contains("Installers:         Included", withInstallers);
        Assert.Contains("Not included", without);
        Assert.Contains("URL Rewrite", without);
    }

    [Fact]
    public void TierFileName_NamesTheTierAndBuild()
    {
        Assert.Equal("AuditVare_database-1.0.0277_manual-deploy.zip",
            ManualPackageBuilder.BuildTierFileName("AuditVare", "database", "1.0.0277"));
        Assert.Equal("AuditVare_frontend-20261011.2_manual-deploy.zip",
            ManualPackageBuilder.BuildTierFileName("AuditVare", "frontend", "20261011.2"));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch (IOException) { }
    }
}
