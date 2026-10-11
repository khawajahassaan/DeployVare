using API.Data;
using API.DTOs.Requests;
using API.DTOs.Responses;
using API.Models.Entities;
using API.Models.Enums;
using API.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace API.Services.Implementations;

public class ApplicationServerService : IApplicationServerService
{
    private readonly DeployVareDbContext _context;
    private readonly IEncryptionService _encryptionService;
    private readonly ISshService _sshService;
    private readonly ILogger<ApplicationServerService> _logger;

    public ApplicationServerService(
        DeployVareDbContext context,
        IEncryptionService encryptionService,
        ISshService sshService,
        ILogger<ApplicationServerService> logger)
    {
        _context = context;
        _encryptionService = encryptionService;
        _sshService = sshService;
        _logger = logger;
    }

    public async Task<ApplicationServerDto?> CreateAsync(CreateServerRequest request)
    {
        try
        {
            // Check if application exists
            var applicationExists = await _context.Applications.AnyAsync(a => a.Id == request.ApplicationId);
            if (!applicationExists)
            {
                _logger.LogWarning($"Application {request.ApplicationId} not found");
                return null;
            }

            // Check for duplicate server name
            var duplicateExists = await _context.ApplicationServers
                .AnyAsync(s => s.ApplicationId == request.ApplicationId && s.ServerName == request.ServerName);
            
            if (duplicateExists)
            {
                _logger.LogWarning($"Server with name '{request.ServerName}' already exists for this application");
                return null;
            }

            // Encrypt password if provided
            string? encryptedPassword = null;
            if (!string.IsNullOrEmpty(request.Password))
            {
                encryptedPassword = _encryptionService.Encrypt(request.Password);
            }

            var server = new ApplicationServer
            {
                ApplicationId = request.ApplicationId,
                ServerName = request.ServerName,
                HostName = request.HostName,
                Port = request.Port,
                Username = request.Username,
                EncryptedPassword = encryptedPassword,
                SshPrivateKey = request.SshPrivateKey,
                DeploymentPath = request.DeploymentPath,
                AppPoolName = request.AppPoolName,
                WebsiteName = request.WebsiteName,
                BackupPath = request.BackupPath,
                ServerType = request.ServerType,
                IsActive = true
            };

            _context.ApplicationServers.Add(server);
            await _context.SaveChangesAsync();

            _logger.LogInformation($"Created server {server.ServerName} (ID: {server.Id})");

            return await GetByIdAsync(server.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating application server");
            return null;
        }
    }

    public async Task<ApplicationServerDto?> GetByIdAsync(int id)
    {
        var server = await _context.ApplicationServers
            .Include(s => s.Application)
            .Include(s => s.ApiDeployments)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (server == null) return null;

        return new ApplicationServerDto
        {
            Id = server.Id,
            ApplicationId = server.ApplicationId,
            ApplicationName = server.Application.Name,
            ServerName = server.ServerName,
            HostName = server.HostName,
            Port = server.Port,
            Username = server.Username,
            DeploymentPath = server.DeploymentPath,
            AppPoolName = server.AppPoolName,
            WebsiteName = server.WebsiteName,
            BackupPath = server.BackupPath,
            ServerType = server.ServerType.ToString(),
            IsActive = server.IsActive,
            CreatedAt = server.CreatedAt,
            UpdatedAt = server.UpdatedAt,
            TotalDeployments = server.ApiDeployments.Count,
            LastDeploymentDate = server.ApiDeployments.OrderByDescending(d => d.DeploymentDate).FirstOrDefault()?.DeploymentDate
        };
    }

    public async Task<List<ApplicationServerDto>> GetByApplicationIdAsync(int applicationId)
    {
        var servers = await _context.ApplicationServers
            .Include(s => s.Application)
            .Include(s => s.ApiDeployments)
            .Where(s => s.ApplicationId == applicationId)
            .ToListAsync();

        return servers.Select(server => new ApplicationServerDto
        {
            Id = server.Id,
            ApplicationId = server.ApplicationId,
            ApplicationName = server.Application.Name,
            ServerName = server.ServerName,
            HostName = server.HostName,
            Port = server.Port,
            Username = server.Username,
            DeploymentPath = server.DeploymentPath,
            AppPoolName = server.AppPoolName,
            WebsiteName = server.WebsiteName,
            BackupPath = server.BackupPath,
            ServerType = server.ServerType.ToString(),
            IsActive = server.IsActive,
            CreatedAt = server.CreatedAt,
            UpdatedAt = server.UpdatedAt,
            TotalDeployments = server.ApiDeployments.Count,
            LastDeploymentDate = server.ApiDeployments.OrderByDescending(d => d.DeploymentDate).FirstOrDefault()?.DeploymentDate
        }).ToList();
    }

    public async Task<List<ApplicationServerDto>> GetAllAsync(int? clientId = null)
    {
        var query = _context.ApplicationServers
            .Include(s => s.Application)
            .Include(s => s.ApiDeployments)
            .AsQueryable();

        if (clientId.HasValue)
            query = query.Where(s => s.Application.ClientId == clientId.Value);

        var servers = await query
            .OrderBy(s => s.Application.Name)
            .ThenBy(s => s.ServerName)
            .ToListAsync();

        return servers.Select(server => new ApplicationServerDto
        {
            Id = server.Id,
            ApplicationId = server.ApplicationId,
            ApplicationName = server.Application.Name,
            ServerName = server.ServerName,
            HostName = server.HostName,
            Port = server.Port,
            Username = server.Username,
            DeploymentPath = server.DeploymentPath,
            AppPoolName = server.AppPoolName,
            WebsiteName = server.WebsiteName,
            BackupPath = server.BackupPath,
            ServerType = server.ServerType.ToString(),
            IsActive = server.IsActive,
            CreatedAt = server.CreatedAt,
            UpdatedAt = server.UpdatedAt,
            TotalDeployments = server.ApiDeployments.Count,
            LastDeploymentDate = server.ApiDeployments.OrderByDescending(d => d.DeploymentDate).FirstOrDefault()?.DeploymentDate
        }).ToList();
    }

    public async Task<ApplicationServerDto?> UpdateAsync(int id, UpdateServerRequest request)
    {
        var server = await _context.ApplicationServers.FindAsync(id);
        if (server == null) return null;

        // Update properties if provided
        if (!string.IsNullOrEmpty(request.ServerName))
            server.ServerName = request.ServerName;
        
        if (!string.IsNullOrEmpty(request.HostName))
            server.HostName = request.HostName;
        
        if (request.Port.HasValue)
            server.Port = request.Port.Value;
        
        if (!string.IsNullOrEmpty(request.Username))
            server.Username = request.Username;
        
        if (!string.IsNullOrEmpty(request.Password))
            server.EncryptedPassword = _encryptionService.Encrypt(request.Password);
        
        if (request.SshPrivateKey != null)
            server.SshPrivateKey = request.SshPrivateKey;
        
        if (!string.IsNullOrEmpty(request.DeploymentPath))
            server.DeploymentPath = request.DeploymentPath;
        
        if (!string.IsNullOrEmpty(request.AppPoolName))
            server.AppPoolName = request.AppPoolName;
        
        if (!string.IsNullOrEmpty(request.WebsiteName))
            server.WebsiteName = request.WebsiteName;
        
        if (!string.IsNullOrEmpty(request.BackupPath))
            server.BackupPath = request.BackupPath;
        
        if (request.IsActive.HasValue)
            server.IsActive = request.IsActive.Value;

        if (request.ServerType.HasValue)
            server.ServerType = request.ServerType.Value;

        server.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        _logger.LogInformation($"Updated server {server.ServerName} (ID: {server.Id})");

        return await GetByIdAsync(id);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var server = await _context.ApplicationServers.FindAsync(id);
        if (server == null) return false;

        _context.ApplicationServers.Remove(server);
        await _context.SaveChangesAsync();

        _logger.LogInformation($"Deleted server {server.ServerName} (ID: {id})");
        return true;
    }

    public async Task<(bool Success, string Message)> TestConnectionAsync(int id)
    {
        var server = await _context.ApplicationServers.FindAsync(id);
        if (server == null) return (false, "Server not found");

        string? password = null;
        if (!string.IsNullOrEmpty(server.EncryptedPassword))
        {
            password = _encryptionService.Decrypt(server.EncryptedPassword);
        }

        return await _sshService.TestConnectionWithReasonAsync(
            server.HostName,
            server.Port,
            server.Username,
            password,
            server.SshPrivateKey);
    }
}



