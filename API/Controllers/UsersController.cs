using API.DTOs.Requests;
using API.Models.Enums;
using API.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]  // Only admins can manage users
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly ILogger<UsersController> _logger;

    public UsersController(IUserService userService, ILogger<UsersController> logger)
    {
        _userService = userService;
        _logger = logger;
    }

    /// <summary>
    /// Get all users
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        try
        {
            var users = await _userService.GetAllUsersAsync();
            return Ok(users);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get users");
            return StatusCode(500, new { message = "An error occurred while retrieving users" });
        }
    }

    /// <summary>
    /// Get user by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        try
        {
            var user = await _userService.GetUserByIdAsync(id);
            
            if (user == null)
                return NotFound(new { message = $"User with ID {id} not found" });

            return Ok(user);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get user {Id}", id);
            return StatusCode(500, new { message = "An error occurred while retrieving the user" });
        }
    }

    /// <summary>
    /// Create a new user
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest request)
    {
        try
        {
            var createdBy = User.Identity?.Name ?? "Unknown";
            var user = await _userService.CreateUserAsync(request, createdBy);
            return CreatedAtAction(nameof(GetById), new { id = user.Id }, user);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create user");
            return StatusCode(500, new { message = "An error occurred while creating the user" });
        }
    }

    /// <summary>
    /// Update a user
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateUserRequest request)
    {
        try
        {
            var user = await _userService.UpdateUserAsync(id, request);
            
            if (user == null)
                return NotFound(new { message = $"User with ID {id} not found" });

            return Ok(user);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update user {Id}", id);
            return StatusCode(500, new { message = "An error occurred while updating the user" });
        }
    }

    /// <summary>
    /// Delete a user
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            var deleted = await _userService.DeleteUserAsync(id);
            
            if (!deleted)
                return NotFound(new { message = $"User with ID {id} not found" });

            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete user {Id}", id);
            return StatusCode(500, new { message = "An error occurred while deleting the user" });
        }
    }

    /// <summary>
    /// Get user permissions
    /// </summary>
    [HttpGet("{id}/permissions")]
    public async Task<IActionResult> GetPermissions(int id)
    {
        try
        {
            var permissions = await _userService.GetUserPermissionsAsync(id);
            return Ok(new { permissions = permissions.Select(p => p.ToString()).ToList() });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get permissions for user {Id}", id);
            return StatusCode(500, new { message = "An error occurred while retrieving permissions" });
        }
    }

    /// <summary>
    /// Update user permissions
    /// </summary>
    [HttpPut("{id}/permissions")]
    public async Task<IActionResult> UpdatePermissions(int id, [FromBody] UpdateUserPermissionsRequest request)
    {
        try
        {
            var grantedBy = User.Identity?.Name ?? "Unknown";
            await _userService.UpdateUserPermissionsAsync(id, request.Permissions, grantedBy);
            return Ok(new { message = "Permissions updated successfully" });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update permissions for user {Id}", id);
            return StatusCode(500, new { message = "An error occurred while updating permissions" });
        }
    }

    /// <summary>
    /// Get all available permissions (for dropdown/checkboxes)
    /// </summary>
    [HttpGet("available-permissions")]
    public IActionResult GetAvailablePermissions()
    {
        try
        {
            var permissions = Enum.GetValues<Permission>()
                .Select(p => new { 
                    value = p.ToString(),
                    label = SplitCamelCase(p.ToString())
                })
                .ToList();

            return Ok(permissions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get available permissions");
            return StatusCode(500, new { message = "An error occurred while retrieving permissions" });
        }
    }

    private static string SplitCamelCase(string input)
    {
        return System.Text.RegularExpressions.Regex.Replace(input, "([a-z])([A-Z])", "$1 $2");
    }
}

