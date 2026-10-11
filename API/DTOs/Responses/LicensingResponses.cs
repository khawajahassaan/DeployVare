namespace API.DTOs.Responses;

public class LicenseDto
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public string ClientName { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;
    public string LicenseId { get; set; } = string.Empty;
    public string Product { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime IssuedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public bool IsExpired { get; set; }
    public int MaxActivations { get; set; }
    public List<string> Features { get; set; } = new();
    public string? Notes { get; set; }
    public string KeyId { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    /// <summary>The machine this licence is bound to, when it is activated.</summary>
    public LicenseActivationDto? CurrentActivation { get; set; }

    /// <summary>Earlier bindings, most recent first (transfers and revocations).</summary>
    public List<LicenseActivationDto> PreviousActivations { get; set; } = new();

    public string FileName { get; set; } = string.Empty;
}

public class LicenseActivationDto
{
    public int Id { get; set; }
    public string MachineFingerprint { get; set; } = string.Empty;
    public string? MachineName { get; set; }
    public string? ServerIdentity { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime ActivatedAt { get; set; }
    public DateTime? DeactivatedAt { get; set; }
    public DateTime? LastIssuedAt { get; set; }
    public string ActivatedBy { get; set; } = string.Empty;
    public string? DeactivationReason { get; set; }
}

/// <summary>A licensing file plus the name it should be saved under.</summary>
public class LicensingFileDto
{
    public string FileName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

/// <summary>What an activation request asks for, and what approving it would do.</summary>
public class ActivationReviewDto
{
    public bool CanApprove { get; set; }
    public string Outcome { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;

    public string LicenseId { get; set; } = string.Empty;
    public string Product { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;
    public string? ClientName { get; set; }
    public string MachineFingerprint { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
    public string? ServerIdentity { get; set; }
    public DateTime RequestedAt { get; set; }
    public DateTime? LicenseExpiresAt { get; set; }

    /// <summary>The machine currently holding the licence, when there is one.</summary>
    public LicenseActivationDto? CurrentActivation { get; set; }
}

public class ActivationApprovalDto
{
    public ActivationReviewDto Review { get; set; } = new();
    public LicensingFileDto ResponseFile { get; set; } = new();
    public LicenseDto License { get; set; } = new();
}

public class SigningKeyDto
{
    public bool Exists { get; set; }
    public string? KeyId { get; set; }
    public string? Algorithm { get; set; }

    /// <summary>Public key to embed in the licensed application so it can verify signatures offline.</summary>
    public string? PublicKey { get; set; }

    public DateTime? CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string Location { get; set; } = string.Empty;
    public string? Protection { get; set; }
}
