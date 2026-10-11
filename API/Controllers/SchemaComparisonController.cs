using API.DTOs.Requests;
using API.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SchemaComparisonController : ControllerBase
{
    private readonly ISchemaComparisonService _comparisonService;
    private readonly ILogger<SchemaComparisonController> _logger;

    public SchemaComparisonController(ISchemaComparisonService comparisonService, ILogger<SchemaComparisonController> logger)
    {
        _comparisonService = comparisonService;
        _logger = logger;
    }

    /// <summary>
    /// Compare schemas of two database instances
    /// </summary>
    [HttpPost("compare")]
    public async Task<IActionResult> Compare([FromBody] CompareSchemaRequest request)
    {
        try
        {
            var comparison = await _comparisonService.CompareSchemaAsync(request.SourceInstanceId, request.TargetInstanceId);
            return Ok(comparison);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Schema comparison failed: Source={SourceId}, Target={TargetId}", 
                request.SourceInstanceId, request.TargetInstanceId);
            return StatusCode(500, new { message = $"Schema comparison failed: {ex.Message}" });
        }
    }

    /// <summary>
    /// Get detailed comparison for a specific procedure (lazy loading)
    /// </summary>
    [HttpGet("procedure-detail")]
    public async Task<IActionResult> GetProcedureDetail(
        [FromQuery] int sourceInstanceId,
        [FromQuery] int targetInstanceId,
        [FromQuery] string procedureName,
        [FromQuery] string schema = "dbo")
    {
        try
        {
            var detail = await _comparisonService.GetProcedureDetailAsync(
                sourceInstanceId, targetInstanceId, procedureName, schema);
            return Ok(detail);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get procedure detail: {Schema}.{ProcedureName}", 
                schema, procedureName);
            return StatusCode(500, new { message = $"Failed to get procedure detail: {ex.Message}" });
        }
    }

    /// <summary>
    /// Transfer a database object (procedure/function) between databases
    /// </summary>
    [HttpPost("transfer-object")]
    public async Task<IActionResult> TransferObject([FromBody] TransferObjectRequest request)
    {
        try
        {
            var username = User.Identity?.Name ?? "Unknown";
            var result = await _comparisonService.TransferObjectAsync(
                request.SourceInstanceId,
                request.TargetInstanceId,
                request.ObjectType,
                request.ObjectName,
                request.Schema,
                request.Direction,
                username);

            if (result.Success)
                return Ok(result);
            else
                return BadRequest(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Object transfer failed: {Schema}.{ObjectName}", 
                request.Schema, request.ObjectName);
            return StatusCode(500, new { message = $"Object transfer failed: {ex.Message}" });
        }
    }

    /// <summary>
    /// Compare schemas where either side can be a .dbsnap file or a live database instance.
    /// Send as multipart/form-data.
    /// </summary>
    [HttpPost("compare-with-snap")]
    [RequestFormLimits(MultipartBodyLengthLimit = 104_857_600)] // 100 MB
    public async Task<IActionResult> CompareWithSnap(
        IFormFile? sourceFile,
        IFormFile? targetFile,
        [FromForm] int? sourceInstanceId,
        [FromForm] int? targetInstanceId)
    {
        try
        {
            var hasSourceSnap = sourceFile is not null;
            var hasTargetSnap = targetFile is not null;

            if (!hasSourceSnap && (sourceInstanceId is null or 0))
                return BadRequest(new { message = "Provide either sourceFile or sourceInstanceId." });
            if (!hasTargetSnap && (targetInstanceId is null or 0))
                return BadRequest(new { message = "Provide either targetFile or targetInstanceId." });

            Stream? srcStream = hasSourceSnap ? sourceFile!.OpenReadStream() : null;
            Stream? tgtStream = hasTargetSnap ? targetFile!.OpenReadStream() : null;
            var srcLabel = hasSourceSnap ? sourceFile!.FileName : $"Instance {sourceInstanceId}";
            var tgtLabel = hasTargetSnap ? targetFile!.FileName : $"Instance {targetInstanceId}";

            var result = await _comparisonService.CompareWithSnapAsync(
                srcStream, srcLabel,
                tgtStream, tgtLabel,
                hasSourceSnap ? null : sourceInstanceId,
                hasTargetSnap ? null : targetInstanceId);

            return Ok(result);
        }
        catch (InvalidDataException ex)
        {
            _logger.LogWarning(ex, "Invalid .dbsnap file uploaded");
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "compare-with-snap failed");
            return StatusCode(500, new { message = $"Comparison failed: {ex.Message}" });
        }
    }
}



