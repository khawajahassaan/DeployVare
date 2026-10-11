namespace API.Models;

/// <summary>
/// Licensing settings (appsettings "Licensing" section).
/// The signing key lives outside the application folder so it is never part of a deployment package: an
/// installation without the key file can apply licences and activations, but cannot issue them.
/// </summary>
public class LicensingOptions
{
    public const string SectionName = "Licensing";

    /// <summary>Folder holding the signing key on the machine that issues licences.</summary>
    public string KeyDirectory { get; set; } = @"C:\ProgramData\DeployVare\Licensing";

    public string KeyFileName { get; set; } = "signing-key.json";

    /// <summary>Default licence validity when no expiry date is given.</summary>
    public int DefaultValidityDays { get; set; } = 365;

    /// <summary>Product/module a licence covers when none is given; further modules are sold separately.</summary>
    public string DefaultProduct { get; set; } = "AuditVare";
}
