using API.Models.Entities;
using API.Models.Enums;
using API.Utilities;

namespace API.Tests.AzureDevOps;

public class ManualPackagePairingTests
{
    [Fact]
    public void SingleCounterpart_IsSelected()
    {
        var api = Server(1, ServerType.Api, "10.0.0.1");
        var frontend = Server(2, ServerType.Frontend, "10.0.0.1");

        Assert.Same(frontend, ManualPackagePairing.Resolve(api, new[] { frontend }, requestedId: null));
    }

    [Fact]
    public void RequestedId_WinsOverSameHost()
    {
        var api = Server(1, ServerType.Api, "10.0.0.1");
        var local = Server(2, ServerType.Frontend, "10.0.0.1");
        var other = Server(3, ServerType.Frontend, "10.0.0.2");

        Assert.Same(other, ManualPackagePairing.Resolve(api, new[] { local, other }, requestedId: 3));
    }

    [Fact]
    public void SameHost_IsPreferredWhenSeveralExist()
    {
        var api = Server(1, ServerType.Api, "10.0.0.1");
        var local = Server(2, ServerType.Frontend, "10.0.0.1");
        var other = Server(3, ServerType.Frontend, "10.0.0.2");

        Assert.Same(local, ManualPackagePairing.Resolve(api, new[] { local, other }, requestedId: null));
    }

    [Fact]
    public void NoCounterpart_ExplainsHowToProceed()
    {
        var api = Server(1, ServerType.Api, "10.0.0.1");
        var error = Assert.Throws<InvalidOperationException>(() =>
            ManualPackagePairing.Resolve(api, Array.Empty<ApplicationServer>(), requestedId: null));
        Assert.Contains("This server only", error.Message);
    }

    [Fact]
    public void SeveralHostsWithoutAChoice_AsksTheOperatorToPick()
    {
        var api = Server(1, ServerType.Api, "10.0.0.1");
        var a = Server(2, ServerType.Frontend, "10.0.0.2");
        var b = Server(3, ServerType.Frontend, "10.0.0.3");

        var error = Assert.Throws<InvalidOperationException>(() =>
            ManualPackagePairing.Resolve(api, new[] { a, b }, requestedId: null));
        Assert.Contains("more than one", error.Message);
    }

    private static ApplicationServer Server(int id, ServerType type, string host) => new()
    {
        Id = id,
        ApplicationId = 1,
        ServerName = $"{type}-{id}",
        HostName = host,
        Username = "admin",
        DeploymentPath = @"C:\app",
        AppPoolName = "pool",
        WebsiteName = "site",
        ServerType = type
    };
}
