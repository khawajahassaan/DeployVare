namespace API.Models;

/// <summary>
/// Raw facts collected from a Windows server by the readiness script (deserialized from its JSON output).
/// </summary>
public class ServerFacts
{
    public string? Os { get; set; }
    public string? ComputerName { get; set; }
    public bool IsServerOs { get; set; }
    public bool IsAdmin { get; set; }
    public bool RebootPending { get; set; }

    public bool IisInstalled { get; set; }
    public bool IisRunning { get; set; }
    public bool SiteExists { get; set; }
    public string? SitePhysicalPath { get; set; }
    public string? SiteState { get; set; }
    public bool AppPoolExists { get; set; }
    public string? AppPoolRuntime { get; set; }
    public string? AppPoolIdentityType { get; set; }
    public string? AppPoolUserName { get; set; }
    public bool AspNetCoreModule { get; set; }
    public bool UrlRewrite { get; set; }
    public List<ServerBindingFact> Bindings { get; set; } = new();

    public List<string> AspNetCoreRuntimes { get; set; } = new();
    public List<string> Sdks { get; set; } = new();

    public bool RedisServiceExists { get; set; }
    public bool RedisRunning { get; set; }
    public string? RedisVersion { get; set; }
    public string? RedisServiceName { get; set; }
    public bool RedisPortOpen { get; set; }

    public List<ServerCertificateFact> Certificates { get; set; } = new();
}

public class ServerBindingFact
{
    public string? Protocol { get; set; }
    public string? BindingInformation { get; set; }
    public string? CertificateHash { get; set; }
}

public class ServerCertificateFact
{
    public string? Thumbprint { get; set; }
    public string? Subject { get; set; }
    public string? FriendlyName { get; set; }
    public string? NotAfter { get; set; }
    public bool HasPrivateKey { get; set; }
}
