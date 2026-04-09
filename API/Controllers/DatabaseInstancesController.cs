using API.DTOs.Requests;
using API.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DatabaseInstancesController : ControllerBase
{
    private readonly IDatabaseInstanceService _instanceService;
    private readonly ILogger<DatabaseInstancesController> _logger;

    public DatabaseInstancesController(IDatabaseInstanceService instanceService, ILogger<DatabaseInstancesController> logger)
    {
        _instanceService = instanceService;
        _logger = logger;
    }

    /// <summary>
    /// Get all database instances
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        try
        {
            var instances = await _instanceService.GetAllInstancesAsync();
            return Ok(instances);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get database instances");
            return StatusCode(500, new { message = "An error occurred while retrieving database instances" });
        }
    }

    /// <summary>
    /// Get database instances by application ID
    /// </summary>
    [HttpGet("by-application/{applicationId}")]
    public async Task<IActionResult> GetByApplicationId(int applicationId)
    {
        try
        {
            var instances = await _instanceService.GetInstancesByApplicationIdAsync(applicationId);
            return Ok(instances);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get database instances for application {ApplicationId}", applicationId);
            return StatusCode(500, new { message = "An error occurred while retrieving database instances" });
        }
    }

    /// <summary>
    /// Get database instance by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        try
        {
            var instance = await _instanceService.GetInstanceByIdAsync(id);
            
            if (instance == null)
                return NotFound(new { message = $"Database instance with ID {id} not found" });

            return Ok(instance);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get database instance {Id}", id);
            return StatusCode(500, new { message = "An error occurred while retrieving the database instance" });
        }
    }

    /// <summary>
    /// Create a new database instance
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create([FromBody] CreateDatabaseInstanceRequest request)
    {
        try
        {
            var username = User.Identity?.Name ?? "Unknown";
            var instance = await _instanceService.CreateInstanceAsync(request, username);
            return CreatedAtAction(nameof(GetById), new { id = instance.Id }, instance);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create database instance");
            return StatusCode(500, new { message = "An error occurred while creating the database instance" });
        }
    }

    /// <summary>
    /// Update a database instance
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateDatabaseInstanceRequest request)
    {
        try
        {
            var username = User.Identity?.Name ?? "Unknown";
            var instance = await _instanceService.UpdateInstanceAsync(id, request, username);
            
            if (instance == null)
                return NotFound(new { message = $"Database instance with ID {id} not found" });

            return Ok(instance);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update database instance {Id}", id);
            return StatusCode(500, new { message = "An error occurred while updating the database instance" });
        }
    }

    /// <summary>
    /// Delete a database instance
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            var deleted = await _instanceService.DeleteInstanceAsync(id);
            
            if (!deleted)
                return NotFound(new { message = $"Database instance with ID {id} not found" });

            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete database instance {Id}", id);
            return StatusCode(500, new { message = "An error occurred while deleting the database instance" });
        }
    }

    /// <summary>
    /// Test database connection
    /// </summary>
    [HttpPost("{id}/test-connection")]
    public async Task<IActionResult> TestConnection(int id)
    {
        try
        {
            var success = await _instanceService.TestConnectionAsync(id);
            
            return Ok(new
            {
                success,
                message = success ? "Connection successful" : "Connection failed"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to test connection for database instance {Id}", id);
            return StatusCode(500, new { message = "An error occurred while testing the connection" });
        }
    }
}




