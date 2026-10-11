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
    public void CombinedPackage_NestsBothKitsAndARootOrchestrator()
    {
        var api = NewDirectory("api-kit", "Deploy.ps1", "deploy-config.json", Path.Combine("app", "API.dll"));
        var frontend = NewDirectory("frontend-kit", "Deploy.ps1", Path.Combine("app", "index.html"));
        var zipPath = Path.Combine(_root, "combined.zip");

        ManualPackageBuilder.BuildCombined(
            zipPath, api, frontend, "combined readme", ManualPackageBuilder.BuildCombinedDeployScript());

        using var archive = ZipFile.OpenRead(zipPath);
        var names = archive.Entries.Select(e => e.FullName).ToList();
        Assert.Contains("api/Deploy.ps1", names);
        Assert.Contains("api/app/API.dll", names);
        Assert.Contains("frontend/Deploy.ps1", names);
        Assert.Contains("frontend/app/index.html", names);
        Assert.Contains("Deploy.ps1", names);
        Assert.Contains("README.txt", names);

        var rootScript = new StreamReader(archive.Entries.Single(e => e.FullName == "Deploy.ps1").Open()).ReadToEnd();
        Assert.Contains("Invoke-Kit 'api'", rootScript);
        Assert.Contains("Invoke-Kit 'frontend'", rootScript);
    }

    [Fact]
    public void CombinedFileName_IncludesBothBuilds()
    {
        Assert.Equal(
            "AuditVare_api-1.0.0277_frontend-1.0.0278_manual-deploy.zip",
            ManualPackageBuilder.BuildCombinedFileName("AuditVare", "1.0.0277", "1.0.0278"));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch (IOException) { }
    }
}
