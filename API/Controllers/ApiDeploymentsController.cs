using API.DTOs.Requests;
using API.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ApiDeploymentsController : ControllerBase
{
    private readonly IApiDeploymentService _deploymentService;
    private readonly ILogger<ApiDeploymentsController> _logger;

    public ApiDeploymentsController(
        IApiDeploymentService deploymentService,
        ILogger<ApiDeploymentsController> logger)
    {
        _deploymentService = deploymentService;
        _logger = logger;
    }

    /// <summary>
    /// Deploy API files to a server
    /// </summary>
    [HttpPost]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Deploy([FromForm] DeployApiRequest request)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (request.Files == null || request.Files.Count == 0)
            {
                return BadRequest(new { error = "No files provided" });
            }

            // Get user ID from claims
            // There might be multiple NameIdentifier claims due to JWT mapping
            // Find the one that's actually a valid integer (the user ID)
            var nameIdentifierClaims = User.Claims
                .Where(c => c.Type == ClaimTypes.NameIdentifier || 
                           c.Type == "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")
                .ToList();
            
            int userId = 0;
            foreach (var claim in nameIdentifierClaims)
            {
                if (int.TryParse(claim.Value, out userId))
                {
                    break; // Found the numeric user ID
                }
            }
            
            if (userId == 0)
            {
                var allClaims = string.Join(", ", User.Claims.Select(c => $"{c.Type}={c.Value}"));
                _logger.LogError("No valid integer user ID claim found. Claims: {Claims}", allClaims);
                return Unauthorized(new { error = "Invalid user token - user ID not found" });
            }

            _logger.LogInformation($"Starting API deployment for server {request.ApplicationServerId} by user {userId}");

            var result = await _deploymentService.DeployAsync(request, userId);

            if (result.Status == Models.Enums.ApiDeploymentStatus.Success)
            {
                return Ok(result);
            }
            else
            {
                return StatusCode(500, result);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during API deployment");
            return StatusCode(500, new { error = "An error occurred during deployment", details = ex.Message });
        }
    }

    /// <summary>
    /// Get deployment history for a server
    /// </summary>
    [HttpGet("server/{applicationServerId}")]
    public async Task<IActionResult> GetDeploymentHistory(int applicationServerId, [FromQuery] int limit = 50)
    {
        try
        {
            var deployments = await _deploymentService.GetDeploymentHistoryAsync(applicationServerId, limit);
            return Ok(deployments);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error retrieving deployment history for server {applicationServerId}");
            return StatusCode(500, new { error = "An error occurred while retrieving deployment history" });
        }
    }

    /// <summary>
    /// Get a specific deployment by ID
    /// </summary>
    [HttpGet("{deploymentId}")]
    public async Task<IActionResult> GetDeploymentById(int deploymentId)
    {
        try
        {
            var deployment = await _deploymentService.GetDeploymentByIdAsync(deploymentId);
            if (deployment == null)
            {
                return NotFound(new { error = "Deployment not found" });
            }

            return Ok(deployment);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error retrieving deployment {deploymentId}");
            return StatusCode(500, new { error = "An error occurred while retrieving the deployment" });
        }
    }

    /// <summary>
    /// Download backup file for a deployment
    /// </summary>
    [HttpGet("{deploymentId}/backup")]
    public async Task<IActionResult> DownloadBackup(int deploymentId)
    {
        try
        {
            var (success, filePath, message) = await _deploymentService.DownloadBackupAsync(deploymentId);

            if (!success || string.IsNullOrEmpty(filePath))
            {
                return NotFound(new { error = message });
            }

            if (!System.IO.File.Exists(filePath))
            {
                return NotFound(new { error = "Backup file not found on server" });
            }

            var fileName = Path.GetFileName(filePath);
            var fileBytes = await System.IO.File.ReadAllBytesAsync(filePath);

            return File(fileBytes, "application/zip", fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error downloading backup for deployment {deploymentId}");
            return StatusCode(500, new { error = "An error occurred while downloading the backup" });
        }
    }
}


