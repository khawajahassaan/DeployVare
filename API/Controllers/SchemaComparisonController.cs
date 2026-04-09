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
}



