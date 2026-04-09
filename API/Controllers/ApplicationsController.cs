using API.DTOs.Requests;
using API.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ApplicationsController : ControllerBase
{
    private readonly IApplicationService _applicationService;
    private readonly ILogger<ApplicationsController> _logger;

    public ApplicationsController(IApplicationService applicationService, ILogger<ApplicationsController> logger)
    {
        _applicationService = applicationService;
        _logger = logger;
    }

    /// <summary>
    /// Get all applications
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        try
        {
            var applications = await _applicationService.GetAllApplicationsAsync();
            return Ok(applications);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get applications");
            return StatusCode(500, new { message = "An error occurred while retrieving applications" });
        }
    }

    /// <summary>
    /// Get application by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        try
        {
            var application = await _applicationService.GetApplicationByIdAsync(id);
            
            if (application == null)
                return NotFound(new { message = $"Application with ID {id} not found" });

            return Ok(application);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get application {Id}", id);
            return StatusCode(500, new { message = "An error occurred while retrieving the application" });
        }
    }

    /// <summary>
    /// Create a new application
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create([FromBody] CreateApplicationRequest request)
    {
        try
        {
            var username = User.Identity?.Name ?? "Unknown";
            var application = await _applicationService.CreateApplicationAsync(request, username);
            return CreatedAtAction(nameof(GetById), new { id = application.Id }, application);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create application");
            return StatusCode(500, new { message = "An error occurred while creating the application" });
        }
    }

    /// <summary>
    /// Delete an application
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            var deleted = await _applicationService.DeleteApplicationAsync(id);
            
            if (!deleted)
                return NotFound(new { message = $"Application with ID {id} not found" });

            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete application {Id}", id);
            return StatusCode(500, new { message = "An error occurred while deleting the application" });
        }
    }
}




