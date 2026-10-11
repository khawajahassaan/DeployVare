using System.Text;
using API.Data;
using API.DTOs.Requests;
using API.DTOs.Responses;
using API.Models.Entities;
using API.Models.Enums;
using API.Services.Interfaces;
using API.Utilities;
using Microsoft.EntityFrameworkCore;

namespace API.Services.Implementations;

public class FrontendRuntimeConfigService : IFrontendRuntimeConfigService
{
    private readonly DeployVareDbContext _context;
    private readonly IIisManagementService _iisService;
    private readonly ILogger<FrontendRuntimeConfigService> _logger;

    public FrontendRuntimeConfigService(
        DeployVareDbContext context,
        IIisManagementService iisService,
        ILogger<FrontendRuntimeConfigService> logger)
    {
        _context = context;
        _iisService = iisService;
        _logger = logger;
    }

    public async Task<FrontendRuntimeConfigDto> GetAsync(int serverId)
    {
        var server = await _context.ApplicationServers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == serverId)
            ?? throw new KeyNotFoundException("Application server not found");

        return MapToDto(server);
    }

    public async Task<FrontendRuntimeConfigDto> SaveAsync(int serverId, SaveFrontendRuntimeConfigRequest request)
    {
        var server = await _context.ApplicationServers.FirstOrDefaultAsync(s => s.Id == serverId)
            ?? throw new KeyNotFoundException("Application server not found");

        if (server.ServerType != ServerType.Frontend)
            throw new InvalidOperationException("Runtime settings apply to frontend servers only.");

        var settings = FrontendRuntimeConfig.Normalize(request.Settings);
        server.RuntimeConfigJson = FrontendRuntimeConfig.Serialize(settings);
        server.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        // Values can include subscription keys, so only the setting names are logged
        _logger.LogInformation("Frontend runtime settings for {ServerName} saved: [{Keys}]",
            server.ServerName, string.Join(", ", settings.Select(s => s.Key)));

        return MapToDto(server);
    }

    public async Task<ServerSetupActionResultDto> ApplyToServerAsync(int serverId)
    {
        var server = await _context.ApplicationServers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == serverId)
            ?? throw new KeyNotFoundException("Application server not found");

        if (server.ServerType != ServerType.Frontend)
            throw new InvalidOperationException("Runtime settings apply to frontend servers only.");

        var result = await WriteToServerAsync(server);
        if (result == null)
            return new ServerSetupActionResultDto
            {
                Success = false,
                Message = "Save at least one runtime setting first.",
                Steps = { "✗ Save at least one runtime setting first." }
            };

        var (success, message) = result.Value;
        return new ServerSetupActionResultDto
        {
            Success = success,
            Message = message,
            Steps = { success ? $"✓ {message}" : $"✗ {message}" }
        };
    }

    public async Task<(bool Success, string Message)?> WriteToServerAsync(ApplicationServer server)
    {
        var settings = FrontendRuntimeConfig.Parse(server.RuntimeConfigJson);
        if (settings.Count == 0)
            return null;

        var localFile = Path.Combine(Path.GetTempPath(), "DeployVare", "runtime-config", $"{Guid.NewGuid():N}.js");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(localFile)!);
            await File.WriteAllTextAsync(localFile,
                FrontendRuntimeConfig.BuildConfigJs(server.ServerName, settings, DateTime.UtcNow), new UTF8Encoding(false));

            var (success, message) = await _iisService.WriteFrontendRuntimeConfigAsync(server, localFile);

            if (success)
            {
                _logger.LogInformation("config.js written to {ServerName} with [{Keys}]",
                    server.ServerName, string.Join(", ", settings.Select(s => s.Key)));
                return (true, $"config.js written with {settings.Count} runtime setting(s)");
            }

            _logger.LogWarning("Writing config.js to {ServerName} failed: {Message}", server.ServerName, message);
            return (false, message);
        }
        finally
        {
            try { if (File.Exists(localFile)) File.Delete(localFile); } catch { /* best effort */ }
        }
    }

    private static FrontendRuntimeConfigDto MapToDto(ApplicationServer server)
    {
        var settings = FrontendRuntimeConfig.Parse(server.RuntimeConfigJson);
        return new FrontendRuntimeConfigDto
        {
            ServerId = server.Id,
            ServerType = server.ServerType.ToString(),
            Settings = settings,
            StandardKeys = FrontendRuntimeConfig.StandardKeys.ToList(),
            SuggestedKeys = FrontendRuntimeConfig.SuggestedKeys.ToList(),
            ConfigJsPreview = settings.Count == 0
                ? null
                : FrontendRuntimeConfig.BuildConfigJs(server.ServerName, settings, DateTime.UtcNow)
        };
    }
}
