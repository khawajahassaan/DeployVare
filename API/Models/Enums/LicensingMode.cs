namespace API.Models.Enums;

/// <summary>
/// Whether a deployment enables AuditVare's licensing on the target server.
///
/// <see cref="WithoutLicense"/> is the default everywhere on purpose. Existing clients, UAT and the Dev server
/// the pipeline deploys to are all unlicensed, and deploying a newer build to them must not change that: the
/// application only enforces licensing on a server that a licensed deployment has explicitly marked.
///
/// This is a transition setting. When every client has been migrated, the default flips.
/// </summary>
public enum LicensingMode
{
    /// <summary>Deploy the application as it has always been deployed; licensing stays dormant.</summary>
    WithoutLicense = 0,

    /// <summary>Install the client's licence, activate it, and mark the server as a licensed installation.</summary>
    WithLicense = 1
}
