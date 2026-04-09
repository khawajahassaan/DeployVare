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

    public async Task<List<ApplicationDto>> GetAllApplicationsAsync()
    {
        var applications = await _context.Applications
            .Include(a => a.DatabaseInstances)
            .OrderBy(a => a.Name)
            .ToListAsync();

        return applications.Select(a => new ApplicationDto
        {
            Id = a.Id,
            Name = a.Name,
            Description = a.Description,
            CreatedAt = a.CreatedAt,
            CreatedBy = a.CreatedBy,
            InstanceCount = a.DatabaseInstances.Count
        }).ToList();
    }

    public async Task<ApplicationDto?> GetApplicationByIdAsync(int id)
    {
        var application = await _context.Applications
            .Include(a => a.DatabaseInstances)
            .FirstOrDefaultAsync(a => a.Id == id);

        if (application == null)
            return null;

        return new ApplicationDto
        {
            Id = application.Id,
            Name = application.Name,
            Description = application.Description,
            CreatedAt = application.CreatedAt,
            CreatedBy = application.CreatedBy,
            InstanceCount = application.DatabaseInstances.Count
        };
    }

    public async Task<ApplicationDto> CreateApplicationAsync(CreateApplicationRequest request, string createdBy)
    {
        // Check if application already exists
        if (await _context.Applications.AnyAsync(a => a.Name == request.Name))
        {
            throw new InvalidOperationException($"Application '{request.Name}' already exists");
        }

        var application = new Application
        {
            Name = request.Name,
            Description = request.Description,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = createdBy
        };

        _context.Applications.Add(application);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Application '{Name}' created by {User}", request.Name, createdBy);

        return new ApplicationDto
        {
            Id = application.Id,
            Name = application.Name,
            Description = application.Description,
            CreatedAt = application.CreatedAt,
            CreatedBy = application.CreatedBy,
            InstanceCount = 0
        };
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

        _context.Applications.Remove(application);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Application {Id} deleted", id);
        return true;
    }
}




