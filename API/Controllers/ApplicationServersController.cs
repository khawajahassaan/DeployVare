using API.DTOs.Requests;
using API.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ApplicationServersController : ControllerBase
{
    private readonly IApplicationServerService _serverService;
    private readonly ILogger<ApplicationServersController> _logger;

    public ApplicationServersController(
        IApplicationServerService serverService,
        ILogger<ApplicationServersController> logger)
    {
        _serverService = serverService;
        _logger = logger;
    }

    /// <summary>
    /// Get all application servers (optionally filtered by client)
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int? clientId = null)
    {
        try
        {
            var servers = await _serverService.GetAllAsync(clientId);
            return Ok(servers);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving all servers");
            return StatusCode(500, new { error = "An error occurred while retrieving servers" });
        }
    }

    /// <summary>
    /// Get servers by application ID
    /// </summary>
    [HttpGet("application/{applicationId}")]
    public async Task<IActionResult> GetByApplicationId(int applicationId)
    {
        try
        {
            var servers = await _serverService.GetByApplicationIdAsync(applicationId);
            return Ok(servers);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error retrieving servers for application {applicationId}");
            return StatusCode(500, new { error = "An error occurred while retrieving servers" });
        }
    }

    /// <summary>
    /// Get server by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        try
        {
            var server = await _serverService.GetByIdAsync(id);
            if (server == null)
            {
                return NotFound(new { error = "Server not found" });
            }

            return Ok(server);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error retrieving server {id}");
            return StatusCode(500, new { error = "An error occurred while retrieving the server" });
        }
    }

    /// <summary>
    /// Create a new application server
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create([FromBody] CreateServerRequest request)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var server = await _serverService.CreateAsync(request);
            if (server == null)
            {
                return BadRequest(new { error = "Failed to create server. Check if application exists and server name is unique." });
            }

            return CreatedAtAction(nameof(GetById), new { id = server.Id }, server);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating server");
            return StatusCode(500, new { error = "An error occurred while creating the server" });
        }
    }

    /// <summary>
    /// Update an existing server
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateServerRequest request)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var server = await _serverService.UpdateAsync(id, request);
            if (server == null)
            {
                return NotFound(new { error = "Server not found" });
            }

            return Ok(server);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error updating server {id}");
            return StatusCode(500, new { error = "An error occurred while updating the server" });
        }
    }

    /// <summary>
    /// Delete a server
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            var success = await _serverService.DeleteAsync(id);
            if (!success)
            {
                return NotFound(new { error = "Server not found" });
            }

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error deleting server {id}");
            return StatusCode(500, new { error = "An error occurred while deleting the server" });
        }
    }

    /// <summary>
    /// Test SSH connection to a server
    /// </summary>
    [HttpPost("{id}/test-connection")]
    public async Task<IActionResult> TestConnection(int id)
    {
        try
        {
            var (success, message) = await _serverService.TestConnectionAsync(id);

            return success
                ? Ok(new { message })
                : BadRequest(new { error = message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error testing connection to server {id}");
            return StatusCode(500, new { error = "An error occurred while testing the connection" });
        }
    }
}



