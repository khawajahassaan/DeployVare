using API.DTOs.Requests;
using API.Services;
using API.Services.Implementations;
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
    private readonly IDeploymentProgressTracker _progressTracker;
    private readonly IManualPackageService _manualPackageService;
    private readonly IBackgroundOperations _operations;
    private readonly ILogger<ApiDeploymentsController> _logger;

    public ApiDeploymentsController(
        IApiDeploymentService deploymentService,
        IDeploymentProgressTracker progressTracker,
        IManualPackageService manualPackageService,
        IBackgroundOperations operations,
        ILogger<ApiDeploymentsController> logger)
    {
        _deploymentService = deploymentService;
        _progressTracker = progressTracker;
        _manualPackageService = manualPackageService;
        _operations = operations;
        _logger = logger;
    }

    private int CurrentUserId()
    {
        foreach (var claim in User.Claims.Where(c => c.Type == ClaimTypes.NameIdentifier))
            if (int.TryParse(claim.Value, out var id)) return id;
        return 0;
    }

    /// <summary>
    /// Starts an Azure DevOps deployment in the background and returns at once (202). The page follows it with
    /// GET progress/{operationId} and reads the result from GET operations/{operationId}. Unlike POST azure-artifact,
    /// a proxy timeout or a closed tab cannot cancel the deployment half-way.
    /// </summary>
    [HttpPost("azure-artifact/start")]
    public IActionResult StartAzureArtifactDeployment([FromBody] DeployAzureArtifactRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var userId = CurrentUserId();
        if (userId == 0) return Unauthorized(new { error = "Invalid user token - user ID not found" });
        if (!DeploymentProgressTracker.IsValidId(request.ProgressId))
            return BadRequest(new { error = "A progressId (8-64 letters, digits or dashes) is required." });

        _logger.LogInformation("Starting background Azure DevOps deployment of build {BuildId} ({Environment}) to server {ServerId} by user {UserId}",
            request.BuildId, request.Environment, request.ApplicationServerId, userId);

        var started = _operations.Start(request.ProgressId!, userId, "deployment", async (services, cancellationToken) =>
            await services.GetRequiredService<IApiDeploymentService>().DeployFromAzureArtifactAsync(request, userId, cancellationToken));

        return started
            ? Accepted(new { operationId = request.ProgressId })
            : Conflict(new { error = "An operation with this ID is already running." });
    }

    /// <summary>
    /// Starts building a manual-deployment package in the background (202). When GET operations/{operationId} reports
    /// it succeeded, the zip downloads from GET operations/{operationId}/download?token=... with a plain link, so it
    /// goes straight to disk instead of browser memory.
    /// </summary>
    [HttpPost("azure-artifact/package/start")]
    [RequestSizeLimit(12_000_000)]
    [RequestFormLimits(MultipartBodyLengthLimit = 12_000_000)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> StartAzureArtifactPackage([FromForm] DownloadArtifactPackageRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var userId = CurrentUserId();
        if (userId == 0) return Unauthorized(new { error = "Invalid user token - user ID not found" });
        if (!DeploymentProgressTracker.IsValidId(request.ProgressId))
            return BadRequest(new { error = "A progressId (8-64 letters, digits or dashes) is required." });

        // The upload is gone once this request ends, so the certificate is saved now
        if (request.CertificateMode == Models.Enums.CertificateMode.UserProvided)
        {
            try
            {
                request.CertificateTempPath = await ManualPackageService.SaveCertificateFileAsync(request.CertificateFile, HttpContext.RequestAborted);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }
        request.CertificateFile = null;

        var operationId = request.ProgressId!;
        var started = _operations.Start(operationId, userId, "package", async (services, cancellationToken) =>
        {
            var package = await services.GetRequiredService<IManualPackageService>().BuildAsync(request, cancellationToken);
            _operations.AttachFile(operationId, package.ZipPath, package.FileName);
            return new { package.FileName, package.AppFileCount, package.SqlFileCount, package.ContentFolders };
        });

        return started
            ? Accepted(new { operationId })
            : Conflict(new { error = "An operation with this ID is already running." });
    }

    /// <summary>Status and result of a background deployment or package build started by this user.</summary>
    [HttpGet("operations/{operationId}")]
    public IActionResult GetOperation(string operationId)
    {
        var operation = _operations.Get(operationId, CurrentUserId());
        return operation == null ? NotFound(new { error = "Unknown operation." }) : Ok(operation);
    }

    /// <summary>
    /// The finished package. Anonymous on purpose (a plain browser download carries no login header); the random
    /// token from GET operations/{operationId} is the key, and the file expires with the operation. Supports
    /// resuming (HTTP range requests).
    /// </summary>
    [AllowAnonymous]
    [HttpGet("operations/{operationId}/download")]
    public IActionResult DownloadOperationFile(string operationId, [FromQuery] string? token)
    {
        var file = _operations.GetFile(operationId, token);
        if (file == null) return NotFound(new { error = "This download link is invalid or has expired. Build the package again." });
        return PhysicalFile(file.Path, "application/zip", file.FileName, enableRangeProcessing: true);
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
    /// Deploy a frontend ZIP to a server
    /// </summary>
    [HttpPost("frontend")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> DeployFrontend([FromForm] DeployFrontendRequest request)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (request.ZipFile == null || request.ZipFile.Length == 0)
            {
                return BadRequest(new { error = "No ZIP file provided" });
            }

            if (!request.ZipFile.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { error = "Only .zip files are accepted for frontend deployments" });
            }

            var nameIdentifierClaims = User.Claims
                .Where(c => c.Type == ClaimTypes.NameIdentifier ||
                           c.Type == "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")
                .ToList();

            int userId = 0;
            foreach (var claim in nameIdentifierClaims)
            {
                if (int.TryParse(claim.Value, out userId))
                {
                    break;
                }
            }

            if (userId == 0)
            {
                return Unauthorized(new { error = "Invalid user token - user ID not found" });
            }

            _logger.LogInformation($"Starting frontend deployment for server {request.ApplicationServerId} by user {userId}");

            var result = await _deploymentService.DeployFrontendAsync(request, userId);

            return result.Status == Models.Enums.ApiDeploymentStatus.Success
                ? Ok(result)
                : StatusCode(500, result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during frontend deployment");
            return StatusCode(500, new { error = "An error occurred during frontend deployment", details = ex.Message });
        }
    }

    /// <summary>
    /// Deploy a build artifact from Azure DevOps (Dev/UAT) to a server
    /// </summary>
    [HttpPost("azure-artifact")]
    public async Task<IActionResult> DeployAzureArtifact([FromBody] DeployAzureArtifactRequest request)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var nameIdentifierClaims = User.Claims
                .Where(c => c.Type == ClaimTypes.NameIdentifier ||
                           c.Type == "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")
                .ToList();

            int userId = 0;
            foreach (var claim in nameIdentifierClaims)
            {
                if (int.TryParse(claim.Value, out userId))
                {
                    break;
                }
            }

            if (userId == 0)
            {
                return Unauthorized(new { error = "Invalid user token - user ID not found" });
            }

            _logger.LogInformation("Starting Azure DevOps artifact deployment of build {BuildId} ({Environment}) to server {ServerId} by user {UserId}",
                request.BuildId, request.Environment, request.ApplicationServerId, userId);

            var result = await _deploymentService.DeployFromAzureArtifactAsync(request, userId, HttpContext.RequestAborted);

            return result.Status == Models.Enums.ApiDeploymentStatus.Success
                ? Ok(result)
                : StatusCode(500, result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during Azure DevOps artifact deployment");
            return StatusCode(500, new { error = "An error occurred during deployment", details = ex.Message });
        }
    }

    /// <summary>
    /// Downloads a complete offline deployment package for the selected server: app files, installers,
    /// Deploy.ps1 and (optionally) a baseline database. Nothing is sent to the server.
    /// </summary>
    [HttpPost("azure-artifact/package")]
    [RequestSizeLimit(12_000_000)]
    [RequestFormLimits(MultipartBodyLengthLimit = 12_000_000)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> DownloadAzureArtifactPackage([FromForm] DownloadArtifactPackageRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        try
        {
            var package = await _manualPackageService.BuildAsync(request, HttpContext.RequestAborted);

            // Delete the file when the response has been sent, and the temporary folder afterwards
            var workspace = Path.GetDirectoryName(package.ZipPath)!;
            Response.OnCompleted(() =>
            {
                try { if (Directory.Exists(workspace)) Directory.Delete(workspace, true); }
                catch (Exception ex) { _logger.LogWarning(ex, "Could not delete package workspace {Workspace}", workspace); }
                return Task.CompletedTask;
            });

            var stream = new FileStream(package.ZipPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 64 * 1024, FileOptions.Asynchronous | FileOptions.DeleteOnClose);

            return File(stream, "application/zip", package.FileName);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("Manual package refused: {Message}", ex.Message);
            return BadRequest(new { error = ex.Message });
        }
        catch (Services.AzureDevOpsException ex)
        {
            _logger.LogWarning("Manual package failed: {ErrorCode} {Message}", ex.ErrorCode, ex.Message);
            return BadRequest(new { error = ex.Message });
        }
        catch (Services.Interfaces.LicensingException ex)
        {
            // The operator's to fix (wrong client, expired, revoked), so it is a bad request rather than a fault
            _logger.LogWarning("Manual package licensing failed: {Message}", ex.Message);
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error building the manual deployment package");
            return StatusCode(500, new { error = "An error occurred while building the package", details = ex.Message });
        }
    }

    /// <summary>
    /// Live progress of an Azure DevOps deployment started with this progress ID (204 until tracking has started)
    /// </summary>
    [HttpGet("progress/{progressId}")]
    public IActionResult GetDeploymentProgress(string progressId)
    {
        var progress = _progressTracker.Get(progressId);
        return progress == null ? NoContent() : Ok(progress);
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


