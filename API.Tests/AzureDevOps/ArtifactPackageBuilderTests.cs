using System.IO.Compression;
using System.Text;
using API.Models.Enums;
using API.Services;
using API.Utilities;

namespace API.Tests.AzureDevOps;

public class ArtifactPackageBuilderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "DeployVareTests", Guid.NewGuid().ToString("N"));
    private readonly string _workspace;

    public ArtifactPackageBuilderTests()
    {
        _workspace = Path.Combine(_root, "workspace");
        Directory.CreateDirectory(_workspace);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [Fact]
    public void StripsArtifactRootFolder_AndSelectsConfiguredSubfolder()
    {
        var zip = CreateZip(
            ("drop/AuditVare-WebApi/AuditVare.dll", "dll"),
            ("drop/AuditVare-WebApi/web.config", "<configuration />"),
            ("drop/AuditVare-WebApi/wwwroot/app.css", "css"),
            ("drop/Template/report.docx", "docx"));

        var package = ArtifactPackageBuilder.Build(zip, _workspace, "drop", ServerType.Api, "AuditVare-WebApi");

        Assert.Equal(new[] { "AuditVare.dll", "web.config", "wwwroot/app.css" }, package.Files.Select(f => f.RelativePath));
        Assert.True(package.ContainsWebConfig);
        Assert.Empty(package.Warnings);

        using var packageZip = ZipFile.OpenRead(package.PackageZipPath);
        Assert.Contains(packageZip.Entries, e => e.FullName.Replace('\\', '/') == "AuditVare.dll");
        Assert.DoesNotContain(packageZip.Entries, e => e.FullName.Contains("Template"));
    }

    [Fact]
    public void FrontendArtifactAtRoot_IsDeployedAsIs()
    {
        var zip = CreateZip(
            ("frontend-drop/index.html", "<html />"),
            ("frontend-drop/assets/index.js", "js"));

        var package = ArtifactPackageBuilder.Build(zip, _workspace, "frontend-drop", ServerType.Frontend, null);

        Assert.Equal(new[] { "assets/index.js", "index.html" }, package.Files.Select(f => f.RelativePath));
        Assert.Empty(package.Warnings);
    }

    [Fact]
    public void MissingSubfolder_ThrowsAndListsAvailableFolders()
    {
        var zip = CreateZip(("drop/AuditVare-WebApi/AuditVare.dll", "dll"), ("drop/Template/a.txt", "a"));

        var ex = Assert.Throws<AzureDevOpsException>(() =>
            ArtifactPackageBuilder.Build(zip, _workspace, "drop", ServerType.Api, "WebApi"));

        Assert.Equal(AzureDevOpsErrorCode.InvalidArtifact, ex.ErrorCode);
        Assert.Contains("AuditVare-WebApi", ex.Message);
    }

    [Theory]
    [InlineData("../evil.txt")]
    [InlineData("drop/../../evil.txt")]
    [InlineData("/absolute.txt")]
    public void PathTraversalEntries_AreRejected(string entryName)
    {
        var zip = CreateZip(("drop/app.dll", "dll"), (entryName, "evil"));

        var ex = Assert.Throws<AzureDevOpsException>(() =>
            ArtifactPackageBuilder.Build(zip, _workspace, "drop", ServerType.Api, null));

        Assert.Equal(AzureDevOpsErrorCode.InvalidArtifact, ex.ErrorCode);
        Assert.False(File.Exists(Path.Combine(_root, "evil.txt")));
        Assert.False(File.Exists(Path.Combine(Path.GetTempPath(), "DeployVareTests", "evil.txt")));
    }

    [Fact]
    public void SubfolderSettingCannotEscapeArtifact()
    {
        var zip = CreateZip(("drop/app.dll", "dll"));

        var ex = Assert.Throws<AzureDevOpsException>(() =>
            ArtifactPackageBuilder.Build(zip, _workspace, "drop", ServerType.Api, "../../"));

        Assert.Equal(AzureDevOpsErrorCode.InvalidArtifact, ex.ErrorCode);
    }

    [Fact]
    public void ApiPackageWithoutAssemblies_IsRejected()
    {
        var zip = CreateZip(("drop/readme.txt", "no dlls"));

        var ex = Assert.Throws<AzureDevOpsException>(() =>
            ArtifactPackageBuilder.Build(zip, _workspace, "drop", ServerType.Api, null));

        Assert.Equal(AzureDevOpsErrorCode.InvalidArtifact, ex.ErrorCode);
    }

    [Fact]
    public void FrontendPackageWithoutIndexHtml_OnlyWarns()
    {
        var zip = CreateZip(("frontend-drop/app.js", "js"));

        var package = ArtifactPackageBuilder.Build(zip, _workspace, "frontend-drop", ServerType.Frontend, null);

        Assert.Single(package.Warnings);
    }

    [Fact]
    public void EmptyArtifact_IsRejected()
    {
        var zip = CreateZip(("drop/", string.Empty));

        var ex = Assert.Throws<AzureDevOpsException>(() =>
            ArtifactPackageBuilder.Build(zip, _workspace, "drop", ServerType.Frontend, null));

        Assert.Equal(AzureDevOpsErrorCode.InvalidArtifact, ex.ErrorCode);
    }

    [Fact]
    public void CorruptZip_IsRejected()
    {
        var zip = Path.Combine(_root, "corrupt.zip");
        File.WriteAllText(zip, "this is not a zip file");

        var ex = Assert.Throws<AzureDevOpsException>(() =>
            ArtifactPackageBuilder.Build(zip, _workspace, "drop", ServerType.Api, null));

        Assert.Equal(AzureDevOpsErrorCode.InvalidArtifact, ex.ErrorCode);
    }

    [Fact]
    public void SingleNestedPublishZip_IsExtracted()
    {
        var innerZip = CreateZip(("AuditVare.dll", "dll"), ("appsettings.json", "{}"));
        var zip = CreateZipWithBinary(("drop/AuditVare.zip", File.ReadAllBytes(innerZip)));

        var package = ArtifactPackageBuilder.Build(zip, _workspace, "drop", ServerType.Api, null);

        Assert.Equal(new[] { "appsettings.json", "AuditVare.dll" }, package.Files.Select(f => f.RelativePath));
    }

    [Fact]
    public void ExtractionAboveSizeLimit_IsRejected()
    {
        var zip = CreateZip(("drop/app.dll", new string('x', 10_000)));

        var ex = Assert.Throws<AzureDevOpsException>(() =>
            ArtifactPackageBuilder.Build(zip, _workspace, "drop", ServerType.Api, null, maxExtractedBytes: 1_000));

        Assert.Equal(AzureDevOpsErrorCode.InvalidArtifact, ex.ErrorCode);
    }

    [Fact]
    public void TooManyEntries_IsRejected()
    {
        var zip = CreateZip(("drop/a.dll", "a"), ("drop/b.dll", "b"), ("drop/c.dll", "c"));

        var ex = Assert.Throws<AzureDevOpsException>(() =>
            ArtifactPackageBuilder.Build(zip, _workspace, "drop", ServerType.Api, null, maxEntries: 2));

        Assert.Equal(AzureDevOpsErrorCode.InvalidArtifact, ex.ErrorCode);
    }

    private string CreateZip(params (string Name, string Content)[] entries) =>
        CreateZipWithBinary(entries.Select(e => (e.Name, Encoding.UTF8.GetBytes(e.Content))).ToArray());

    private string CreateZipWithBinary(params (string Name, byte[] Content)[] entries)
    {
        var path = Path.Combine(_root, $"{Guid.NewGuid():N}.zip");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            var entry = archive.CreateEntry(name);
            if (name.EndsWith('/')) continue;
            using var stream = entry.Open();
            stream.Write(content);
        }
        return path;
    }
}
