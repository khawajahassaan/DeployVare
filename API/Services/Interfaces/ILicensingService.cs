using API.DTOs.Requests;
using API.DTOs.Responses;

namespace API.Services.Interfaces;

/// <summary>
/// Raised for licensing problems that are the operator's to fix (already activated elsewhere, expired,
/// revoked, wrong file). The message is safe to show in the UI and never names the other server.
/// </summary>
public class LicensingException : Exception
{
    public LicensingException(string message) : base(message) { }
    public LicensingException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Issues licences, approves activation requests and manages transfers. Only an installation holding the
/// signing key can issue or approve; everything else is read-only.
/// </summary>
public interface ILicensingService
{
    SigningKeyDto GetSigningKey();
    SigningKeyDto CreateSigningKey(string createdBy);

    Task<List<LicenseDto>> GetLicensesAsync(int? clientId);
    Task<LicenseDto?> GetLicenseAsync(int id);
    Task<LicenseDto> IssueAsync(IssueLicenseRequest request, string issuedBy);
    Task<LicensingFileDto> GetLicenseFileAsync(int id);

    /// <summary>Checks an activation request file and reports what approving it would do; changes nothing.</summary>
    Task<ActivationReviewDto> ReviewActivationAsync(string requestFile);

    /// <summary>Binds the licence to the requesting machine and returns the signed response file.</summary>
    Task<ActivationApprovalDto> ApproveActivationAsync(string requestFile, string approvedBy);

    /// <summary>Releases the current binding so another server can activate.</summary>
    Task<LicenseDto> TransferAsync(int id, string? reason, string performedBy);

    /// <summary>Withdraws the licence entirely.</summary>
    Task<LicenseDto> RevokeAsync(int id, string? reason, string performedBy);

    /// <summary>Extends the expiry date and re-signs the licence file.</summary>
    Task<LicenseDto> RenewAsync(int id, DateTime expiresAt, string performedBy);
}
