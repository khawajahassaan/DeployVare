using API.Models.Enums;

namespace API.Models.Entities;

/// <summary>
/// Binding between a licence and one client machine. The machine's private key never leaves that server, so a
/// copied deployment cannot prove ownership of the public key recorded here.
/// </summary>
public class LicenseActivation
{
    public int Id { get; set; }

    public int LicenseId { get; set; }
    public License License { get; set; } = null!;

    /// <summary>Base64 SubjectPublicKeyInfo of the machine key pair generated on the client server.</summary>
    public string MachinePublicKey { get; set; } = string.Empty;

    /// <summary>Short SHA-256 fingerprint of the public key; used for display and uniqueness.</summary>
    public string MachineFingerprint { get; set; } = string.Empty;

    public string? MachineName { get; set; }

    /// <summary>What the request reported about the installation (site, path), for support.</summary>
    public string? ServerIdentity { get; set; }

    public ActivationStatus Status { get; set; } = ActivationStatus.Active;

    public DateTime ActivatedAt { get; set; }
    public DateTime? DeactivatedAt { get; set; }
    public DateTime? LastIssuedAt { get; set; }

    /// <summary>Base64 of the signed activation response handed back to the server.</summary>
    public string ResponsePayload { get; set; } = string.Empty;
    public string ResponseSignature { get; set; } = string.Empty;

    public string ActivatedBy { get; set; } = string.Empty;
    public string? DeactivationReason { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
