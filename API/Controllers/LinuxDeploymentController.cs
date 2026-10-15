using System.Security.Claims;
using API.DTOs.Requests;
using API.Models.Enums;
using API.Services;
using API.Services.Implementations;
using API.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

/// <summary>
/// Linux deployments of Azure DevOps builds. Package builds and deployments run in the background: follow them with
/// GET api/apideployments/progress/{id} and GET api/apideployments/operations/{id} (and .../download for kits).
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class LinuxDeploymentController : ControllerBase
{
    private readonly ILinuxDeploymentService _linuxDeploymentService;
    private readonly IBackgroundOperations _operations;
    private readonly ILogger<LinuxDeploymentController> _logger;

    public LinuxDeploymentController(
        ILinuxDeploymentService linuxDeploymentService,
        IBackgroundOperations operations,
        ILogger<LinuxDeploymentController> logger)
    {
        _linuxDeploymentService = linuxDeploymentService;
        _operations = operations;
        _logger = logger;
    }

    private int CurrentUserId()
    {
        foreach (var claim in User.Claims.Where(c => c.Type == ClaimTypes.NameIdentifier))
            if (int.TryParse(claim.Value, out var id)) return id;
        return 0;
    }

    /// <summary>Latest backend and frontend builds of an application and the Linux artifact each would use.</summary>
    [HttpGet("builds")]
    public async Task<IActionResult> GetBuilds([FromQuery] int applicationId, [FromQuery] ArtifactEnvironment environment)
    {
        try
        {
            return Ok(await _linuxDeploymentService.GetBuildsAsync(applicationId, environment, HttpContext.RequestAborted));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>Logs in over SSH and reports the OS, sudo and installed components. Credentials are not stored.</summary>
    [HttpPost("test-connection")]
    public async Task<IActionResult> TestConnection([FromBody] LinuxSshTarget target)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        return Ok(await _linuxDeploymentService.TestConnectionAsync(target, HttpContext.RequestAborted));
    }

    /// <summary>Starts building the .tar.gz kits of a manual Linux deployment (202).</summary>
    [HttpPost("package/start")]
    public IActionResult StartPackage([FromBody] LinuxPackageRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var userId = CurrentUserId();
        if (userId == 0) return Unauthorized(new { error = "Invalid user token - user ID not found" });
        if (!DeploymentProgressTracker.IsValidId(request.ProgressId))
            return BadRequest(new { error = "A progressId (8-64 letters, digits or dashes) is required." });

        _logger.LogInformation("Starting Linux package build ({Target}) of application {ApplicationId} ({Environment}) by user {UserId}",
            request.Target, request.ApplicationId, request.Environment, userId);

        var operationId = request.ProgressId!;
        var started = _operations.Start(operationId, userId, "package", async (services, cancellationToken) =>
        {
            var package = await services.GetRequiredService<ILinuxDeploymentService>().BuildPackageAsync(request, cancellationToken);
            foreach (var file in package.Files)
                _operations.AttachFile(operationId, file.Path, file.FileName, file.Tier);
            return new { package.FileName, Files = package.Files.Select(f => f.FileName).ToList() };
        });

        return started
            ? Accepted(new { operationId })
            : Conflict(new { error = "An operation with this ID is already running." });
    }

    /// <summary>
    /// Starts a one-click Linux deployment (202): uploads the kits over SFTP and runs Deploy.sh on each server.
    /// The SSH credentials stay in memory for the operation only.
    /// </summary>
    [HttpPost("deploy/start")]
    public IActionResult StartDeployment([FromBody] LinuxDeployRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var userId = CurrentUserId();
        if (userId == 0) return Unauthorized(new { error = "Invalid user token - user ID not found" });
        if (!DeploymentProgressTracker.IsValidId(request.ProgressId))
            return BadRequest(new { error = "A progressId (8-64 letters, digits or dashes) is required." });

        _logger.LogInformation("Starting Linux deployment ({Target}) of application {ApplicationId} ({Environment}) by user {UserId}",
            request.Target, request.ApplicationId, request.Environment, userId);

        var started = _operations.Start(request.ProgressId!, userId, "deployment", async (services, cancellationToken) =>
            await services.GetRequiredService<ILinuxDeploymentService>().DeployAsync(request, userId, cancellationToken));

        return started
            ? Accepted(new { operationId = request.ProgressId })
            : Conflict(new { error = "An operation with this ID is already running." });
    }
}
