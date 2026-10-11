using API.Data;
using API.DTOs.Requests;
using API.DTOs.Responses;
using API.Models.Entities;
using API.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace API.Services.Implementations;

public class ApplicationService : IApplicationService
{
    private readonly DeployVareDbContext _context;
    private readonly ILogger<ApplicationService> _logger;

    public ApplicationService(DeployVareDbContext context, ILogger<ApplicationService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<List<ApplicationDto>> GetAllApplicationsAsync(int? clientId = null)
    {
        var query = _context.Applications
            .Include(a => a.Client)
            .Include(a => a.DatabaseInstances)
            .AsQueryable();

        if (clientId.HasValue)
            query = query.Where(a => a.ClientId == clientId.Value);

        var applications = await query
            .OrderBy(a => a.Name)
            .ToListAsync();

        return applications.Select(MapToDto).ToList();
    }

    public async Task<ApplicationDto?> GetApplicationByIdAsync(int id)
    {
        var application = await _context.Applications
            .Include(a => a.Client)
            .Include(a => a.DatabaseInstances)
            .FirstOrDefaultAsync(a => a.Id == id);

        return application == null ? null : MapToDto(application);
    }

    public async Task<ApplicationDto> CreateApplicationAsync(CreateApplicationRequest request, string createdBy)
    {
        var client = await _context.Clients.FindAsync(request.ClientId);
        if (client == null)
        {
            throw new InvalidOperationException("Please select a valid client before creating an application");
        }

        // Application names are unique per client
        if (await _context.Applications.AnyAsync(a => a.ClientId == request.ClientId && a.Name == request.Name))
        {
            throw new InvalidOperationException($"Application '{request.Name}' already exists for client '{client.Name}'");
        }

        var application = new Application
        {
            ClientId = request.ClientId,
            Name = request.Name,
            Description = request.Description,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = createdBy
        };

        _context.Applications.Add(application);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Application '{Name}' created for client '{Client}' by {User}", request.Name, client.Name, createdBy);

        application.Client = client;
        return MapToDto(application);
    }

    public async Task<bool> DeleteApplicationAsync(int id)
    {
        var application = await _context.Applications
            .Include(a => a.DatabaseInstances)
            .FirstOrDefaultAsync(a => a.Id == id);

        if (application == null)
            return false;

        if (application.DatabaseInstances.Any())
        {
            throw new InvalidOperationException("Cannot delete application with existing database instances");
        }

        if (await _context.ArtifactSources.AnyAsync(s => s.ApplicationId == id))
        {
            throw new InvalidOperationException("Cannot delete application with Azure DevOps artifact sources. Remove them first.");
        }

        _context.Applications.Remove(application);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Application {Id} deleted", id);
        return true;
    }

    private static ApplicationDto MapToDto(Application application)
    {
        return new ApplicationDto
        {
            Id = application.Id,
            ClientId = application.ClientId,
            ClientName = application.Client.Name,
            Name = application.Name,
            Description = application.Description,
            CreatedAt = application.CreatedAt,
            CreatedBy = application.CreatedBy,
            InstanceCount = application.DatabaseInstances.Count
        };
    }
}
