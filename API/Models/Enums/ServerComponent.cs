namespace API.Models.Enums;

/// <summary>
/// Software DeployVare can install on a target server from the Installers folder.
/// </summary>
public enum ServerComponent
{
    HostingBundle,
    DotNetSdk,
    Redis,
    UrlRewrite,
    SqlServer
}
