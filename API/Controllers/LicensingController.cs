using API.DTOs.Requests;
using API.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

/// <summary>
/// Issuing licences and approving activations. Only an installation that holds the signing key can do either;
/// a copy handed to a partner has no key and every write here fails with a clear message.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class LicensingController : ControllerBase
{
    private readonly ILicensingService _licensingService;
    private readonly ILogger<LicensingController> _logger;

    public LicensingController(ILicensingService licensingService, ILogger<LicensingController> logger)
    {
        _licensingService = licensingService;
        _logger = logger;
    }

    private string CurrentUser => User.Identity?.Name ?? "Unknown";

    /// <summary>Whether this installation can issue licences, and the public key to embed in the application.</summary>
    [HttpGet("signing-key")]
    public IActionResult GetSigningKey() => Ok(_licensingService.GetSigningKey());

    /// <summary>Creates the signing key on this machine (once).</summary>
    [HttpPost("signing-key")]
    public IActionResult CreateSigningKey()
    {
        try
        {
            return Ok(_licensingService.CreateSigningKey(CurrentUser));
        }
        catch (LicensingException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create the signing key");
            return StatusCode(500, new { message = "An error occurred while creating the signing key" });
        }
    }

    [HttpGet("licenses")]
    public async Task<IActionResult> GetLicenses([FromQuery] int? clientId)
    {
        try
        {
            return Ok(await _licensingService.GetLicensesAsync(clientId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get licences");
            return StatusCode(500, new { message = "An error occurred while retrieving licences" });
        }
    }

    [HttpGet("licenses/{id}")]
    public async Task<IActionResult> GetLicense(int id)
    {
        try
        {
            var license = await _licensingService.GetLicenseAsync(id);
            return license == null ? NotFound(new { message = $"Licence {id} was not found" }) : Ok(license);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get licence {Id}", id);
            return StatusCode(500, new { message = "An error occurred while retrieving the licence" });
        }
    }

    [HttpPost("licenses")]
    public async Task<IActionResult> IssueLicense([FromBody] IssueLicenseRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        try
        {
            return Ok(await _licensingService.IssueAsync(request, CurrentUser));
        }
        catch (LicensingException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to issue a licence");
            return StatusCode(500, new { message = "An error occurred while issuing the licence" });
        }
    }

    /// <summary>The .lic file to hand to the partner with the installer.</summary>
    [HttpGet("licenses/{id}/file")]
    public async Task<IActionResult> GetLicenseFile(int id)
    {
        try
        {
            return Ok(await _licensingService.GetLicenseFileAsync(id));
        }
        catch (LicensingException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to build the licence file for {Id}", id);
            return StatusCode(500, new { message = "An error occurred while building the licence file" });
        }
    }

    [HttpPost("licenses/{id}/renew")]
    public async Task<IActionResult> RenewLicense(int id, [FromBody] RenewLicenseRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        try
        {
            return Ok(await _licensingService.RenewAsync(id, request.ExpiresAt, CurrentUser));
        }
        catch (LicensingException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to renew licence {Id}", id);
            return StatusCode(500, new { message = "An error occurred while renewing the licence" });
        }
    }

    /// <summary>Checks an activation request file and reports what approving it would do. Changes nothing.</summary>
    [HttpPost("activations/review")]
    public async Task<IActionResult> ReviewActivation([FromBody] ActivationFileRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        try
        {
            return Ok(await _licensingService.ReviewActivationAsync(request.RequestFile));
        }
        catch (LicensingException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to review an activation request");
            return StatusCode(500, new { message = "An error occurred while reading the activation request" });
        }
    }

    /// <summary>Binds the licence to the requesting machine and returns the response file to send back.</summary>
    [HttpPost("activations/approve")]
    public async Task<IActionResult> ApproveActivation([FromBody] ActivationFileRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        try
        {
            return Ok(await _licensingService.ApproveActivationAsync(request.RequestFile, CurrentUser));
        }
        catch (LicensingException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to approve an activation request");
            return StatusCode(500, new { message = "An error occurred while approving the activation" });
        }
    }

    /// <summary>Releases the current binding so another server can activate this licence.</summary>
    [HttpPost("licenses/{id}/transfer")]
    public async Task<IActionResult> TransferLicense(int id, [FromBody] LicenseActionRequest request)
    {
        try
        {
            return Ok(await _licensingService.TransferAsync(id, request?.Reason, CurrentUser));
        }
        catch (LicensingException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to transfer licence {Id}", id);
            return StatusCode(500, new { message = "An error occurred while transferring the licence" });
        }
    }

    [HttpPost("licenses/{id}/revoke")]
    public async Task<IActionResult> RevokeLicense(int id, [FromBody] LicenseActionRequest request)
    {
        try
        {
            return Ok(await _licensingService.RevokeAsync(id, request?.Reason, CurrentUser));
        }
        catch (LicensingException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to revoke licence {Id}", id);
            return StatusCode(500, new { message = "An error occurred while revoking the licence" });
        }
    }
}
