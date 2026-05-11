using API.Data;
using API.DTOs.Requests;
using API.DTOs.Responses;
using API.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DeploymentsController : ControllerBase
{
    private readonly IDeploymentService _deploymentService;
    private readonly IBackupService _backupService;
    private readonly DeployVareDbContext _context;
    private readonly ILogger<DeploymentsController> _logger;
    private readonly string _uploadPath;
    private readonly IFileStorageService _storage;

    public DeploymentsController(
        IDeploymentService deploymentService,
        IBackupService backupService,
        DeployVareDbContext context,
        IConfiguration configuration,
        ILogger<DeploymentsController> logger,
        IFileStorageService storage)
    {
        _deploymentService = deploymentService;
        _backupService = backupService;
        _context = context;
        _logger = logger;
        _storage = storage;
        
        var uploadPathConfig = configuration["FileUploadSettings:UploadPath"] ?? "wwwroot/uploads";
        _uploadPath = Path.Combine(Directory.GetCurrentDirectory(), uploadPathConfig);
    }

    /// <summary>
    /// Get all deployments (with pagination)
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        try
        {
            var query = _context.Deployments
                .Include(d => d.DatabaseInstance)
                    .ThenInclude(di => di.Application)
                .OrderByDescending(d => d.DeployedAt);

            var totalCount = await query.CountAsync();
            var deployments = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var deploymentsDto = deployments.Select(d => new DeploymentDto
            {
                Id = d.Id,
                DatabaseInstanceId = d.DatabaseInstanceId,
                InstanceName = d.DatabaseInstance.InstanceName,
                ApplicationName = d.DatabaseInstance.Application.Name,
                DeployedBy = d.DeployedBy,
                DeployedAt = d.DeployedAt,
                Status = d.Status.ToString(),
                FromPatchNumber = d.FromPatchNumber,
                ToPatchNumber = d.ToPatchNumber,
                BackupFilePath = d.BackupFilePath,
                ErrorMessage = d.ErrorMessage,
                DurationSeconds = d.DurationSeconds
            }).ToList();

            return Ok(new
            {
                data = deploymentsDto,
                page,
                pageSize,
                totalCount,
                totalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get deployments");
            return StatusCode(500, new { message = "An error occurred while retrieving deployments" });
        }
    }

    /// <summary>
    /// Get deployment by ID (with patches and objects)
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        try
        {
            var deployment = await _context.Deployments
                .Include(d => d.DatabaseInstance)
                    .ThenInclude(di => di.Application)
                .Include(d => d.DeploymentPatches)
                .Include(d => d.DeploymentObjects)
                .FirstOrDefaultAsync(d => d.Id == id);

            if (deployment == null)
                return NotFound(new { message = $"Deployment with ID {id} not found" });

            var deploymentDto = new DeploymentDto
            {
                Id = deployment.Id,
                DatabaseInstanceId = deployment.DatabaseInstanceId,
                InstanceName = deployment.DatabaseInstance.InstanceName,
                ApplicationName = deployment.DatabaseInstance.Application.Name,
                DeployedBy = deployment.DeployedBy,
                DeployedAt = deployment.DeployedAt,
                Status = deployment.Status.ToString(),
                FromPatchNumber = deployment.FromPatchNumber,
                ToPatchNumber = deployment.ToPatchNumber,
                BackupFilePath = deployment.BackupFilePath,
                ErrorMessage = deployment.ErrorMessage,
                DurationSeconds = deployment.DurationSeconds,
                Patches = deployment.DeploymentPatches.Select(p => new DeploymentPatchDto
                {
                    PatchNumber = p.PatchNumber,
                    PatchFileName = p.PatchFileName,
                    Description = p.Description,
                    Author = p.Author,
                    ExecutedAt = p.ExecutedAt,
                    Status = p.Status,
                    ErrorMessage = p.ErrorMessage
                }).ToList(),
                Objects = deployment.DeploymentObjects.Select(o => new DeploymentObjectDto
                {
                    ObjectType = o.ObjectType,
                    ObjectName = o.ObjectName,
                    BackedUp = o.BackedUp,
                    ExecutedAt = o.ExecutedAt
                }).ToList()
            };

            return Ok(deploymentDto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get deployment {Id}", id);
            return StatusCode(500, new { message = "An error occurred while retrieving the deployment" });
        }
    }

    /// <summary>
    /// Get deployments by database instance ID
    /// </summary>
    [HttpGet("by-instance/{instanceId}")]
    public async Task<IActionResult> GetByInstanceId(int instanceId)
    {
        try
        {
            var deployments = await _context.Deployments
                .Include(d => d.DatabaseInstance)
                    .ThenInclude(di => di.Application)
                .Where(d => d.DatabaseInstanceId == instanceId)
                .OrderByDescending(d => d.DeployedAt)
                .ToListAsync();

            var deploymentsDto = deployments.Select(d => new DeploymentDto
            {
                Id = d.Id,
                DatabaseInstanceId = d.DatabaseInstanceId,
                InstanceName = d.DatabaseInstance.InstanceName,
                ApplicationName = d.DatabaseInstance.Application.Name,
                DeployedBy = d.DeployedBy,
                DeployedAt = d.DeployedAt,
                Status = d.Status.ToString(),
                FromPatchNumber = d.FromPatchNumber,
                ToPatchNumber = d.ToPatchNumber,
                BackupFilePath = d.BackupFilePath,
                ErrorMessage = d.ErrorMessage,
                DurationSeconds = d.DurationSeconds
            }).ToList();

            return Ok(deploymentsDto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get deployments for instance {InstanceId}", instanceId);
            return StatusCode(500, new { message = "An error occurred while retrieving deployments" });
        }
    }

    /// <summary>
    /// Deploy SQL scripts to a database instance
    /// </summary>
    [HttpPost("deploy")]
    [Authorize(Roles = "Admin")]
    [RequestSizeLimit(100_000_000)] // 100 MB limit
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(DeploymentResultDto), 200)]
    [ProducesResponseType(typeof(object), 400)]
    [ProducesResponseType(typeof(object), 500)]
    public async Task<IActionResult> Deploy([FromForm] DeployRequest request)
    {
        try
        {
            if (request.ZipFile == null || request.ZipFile.Length == 0)
            {
                return BadRequest(new { message = "ZIP file is required" });
            }

            if (!request.ZipFile.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { message = "File must be a ZIP archive" });
            }

            // Save uploaded file
            var fileName = $"{Guid.NewGuid()}_{request.ZipFile.FileName}";
            var filePath = Path.Combine(_uploadPath, fileName);
            
            Directory.CreateDirectory(_uploadPath);
            
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await request.ZipFile.CopyToAsync(stream);
            }

            try
            {
                var username = User.Identity?.Name ?? "Unknown";
                var result = await _deploymentService.DeployAsync(request.DatabaseInstanceId, filePath, username);

                // Always return result directly (consistent structure)
                return Ok(result);
            }
            finally
            {
                // Clean up uploaded file
                if (System.IO.File.Exists(filePath))
                {
                    System.IO.File.Delete(filePath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Deployment failed");
            return StatusCode(500, new { message = $"Deployment failed: {ex.Message}" });
        }
    }

    /// <summary>
    /// Download backup file for a deployment
    /// </summary>
    [HttpGet("{id}/download-backup")]
    public async Task<IActionResult> DownloadBackup(int id)
    {
        try
        {
            var deployment = await _context.Deployments.FindAsync(id);
            
            if (deployment == null)
                return NotFound(new { message = $"Deployment with ID {id} not found" });

            if (string.IsNullOrEmpty(deployment.BackupFilePath))
                return NotFound(new { message = "Backup file not found" });

            var fileName = Path.GetFileName(deployment.BackupFilePath);

            if (_storage.IsCloud)
            {
                var stream = await _storage.DownloadAsync(deployment.BackupFilePath);
                return File(stream, "application/zip", fileName);
            }

            if (!System.IO.File.Exists(deployment.BackupFilePath))
                return NotFound(new { message = "Backup file not found on disk" });

            var fileBytes = await System.IO.File.ReadAllBytesAsync(deployment.BackupFilePath);
            return File(fileBytes, "application/zip", fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download backup for deployment {Id}", id);
            return StatusCode(500, new { message = "An error occurred while downloading the backup" });
        }
    }
}

