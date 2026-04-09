using API.Data;
using API.DTOs.Requests;
using API.DTOs.Responses;
using API.Models.Entities;
using API.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace API.Services.Implementations;

public class DatabaseInstanceService : IDatabaseInstanceService
{
    private readonly DeployVareDbContext _context;
    private readonly IEncryptionService _encryptionService;
    private readonly ISqlExecutionService _sqlExecutionService;
    private readonly ILogger<DatabaseInstanceService> _logger;

    public DatabaseInstanceService(
        DeployVareDbContext context,
        IEncryptionService encryptionService,
        ISqlExecutionService sqlExecutionService,
        ILogger<DatabaseInstanceService> logger)
    {
        _context = context;
        _encryptionService = encryptionService;
        _sqlExecutionService = sqlExecutionService;
        _logger = logger;
    }

    public async Task<List<DatabaseInstanceDto>> GetAllInstancesAsync()
    {
        var instances = await _context.DatabaseInstances
            .Include(d => d.Application)
            .OrderBy(d => d.Application.Name)
            .ThenBy(d => d.InstanceName)
            .ToListAsync();

        return instances.Select(MapToDto).ToList();
    }

    public async Task<List<DatabaseInstanceDto>> GetInstancesByApplicationIdAsync(int applicationId)
    {
        var instances = await _context.DatabaseInstances
            .Include(d => d.Application)
            .Where(d => d.ApplicationId == applicationId)
            .OrderBy(d => d.InstanceName)
            .ToListAsync();

        return instances.Select(MapToDto).ToList();
    }

    public async Task<DatabaseInstanceDto?> GetInstanceByIdAsync(int id)
    {
        var instance = await _context.DatabaseInstances
            .Include(d => d.Application)
            .FirstOrDefaultAsync(d => d.Id == id);

        return instance == null ? null : MapToDto(instance);
    }

    public async Task<DatabaseInstanceDto> CreateInstanceAsync(CreateDatabaseInstanceRequest request, string createdBy)
    {
        // Check if application exists
        var application = await _context.Applications.FindAsync(request.ApplicationId);
        if (application == null)
        {
            throw new InvalidOperationException($"Application with ID {request.ApplicationId} not found");
        }

        // Check if instance name is unique within application
        if (await _context.DatabaseInstances.AnyAsync(d => 
            d.ApplicationId == request.ApplicationId && d.InstanceName == request.InstanceName))
        {
            throw new InvalidOperationException(
                $"Instance '{request.InstanceName}' already exists for application '{application.Name}'");
        }

        // Test connection before saving
        if (!await _sqlExecutionService.TestConnectionAsync(request.ConnectionString))
        {
            throw new InvalidOperationException("Failed to connect to the database. Please verify the connection string.");
        }

        // Encrypt connection string
        var encryptedConnectionString = _encryptionService.Encrypt(request.ConnectionString);

        var instance = new DatabaseInstance
        {
            ApplicationId = request.ApplicationId,
            InstanceName = request.InstanceName,
            ConnectionStringEncrypted = encryptedConnectionString,
            CurrentPatchNumber = request.CurrentPatchNumber,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = createdBy
        };

        _context.DatabaseInstances.Add(instance);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Database instance '{Instance}' created for application '{Application}' by {User}",
            request.InstanceName, application.Name, createdBy);

        instance.Application = application;
        return MapToDto(instance);
    }

    public async Task<DatabaseInstanceDto?> UpdateInstanceAsync(int id, UpdateDatabaseInstanceRequest request, string updatedBy)
    {
        var instance = await _context.DatabaseInstances
            .Include(d => d.Application)
            .FirstOrDefaultAsync(d => d.Id == id);

        if (instance == null)
            return null;

        // Update fields if provided
        if (!string.IsNullOrWhiteSpace(request.InstanceName))
        {
            // Check uniqueness
            if (await _context.DatabaseInstances.AnyAsync(d => 
                d.ApplicationId == instance.ApplicationId && 
                d.InstanceName == request.InstanceName && 
                d.Id != id))
            {
                throw new InvalidOperationException($"Instance name '{request.InstanceName}' already exists");
            }
            instance.InstanceName = request.InstanceName;
        }

        if (!string.IsNullOrWhiteSpace(request.ConnectionString))
        {
            // Test new connection
            if (!await _sqlExecutionService.TestConnectionAsync(request.ConnectionString))
            {
                throw new InvalidOperationException("Failed to connect with the new connection string");
            }
            instance.ConnectionStringEncrypted = _encryptionService.Encrypt(request.ConnectionString);
        }

        if (request.CurrentPatchNumber.HasValue)
        {
            instance.CurrentPatchNumber = request.CurrentPatchNumber.Value;
        }

        if (request.IsActive.HasValue)
        {
            instance.IsActive = request.IsActive.Value;
        }

        instance.UpdatedAt = DateTime.UtcNow;
        instance.UpdatedBy = updatedBy;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Database instance {Id} updated by {User}", id, updatedBy);

        return MapToDto(instance);
    }

    public async Task<bool> DeleteInstanceAsync(int id)
    {
        var instance = await _context.DatabaseInstances
            .Include(d => d.Deployments)
            .FirstOrDefaultAsync(d => d.Id == id);

        if (instance == null)
            return false;

        if (instance.Deployments.Any())
        {
            throw new InvalidOperationException("Cannot delete database instance with existing deployments");
        }

        _context.DatabaseInstances.Remove(instance);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Database instance {Id} deleted", id);
        return true;
    }

    public async Task<bool> TestConnectionAsync(int id)
    {
        var instance = await _context.DatabaseInstances.FindAsync(id);
        if (instance == null)
            return false;

        var connectionString = _encryptionService.Decrypt(instance.ConnectionStringEncrypted);
        return await _sqlExecutionService.TestConnectionAsync(connectionString);
    }

    private DatabaseInstanceDto MapToDto(DatabaseInstance instance)
    {
        return new DatabaseInstanceDto
        {
            Id = instance.Id,
            ApplicationId = instance.ApplicationId,
            ApplicationName = instance.Application.Name,
            InstanceName = instance.InstanceName,
            CurrentPatchNumber = instance.CurrentPatchNumber,
            IsActive = instance.IsActive,
            CreatedAt = instance.CreatedAt,
            CreatedBy = instance.CreatedBy,
            UpdatedAt = instance.UpdatedAt,
            UpdatedBy = instance.UpdatedBy
        };
    }
}




