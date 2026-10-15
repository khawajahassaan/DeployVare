using System.Security.Cryptography;
using API.Data;
using API.DTOs.Requests;
using API.DTOs.Responses;
using API.Models;
using API.Models.Entities;
using API.Models.Enums;
using API.Services.Interfaces;
using API.Utilities;
using Microsoft.EntityFrameworkCore;

namespace API.Services.Implementations;

public class AzureDevOpsConfigService : IAzureDevOpsConfigService
{
    private readonly DeployVareDbContext _context;
    private readonly IEncryptionService _encryptionService;
    private readonly IAzureDevOpsService _azureDevOpsService;
    private readonly ILogger<AzureDevOpsConfigService> _logger;

    public AzureDevOpsConfigService(
        DeployVareDbContext context,
        IEncryptionService encryptionService,
        IAzureDevOpsService azureDevOpsService,
        ILogger<AzureDevOpsConfigService> logger)
    {
        _context = context;
        _encryptionService = encryptionService;
        _azureDevOpsService = azureDevOpsService;
        _logger = logger;
    }

    public async Task<AzureDevOpsConnectionDto?> GetConnectionAsync(int clientId)
    {
        var connection = await _context.AzureDevOpsConnections
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.ClientId == clientId);

        return connection == null ? null : MapConnection(connection);
    }

    public async Task<AzureDevOpsConnectionDto> SaveConnectionAsync(SaveAzureDevOpsConnectionRequest request, string username)
    {
        if (!await _context.Clients.AnyAsync(c => c.Id == request.ClientId))
            throw new InvalidOperationException("Client not found");

        var organization = AzureDevOpsCredentials.NormalizeOrganization(request.Organization);
        var project = AzureDevOpsCredentials.ValidateName(request.Project, "Project");
        var hasNewToken = !string.IsNullOrWhiteSpace(request.PersonalAccessToken);

        var connection = await _context.AzureDevOpsConnections.FirstOrDefaultAsync(c => c.ClientId == request.ClientId);
        if (connection == null)
        {
            if (!hasNewToken)
                throw new AzureDevOpsException(AzureDevOpsErrorCode.InvalidConfiguration, "A personal access token is required.");

            connection = new AzureDevOpsConnection
            {
                ClientId = request.ClientId,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = username
            };
            _context.AzureDevOpsConnections.Add(connection);
        }
        else
        {
            connection.UpdatedAt = DateTime.UtcNow;
            connection.UpdatedBy = username;
        }

        if (connection.Organization != organization || connection.Project != project || hasNewToken)
        {
            // A previous test result no longer describes this configuration
            connection.LastTestedAt = null;
            connection.LastTestSucceeded = null;
        }

        connection.Organization = organization;
        connection.Project = project;
        if (hasNewToken)
            connection.EncryptedPersonalAccessToken = _encryptionService.Encrypt(request.PersonalAccessToken!.Trim());

        await _context.SaveChangesAsync();

        _logger.LogInformation("Azure DevOps connection for client {ClientId} saved by {User}: {Organization}/{Project} (token updated: {TokenUpdated})",
            request.ClientId, username, organization, project, hasNewToken);

        return MapConnection(connection);
    }

    public async Task<bool> DeleteConnectionAsync(int clientId)
    {
        var connection = await _context.AzureDevOpsConnections.FirstOrDefaultAsync(c => c.ClientId == clientId);
        if (connection == null)
            return false;

        _context.AzureDevOpsConnections.Remove(connection);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Azure DevOps connection for client {ClientId} deleted", clientId);
        return true;
    }

    public async Task<AzureDevOpsTestResultDto> TestConnectionAsync(TestAzureDevOpsConnectionRequest request, CancellationToken cancellationToken = default)
    {
        var saved = await _context.AzureDevOpsConnections.FirstOrDefaultAsync(c => c.ClientId == request.ClientId, cancellationToken);
        AzureDevOpsCredentials? credentials = null;

        try
        {
            var organization = string.IsNullOrWhiteSpace(request.Organization) ? saved?.Organization : request.Organization;
            var project = string.IsNullOrWhiteSpace(request.Project) ? saved?.Project : request.Project;
            var token = string.IsNullOrWhiteSpace(request.PersonalAccessToken)
                ? (saved == null ? null : DecryptToken(saved))
                : request.PersonalAccessToken;

            credentials = AzureDevOpsCredentials.Create(organization, project, token);
            await _azureDevOpsService.TestConnectionAsync(credentials, cancellationToken);

            await RecordTestResultAsync(saved, request, credentials, succeeded: true);
            return new AzureDevOpsTestResultDto
            {
                Success = true,
                Message = $"Azure DevOps connection successful. Project '{credentials.Project}' in organization '{credentials.Organization}' is accessible."
            };
        }
        catch (AzureDevOpsException ex)
        {
            if (credentials != null)
                await RecordTestResultAsync(saved, request, credentials, succeeded: false);

            return new AzureDevOpsTestResultDto { Success = false, Message = ex.Message };
        }
    }

    public async Task<List<ArtifactSourceDto>> GetArtifactSourcesAsync(int clientId)
    {
        var sources = await _context.ArtifactSources
            .AsNoTracking()
            .Include(s => s.Application)
            .Where(s => s.Application.ClientId == clientId)
            .ToListAsync();

        return sources
            .OrderBy(s => s.Application.Name)
            .ThenBy(s => s.Environment)
            .ThenBy(s => s.ServerType)
            .Select(MapSource)
            .ToList();
    }

    public async Task<ArtifactSourceDto> CreateArtifactSourceAsync(SaveArtifactSourceRequest request, string username)
    {
        var application = await _context.Applications.FirstOrDefaultAsync(a => a.Id == request.ApplicationId)
            ?? throw new InvalidOperationException("Application not found");

        await EnsureUniqueSourceAsync(request, application, excludeId: null);

        var source = new ArtifactSource
        {
            ApplicationId = request.ApplicationId,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = username
        };
        ApplyRequest(source, request);

        _context.ArtifactSources.Add(source);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Artifact source {Id} created for {Application} {Environment}/{ServerType} by {User}",
            source.Id, application.Name, source.Environment, source.ServerType, username);

        source.Application = application;
        return MapSource(source);
    }

    public async Task<ArtifactSourceDto?> UpdateArtifactSourceAsync(int id, SaveArtifactSourceRequest request, string username)
    {
        var source = await _context.ArtifactSources.FirstOrDefaultAsync(s => s.Id == id);
        if (source == null)
            return null;

        var application = await _context.Applications.FirstOrDefaultAsync(a => a.Id == request.ApplicationId)
            ?? throw new InvalidOperationException("Application not found");

        await EnsureUniqueSourceAsync(request, application, excludeId: id);

        source.ApplicationId = request.ApplicationId;
        ApplyRequest(source, request);
        source.UpdatedAt = DateTime.UtcNow;
        source.UpdatedBy = username;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Artifact source {Id} updated by {User}", id, username);

        source.Application = application;
        return MapSource(source);
    }

    public async Task<bool> DeleteArtifactSourceAsync(int id)
    {
        var source = await _context.ArtifactSources.FirstOrDefaultAsync(s => s.Id == id);
        if (source == null)
            return false;

        _context.ArtifactSources.Remove(source);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Artifact source {Id} deleted", id);
        return true;
    }

    public async Task<AzureDevOpsTestResultDto> TestArtifactSourceAsync(int id, CancellationToken cancellationToken = default)
    {
        try
        {
            var source = await _context.ArtifactSources
                .Include(s => s.Application)
                .FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
                ?? throw new AzureDevOpsException(AzureDevOpsErrorCode.InvalidConfiguration, "Artifact source not found.");

            var credentials = await GetClientCredentialsAsync(source.Application.ClientId, cancellationToken);

            await _azureDevOpsService.ValidateRepositoryAsync(credentials, source.Repository, source.Branch, cancellationToken);
            var build = await _azureDevOpsService.GetLatestSuccessfulBuildAsync(credentials, source.Repository, source.Branch,
                source.PipelineDefinitionId, source.ArtifactName, cancellationToken);

            var artifacts = string.Join(", ", build.Artifacts.Select(a => a.Name));
            return new AzureDevOpsTestResultDto
            {
                Success = true,
                Message = $"Repository '{source.Repository}' and branch '{source.Branch}' found. "
                    + $"Latest successful build: #{build.BuildNumber} ({build.PipelineName}). Artifacts: {artifacts}."
            };
        }
        catch (AzureDevOpsException ex)
        {
            return new AzureDevOpsTestResultDto { Success = false, Message = ex.Message };
        }
    }

    public async Task<LatestBuildResultDto> GetLatestBuildAsync(int applicationServerId, ArtifactEnvironment environment,
        CancellationToken cancellationToken = default)
    {
        var context = await ResolveDeploymentContextAsync(applicationServerId, environment, cancellationToken);
        var source = context.Source;

        var build = await _azureDevOpsService.GetLatestSuccessfulBuildAsync(context.Credentials, source.Repository, source.Branch,
            source.PipelineDefinitionId, source.ArtifactName, cancellationToken);

        var downloadable = build.Artifacts.Where(a => a.IsDownloadable).ToList();
        var selected = !string.IsNullOrWhiteSpace(source.ArtifactName)
            ? downloadable.FirstOrDefault(a => a.Name.Equals(source.ArtifactName, StringComparison.OrdinalIgnoreCase))?.Name
            : downloadable.Count == 1 ? downloadable[0].Name : null;

        return new LatestBuildResultDto
        {
            Source = MapSource(source),
            Build = build,
            SelectedArtifactName = selected
        };
    }

    public async Task<ArtifactDeploymentContext> ResolveDeploymentContextAsync(int applicationServerId, ArtifactEnvironment environment,
        CancellationToken cancellationToken = default)
    {
        var server = await _context.ApplicationServers
            .Include(s => s.Application)
            .FirstOrDefaultAsync(s => s.Id == applicationServerId, cancellationToken)
            ?? throw new AzureDevOpsException(AzureDevOpsErrorCode.InvalidConfiguration, "Application server not found.");

        var source = await _context.ArtifactSources
            .Include(s => s.Application)
            .FirstOrDefaultAsync(s => s.ApplicationId == server.ApplicationId
                && s.Environment == environment
                && s.ServerType == server.ServerType, cancellationToken)
            ?? throw new AzureDevOpsException(AzureDevOpsErrorCode.InvalidConfiguration,
                $"No Azure DevOps artifact source is configured for {server.Application.Name} ({environment}, {server.ServerType}). "
                + "Add one on the Azure DevOps page.");

        var credentials = await GetClientCredentialsAsync(server.Application.ClientId, cancellationToken);
        return new ArtifactDeploymentContext(server, source, credentials);
    }

    public async Task<(ArtifactSource Source, AzureDevOpsCredentials Credentials)> ResolveSourceAsync(int applicationId,
        ServerType serverType, ArtifactEnvironment environment, CancellationToken cancellationToken = default)
    {
        var source = await _context.ArtifactSources
            .Include(s => s.Application)
            .FirstOrDefaultAsync(s => s.ApplicationId == applicationId
                && s.Environment == environment
                && s.ServerType == serverType, cancellationToken);
        if (source == null)
        {
            var name = await _context.Applications.Where(a => a.Id == applicationId).Select(a => a.Name)
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new AzureDevOpsException(AzureDevOpsErrorCode.InvalidConfiguration, "Application not found.");
            throw new AzureDevOpsException(AzureDevOpsErrorCode.InvalidConfiguration,
                $"No Azure DevOps artifact source is configured for {name} ({environment}, {serverType}). Add one on the Azure DevOps page.");
        }

        var credentials = await GetClientCredentialsAsync(source.Application.ClientId, cancellationToken);
        return (source, credentials);
    }

    private async Task<AzureDevOpsCredentials> GetClientCredentialsAsync(int clientId, CancellationToken cancellationToken)
    {
        var connection = await _context.AzureDevOpsConnections
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.ClientId == clientId, cancellationToken)
            ?? throw new AzureDevOpsException(AzureDevOpsErrorCode.InvalidConfiguration,
                "No Azure DevOps connection is configured for this client. Add one on the Azure DevOps page.");

        return AzureDevOpsCredentials.Create(connection.Organization, connection.Project, DecryptToken(connection));
    }

    private string DecryptToken(AzureDevOpsConnection connection)
    {
        try
        {
            return _encryptionService.Decrypt(connection.EncryptedPersonalAccessToken);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException)
        {
            _logger.LogError("Stored Azure DevOps token for client {ClientId} could not be decrypted", connection.ClientId);
            throw new AzureDevOpsException(AzureDevOpsErrorCode.InvalidConfiguration,
                "The stored personal access token could not be decrypted. Re-enter the token and save the connection.");
        }
    }

    private async Task RecordTestResultAsync(AzureDevOpsConnection? saved, TestAzureDevOpsConnectionRequest request,
        AzureDevOpsCredentials credentials, bool succeeded)
    {
        // Only a test of the stored configuration says anything about the saved connection
        if (saved == null
            || !string.IsNullOrWhiteSpace(request.PersonalAccessToken)
            || saved.Organization != credentials.Organization
            || saved.Project != credentials.Project)
            return;

        saved.LastTestedAt = DateTime.UtcNow;
        saved.LastTestSucceeded = succeeded;
        await _context.SaveChangesAsync();
    }

    private async Task EnsureUniqueSourceAsync(SaveArtifactSourceRequest request, Application application, int? excludeId)
    {
        var exists = await _context.ArtifactSources.AnyAsync(s =>
            s.ApplicationId == request.ApplicationId
            && s.Environment == request.Environment
            && s.ServerType == request.ServerType
            && (!excludeId.HasValue || s.Id != excludeId.Value));

        if (exists)
            throw new InvalidOperationException(
                $"An artifact source for {application.Name} ({request.Environment}, {request.ServerType}) already exists");
    }

    private static void ApplyRequest(ArtifactSource source, SaveArtifactSourceRequest request)
    {
        if (request.PipelineDefinitionId is <= 0)
            throw new AzureDevOpsException(AzureDevOpsErrorCode.InvalidConfiguration, "Pipeline ID must be a positive number.");

        source.Environment = request.Environment;
        source.ServerType = request.ServerType;
        source.Repository = AzureDevOpsCredentials.ValidateName(request.Repository, "Repository");
        source.Branch = AzureDevOpsCredentials.NormalizeBranch(request.Branch);
        source.PipelineDefinitionId = request.PipelineDefinitionId;
        source.ArtifactName = string.IsNullOrWhiteSpace(request.ArtifactName)
            ? null
            : AzureDevOpsCredentials.ValidateName(request.ArtifactName, "Artifact name");
        source.ArtifactSubfolder = NormalizeSubfolder(request.ArtifactSubfolder);
        source.PreserveWebConfig = request.PreserveWebConfig;
        source.KeepOnServerPaths = KeepOnServerPaths.Normalize(request.KeepOnServerPaths);

        // Database migrations only apply to backend (API) sources
        source.RunDatabaseMigrations = request.ServerType == ServerType.Api && request.RunDatabaseMigrations;
        source.SqlScriptsArtifactName = source.RunDatabaseMigrations
            ? AzureDevOpsCredentials.ValidateName(
                string.IsNullOrWhiteSpace(request.SqlScriptsArtifactName) ? "sqlscripts" : request.SqlScriptsArtifactName,
                "SQL scripts artifact name")
            : null;
    }

    private static string? NormalizeSubfolder(string? subfolder)
    {
        if (string.IsNullOrWhiteSpace(subfolder))
            return null;

        var value = subfolder.Trim().Replace('\\', '/').Trim('/');
        if (value.Length == 0)
            return null;

        if (Path.IsPathRooted(value) || value.Split('/').Any(s => s is ".." or ".") || value.Any(char.IsControl) || value.Contains(':'))
            throw new AzureDevOpsException(AzureDevOpsErrorCode.InvalidConfiguration,
                "Folder inside artifact must be a relative path such as 'AuditVare-WebApi'.");

        return value;
    }

    private static AzureDevOpsConnectionDto MapConnection(AzureDevOpsConnection connection) => new()
    {
        Id = connection.Id,
        ClientId = connection.ClientId,
        Organization = connection.Organization,
        Project = connection.Project,
        HasPersonalAccessToken = !string.IsNullOrEmpty(connection.EncryptedPersonalAccessToken),
        CreatedAt = connection.CreatedAt,
        CreatedBy = connection.CreatedBy,
        UpdatedAt = connection.UpdatedAt,
        UpdatedBy = connection.UpdatedBy,
        LastTestedAt = connection.LastTestedAt,
        LastTestSucceeded = connection.LastTestSucceeded
    };

    private static ArtifactSourceDto MapSource(ArtifactSource source) => new()
    {
        Id = source.Id,
        ApplicationId = source.ApplicationId,
        ApplicationName = source.Application?.Name ?? string.Empty,
        Environment = source.Environment.ToString(),
        ServerType = source.ServerType.ToString(),
        Repository = source.Repository,
        Branch = source.Branch,
        PipelineDefinitionId = source.PipelineDefinitionId,
        ArtifactName = source.ArtifactName,
        ArtifactSubfolder = source.ArtifactSubfolder,
        PreserveWebConfig = source.PreserveWebConfig,
        KeepOnServerPaths = source.KeepOnServerPaths,
        RunDatabaseMigrations = source.RunDatabaseMigrations,
        SqlScriptsArtifactName = source.SqlScriptsArtifactName,
        CreatedAt = source.CreatedAt,
        CreatedBy = source.CreatedBy,
        UpdatedAt = source.UpdatedAt
    };
}
