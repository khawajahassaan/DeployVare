using System.ComponentModel.DataAnnotations;

namespace API.DTOs.Requests;

public class IssueLicenseRequest
{
    [Required]
    public int ClientId { get; set; }

    /// <summary>Product or module, e.g. AuditVare. Defaults to the configured product.</summary>
    [MaxLength(100)]
    public string? Product { get; set; }

    /// <summary>Licence number; generated from the client code when empty.</summary>
    [MaxLength(100)]
    public string? LicenseId { get; set; }

    /// <summary>Expiry date. Defaults to the configured validity period.</summary>
    public DateTime? ExpiresAt { get; set; }

    public List<string>? Features { get; set; }

    [Range(1, 100)]
    public int MaxActivations { get; set; } = 1;

    [MaxLength(500)]
    public string? Notes { get; set; }
}

public class ActivationFileRequest
{
    /// <summary>Contents of the activation request file produced by the client server.</summary>
    [Required]
    public string RequestFile { get; set; } = string.Empty;
}

public class LicenseActionRequest
{
    [MaxLength(500)]
    public string? Reason { get; set; }
}

public class RenewLicenseRequest
{
    [Required]
    public DateTime ExpiresAt { get; set; }

    [MaxLength(500)]
    public string? Reason { get; set; }
}
