using API.Models.Enums;

namespace API.Models.Entities;

/// <summary>
/// A licence issued to a client for one product/module. The signed payload and signature together form the
/// .lic file handed to the partner; the private signing key never leaves the machine that issued it.
/// </summary>
public class License
{
    public int Id { get; set; }

    public int ClientId { get; set; }
    public Client Client { get; set; } = null!;

    /// <summary>Licence number shown to the partner, e.g. AV-ACME001.</summary>
    public string LicenseId { get; set; } = string.Empty;

    /// <summary>Product or module this licence covers, e.g. AuditVare.</summary>
    public string Product { get; set; } = string.Empty;

    public DateTime IssuedAt { get; set; }
    public DateTime ExpiresAt { get; set; }

    /// <summary>Servers that may hold this licence at the same time (1 today).</summary>
    public int MaxActivations { get; set; } = 1;

    public LicenseStatus Status { get; set; } = LicenseStatus.Issued;

    /// <summary>Base64 of the exact JSON that was signed.</summary>
    public string Payload { get; set; } = string.Empty;

    public string Signature { get; set; } = string.Empty;

    /// <summary>Signing key that produced the signature, so a rotated key can still be identified.</summary>
    public string KeyId { get; set; } = string.Empty;

    /// <summary>Optional feature list, one per line.</summary>
    public string? Features { get; set; }

    public string? Notes { get; set; }

    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public ICollection<LicenseActivation> Activations { get; set; } = new List<LicenseActivation>();
}
