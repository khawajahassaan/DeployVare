namespace API.Models;

/// <summary>
/// Server readiness/setup settings (appsettings "ServerSetup" section).
/// </summary>
public class ServerSetupOptions
{
    public const string SectionName = "ServerSetup";

    /// <summary>Folder holding installer files. Relative paths resolve against the API content root.</summary>
    public string InstallersPath { get; set; } = "../Installers";

    /// <summary>Folder on the target server where installers are copied before running.</summary>
    public string RemoteInstallersPath { get; set; } = @"C:\DeployVare\Installers";

    public int RequiredDotNetMajorVersion { get; set; } = 10;

    /// <summary>
    /// Any installed .NET SDK of this major version or newer passes the SDK check; otherwise the
    /// <see cref="RequiredDotNetMajorVersion"/> SDK is offered for installation.
    /// </summary>
    public int MinimumSdkMajorVersion { get; set; } = 8;

    public int RequiredRedisMajorVersion { get; set; } = 8;

    /// <summary>Maximum time for a single remote installation.</summary>
    public int InstallTimeoutMinutes { get; set; } = 30;

    /// <summary>File name patterns (wildcards) used to find each installer in <see cref="InstallersPath"/>.</summary>
    public Dictionary<string, string> InstallerPatterns { get; set; } = new()
    {
        ["HostingBundle"] = "dotnet-hosting-*-win.exe",
        ["DotNetSdk"] = "dotnet-sdk-*-win-x64.exe",
        // Redis on Windows is provided by Memurai (Redis-compatible, service on 6379). A classic
        // Redis-x64 MSI is still matched if that is what sits in the Installers folder instead.
        ["Redis"] = "Memurai*.msi",
        ["UrlRewrite"] = "rewrite_amd64*.msi",
        // SQL Express: the small SSEI bootstrapper (SQL2025-SSEI-Expr.exe) or the full offline SQLEXPR_x64_ENU.exe.
        ["SqlServer"] = "SQL*SSEI*Expr*.exe;SQLEXPR_x64*.exe"
    };
}
