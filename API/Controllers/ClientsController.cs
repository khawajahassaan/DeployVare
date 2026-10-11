using API.DTOs.Requests;
using API.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ClientsController : ControllerBase
{
    private readonly IClientService _clientService;
    private readonly ILogger<ClientsController> _logger;

    public ClientsController(IClientService clientService, ILogger<ClientsController> logger)
    {
        _clientService = clientService;
        _logger = logger;
    }

    /// <summary>
    /// Get all clients
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        try
        {
            var clients = await _clientService.GetAllClientsAsync();
            return Ok(clients);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get clients");
            return StatusCode(500, new { message = "An error occurred while retrieving clients" });
        }
    }

    /// <summary>
    /// Get client by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        try
        {
            var client = await _clientService.GetClientByIdAsync(id);

            if (client == null)
                return NotFound(new { message = $"Client with ID {id} not found" });

            return Ok(client);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get client {Id}", id);
            return StatusCode(500, new { message = "An error occurred while retrieving the client" });
        }
    }

    /// <summary>
    /// Onboard a new client (license key is generated automatically)
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create([FromBody] CreateClientRequest request)
    {
        try
        {
            var username = User.Identity?.Name ?? "Unknown";
            var client = await _clientService.CreateClientAsync(request, username);
            return CreatedAtAction(nameof(GetById), new { id = client.Id }, client);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create client");
            return StatusCode(500, new { message = "An error occurred while creating the client" });
        }
    }

    /// <summary>
    /// Update a client
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateClientRequest request)
    {
        try
        {
            var client = await _clientService.UpdateClientAsync(id, request);

            if (client == null)
                return NotFound(new { message = $"Client with ID {id} not found" });

            return Ok(client);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update client {Id}", id);
            return StatusCode(500, new { message = "An error occurred while updating the client" });
        }
    }

    /// <summary>
    /// Generate a new license key for a client
    /// </summary>
    [HttpPost("{id}/regenerate-license")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> RegenerateLicense(int id)
    {
        try
        {
            var client = await _clientService.RegenerateLicenseKeyAsync(id);

            if (client == null)
                return NotFound(new { message = $"Client with ID {id} not found" });

            return Ok(client);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to regenerate license key for client {Id}", id);
            return StatusCode(500, new { message = "An error occurred while regenerating the license key" });
        }
    }

    /// <summary>
    /// Delete a client
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            var deleted = await _clientService.DeleteClientAsync(id);

            if (!deleted)
                return NotFound(new { message = $"Client with ID {id} not found" });

            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete client {Id}", id);
            return StatusCode(500, new { message = "An error occurred while deleting the client" });
        }
    }
}
