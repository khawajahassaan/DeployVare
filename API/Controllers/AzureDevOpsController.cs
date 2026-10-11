using API.DTOs.Requests;
using API.Models.Enums;
using API.Services;
using API.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AzureDevOpsController : ControllerBase
{
    private readonly IAzureDevOpsConfigService _configService;
    private readonly IAzureDevOpsService _azureDevOpsService;
    private readonly ILogger<AzureDevOpsController> _logger;

    public AzureDevOpsController(
        IAzureDevOpsConfigService configService,
        IAzureDevOpsService azureDevOpsService,
        ILogger<AzureDevOpsController> logger)
    {
        _configService = configService;
        _azureDevOpsService = azureDevOpsService;
        _logger = logger;
    }

    /// <summary>
    /// Get the Azure DevOps connection for a client (the token is never returned)
    /// </summary>
    [HttpGet("connection")]
    public async Task<IActionResult> GetConnection([FromQuery] int clientId)
    {
        try
        {
            return Ok(await _configService.GetConnectionAsync(clientId));
        }
        catch (Exception ex)
        {
            return HandleError(ex, "retrieving the Azure DevOps connection");
        }
    }

    /// <summary>
    /// Create or update the Azure DevOps connection for a client
    /// </summary>
    [HttpPut("connection")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> SaveConnection([FromBody] SaveAzureDevOpsConnectionRequest request)
    {
        try
        {
            var username = User.Identity?.Name ?? "Unknown";
            return Ok(await _configService.SaveConnectionAsync(request, username));
        }
        catch (Exception ex)
        {
            return HandleError(ex, "saving the Azure DevOps connection");
        }
    }

    /// <summary>
    /// Delete the Azure DevOps connection for a client
    /// </summary>
    [HttpDelete("connection/{clientId}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteConnection(int clientId)
    {
        try
        {
            var deleted = await _configService.DeleteConnectionAsync(clientId);
            return deleted ? NoContent() : NotFound(new { message = "Azure DevOps connection not found" });
        }
        catch (Exception ex)
        {
            return HandleError(ex, "deleting the Azure DevOps connection");
        }
    }

    /// <summary>
    /// Test Azure DevOps authentication and project access (unsaved values fall back to the saved connection)
    /// </summary>
    [HttpPost("connection/test")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> TestConnection([FromBody] TestAzureDevOpsConnectionRequest request)
    {
        try
        {
            return Ok(await _configService.TestConnectionAsync(request, HttpContext.RequestAborted));
        }
        catch (Exception ex)
        {
            return HandleError(ex, "testing the Azure DevOps connection");
        }
    }

    /// <summary>
    /// Get artifact sources for all applications of a client
    /// </summary>
    [HttpGet("artifact-sources")]
    public async Task<IActionResult> GetArtifactSources([FromQuery] int clientId)
    {
        try
        {
            return Ok(await _configService.GetArtifactSourcesAsync(clientId));
        }
        catch (Exception ex)
        {
            return HandleError(ex, "retrieving artifact sources");
        }
    }

    /// <summary>
    /// Create an artifact source
    /// </summary>
    [HttpPost("artifact-sources")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> CreateArtifactSource([FromBody] SaveArtifactSourceRequest request)
    {
        try
        {
            var username = User.Identity?.Name ?? "Unknown";
            return Ok(await _configService.CreateArtifactSourceAsync(request, username));
        }
        catch (Exception ex)
        {
            return HandleError(ex, "creating the artifact source");
        }
    }

    /// <summary>
    /// Update an artifact source
    /// </summary>
    [HttpPut("artifact-sources/{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> UpdateArtifactSource(int id, [FromBody] SaveArtifactSourceRequest request)
    {
        try
        {
            var username = User.Identity?.Name ?? "Unknown";
            var source = await _configService.UpdateArtifactSourceAsync(id, request, username);
            return source == null ? NotFound(new { message = "Artifact source not found" }) : Ok(source);
        }
        catch (Exception ex)
        {
            return HandleError(ex, "updating the artifact source");
        }
    }

    /// <summary>
    /// Delete an artifact source
    /// </summary>
    [HttpDelete("artifact-sources/{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteArtifactSource(int id)
    {
        try
        {
            var deleted = await _configService.DeleteArtifactSourceAsync(id);
            return deleted ? NoContent() : NotFound(new { message = "Artifact source not found" });
        }
        catch (Exception ex)
        {
            return HandleError(ex, "deleting the artifact source");
        }
    }

    /// <summary>
    /// Verify an artifact source: repository, branch, latest successful build and artifacts
    /// </summary>
    [HttpPost("artifact-sources/{id}/test")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> TestArtifactSource(int id)
    {
        try
        {
            return Ok(await _configService.TestArtifactSourceAsync(id, HttpContext.RequestAborted));
        }
        catch (Exception ex)
        {
            return HandleError(ex, "testing the artifact source");
        }
    }

    /// <summary>
    /// Latest successful build (with artifacts) for a server and environment
    /// </summary>
    [HttpGet("latest-build")]
    public async Task<IActionResult> GetLatestBuild([FromQuery] int applicationServerId, [FromQuery] ArtifactEnvironment environment)
    {
        try
        {
            return Ok(await _configService.GetLatestBuildAsync(applicationServerId, environment, HttpContext.RequestAborted));
        }
        catch (Exception ex)
        {
            return HandleError(ex, "retrieving the latest build");
        }
    }

    /// <summary>
    /// Artifacts of a build, after verifying the build belongs to the server's configured source
    /// </summary>
    [HttpGet("builds/{buildId}/artifacts")]
    public async Task<IActionResult> GetBuildArtifacts(int buildId, [FromQuery] int applicationServerId, [FromQuery] ArtifactEnvironment environment)
    {
        try
        {
            var context = await _configService.ResolveDeploymentContextAsync(applicationServerId, environment, HttpContext.RequestAborted);
            var build = await _azureDevOpsService.GetEligibleBuildAsync(context.Credentials, buildId,
                context.Source.Repository, context.Source.Branch, context.Source.PipelineDefinitionId, HttpContext.RequestAborted);
            return Ok(build.Artifacts);
        }
        catch (Exception ex)
        {
            return HandleError(ex, "retrieving build artifacts");
        }
    }

    private IActionResult HandleError(Exception ex, string action)
    {
        switch (ex)
        {
            case AzureDevOpsException azureEx:
                return StatusCode(azureEx.ToHttpStatusCode(), new { message = azureEx.Message, errorCode = azureEx.ErrorCode.ToString() });
            case InvalidOperationException:
                return BadRequest(new { message = ex.Message });
            case OperationCanceledException when HttpContext.RequestAborted.IsCancellationRequested:
                return StatusCode(499, new { message = "Request was cancelled" });
            default:
                _logger.LogError(ex, "Error while {Action}", action);
                return StatusCode(500, new { message = $"An error occurred while {action}" });
        }
    }
}
