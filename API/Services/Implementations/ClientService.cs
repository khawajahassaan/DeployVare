using API.Data;
using API.DTOs.Requests;
using API.DTOs.Responses;
using API.Models.Entities;
using API.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace API.Services.Implementations;

public class ClientService : IClientService
{
    private readonly DeployVareDbContext _context;
    private readonly ILogger<ClientService> _logger;

    public ClientService(DeployVareDbContext context, ILogger<ClientService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<List<ClientDto>> GetAllClientsAsync()
    {
        var clients = await _context.Clients
            .Include(c => c.Applications)
            .OrderBy(c => c.Name)
            .ToListAsync();

        return clients.Select(MapToDto).ToList();
    }

    public async Task<ClientDto?> GetClientByIdAsync(int id)
    {
        var client = await _context.Clients
            .Include(c => c.Applications)
            .FirstOrDefaultAsync(c => c.Id == id);

        return client == null ? null : MapToDto(client);
    }

    public async Task<ClientDto> CreateClientAsync(CreateClientRequest request, string createdBy)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        var clientCode = request.ClientCode?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(name))
            throw new InvalidOperationException("Client name is required");

        if (string.IsNullOrEmpty(clientCode))
            throw new InvalidOperationException("Client ID is required");

        if (await _context.Clients.AnyAsync(c => c.ClientCode == clientCode))
            throw new InvalidOperationException($"Client ID '{clientCode}' already exists");

        var client = new Client
        {
            Name = name,
            ClientCode = clientCode,
            LicenseKey = await GenerateUniqueLicenseKeyAsync(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = createdBy
        };

        _context.Clients.Add(client);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Client '{Name}' ({Code}) onboarded by {User}", name, clientCode, createdBy);

        return MapToDto(client);
    }

    public async Task<ClientDto?> UpdateClientAsync(int id, UpdateClientRequest request)
    {
        var client = await _context.Clients
            .Include(c => c.Applications)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (client == null)
            return null;

        if (!string.IsNullOrWhiteSpace(request.Name))
            client.Name = request.Name.Trim();

        if (!string.IsNullOrWhiteSpace(request.ClientCode))
        {
            var clientCode = request.ClientCode.Trim();
            if (await _context.Clients.AnyAsync(c => c.ClientCode == clientCode && c.Id != id))
                throw new InvalidOperationException($"Client ID '{clientCode}' already exists");

            client.ClientCode = clientCode;
        }

        if (request.IsActive.HasValue)
            client.IsActive = request.IsActive.Value;

        client.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        _logger.LogInformation("Client {Id} updated", id);
        return MapToDto(client);
    }

    public async Task<ClientDto?> RegenerateLicenseKeyAsync(int id)
    {
        var client = await _context.Clients
            .Include(c => c.Applications)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (client == null)
            return null;

        client.LicenseKey = await GenerateUniqueLicenseKeyAsync();
        client.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        _logger.LogInformation("License key regenerated for client {Id}", id);
        return MapToDto(client);
    }

    public async Task<bool> DeleteClientAsync(int id)
    {
        var client = await _context.Clients
            .Include(c => c.Applications)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (client == null)
            return false;

        if (client.Applications.Any())
            throw new InvalidOperationException("Cannot delete client with existing applications");

        _context.Clients.Remove(client);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Client {Id} deleted", id);
        return true;
    }

    /// <summary>
    /// Generates a 32-character GUID-based license key that no other client uses.
    /// </summary>
    private async Task<string> GenerateUniqueLicenseKeyAsync()
    {
        string licenseKey;
        do
        {
            licenseKey = Guid.NewGuid().ToString("N").ToUpperInvariant();
        }
        while (await _context.Clients.AnyAsync(c => c.LicenseKey == licenseKey));

        return licenseKey;
    }

    private static ClientDto MapToDto(Client client)
    {
        return new ClientDto
        {
            Id = client.Id,
            Name = client.Name,
            ClientCode = client.ClientCode,
            LicenseKey = client.LicenseKey,
            IsActive = client.IsActive,
            CreatedAt = client.CreatedAt,
            CreatedBy = client.CreatedBy,
            UpdatedAt = client.UpdatedAt,
            ApplicationCount = client.Applications.Count
        };
    }
}
