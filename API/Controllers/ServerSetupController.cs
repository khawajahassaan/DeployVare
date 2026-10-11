using API.DTOs.Requests;
using API.Services;
using API.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ServerSetupController : ControllerBase
{
    private readonly IServerReadinessService _readinessService;
    private readonly ISqlServerSetupService _sqlServerSetupService;
    private readonly IFrontendRuntimeConfigService _runtimeConfigService;
    private readonly ILogger<ServerSetupController> _logger;

    public ServerSetupController(
        IServerReadinessService readinessService,
        ISqlServerSetupService sqlServerSetupService,
        IFrontendRuntimeConfigService runtimeConfigService,
        ILogger<ServerSetupController> logger)
    {
        _readinessService = readinessService;
        _sqlServerSetupService = sqlServerSetupService;
        _runtimeConfigService = runtimeConfigService;
        _logger = logger;
    }

    /// <summary>
    /// Frontend runtime settings (config.js) for a server
    /// </summary>
    [HttpGet("{serverId}/runtime-config")]
    public Task<IActionResult> GetRuntimeConfig(int serverId) =>
        Execute(async () => Ok(await _runtimeConfigService.GetAsync(serverId)), "loading runtime settings");

    /// <summary>
    /// Save frontend runtime settings; they are written to config.js on every deploy
    /// </summary>
    [HttpPut("{serverId}/runtime-config")]
    [Authorize(Roles = "Admin")]
    public Task<IActionResult> SaveRuntimeConfig(int serverId, [FromBody] SaveFrontendRuntimeConfigRequest request) =>
        Execute(async () => Ok(await _runtimeConfigService.SaveAsync(serverId, request)), "saving runtime settings");

    /// <summary>
    /// Write config.js with the saved runtime settings to the server now
    /// </summary>
    [HttpPost("{serverId}/runtime-config/apply")]
    [Authorize(Roles = "Admin")]
    public Task<IActionResult> ApplyRuntimeConfig(int serverId) =>
        Execute(async () => Ok(await _runtimeConfigService.ApplyToServerAsync(serverId)), "writing config.js");

    /// <summary>
    /// Installer files available for server setup
    /// </summary>
    [HttpGet("installers")]
    [Authorize(Roles = "Admin")]
    public IActionResult GetInstallers() => Ok(_readinessService.GetInstallers());

    /// <summary>
    /// Latest saved readiness report for a server (empty when it has never been checked)
    /// </summary>
    [HttpGet("{serverId}/readiness")]
    public Task<IActionResult> GetReadiness(int serverId) =>
        Execute(async () => Ok(await _readinessService.GetLastReportAsync(serverId)), "loading the saved readiness report");

    /// <summary>
    /// Run the readiness checklist on a server (IIS, site, runtimes, Redis, SSL, bindings, database) and save it
    /// </summary>
    [HttpPost("{serverId}/readiness")]
    public Task<IActionResult> RunReadinessCheck(int serverId) =>
        Execute(async () => Ok(await _readinessService.CheckAsync(serverId, User.Identity?.Name ?? "Unknown")),
            "checking server readiness");

    /// <summary>
    /// Install IIS on a server
    /// </summary>
    [HttpPost("{serverId}/install-iis")]
    [Authorize(Roles = "Admin")]
    public Task<IActionResult> InstallIis(int serverId) =>
        Execute(async () => Ok(await _readinessService.InstallIisAsync(serverId)), "installing IIS");

    /// <summary>
    /// Install a component (.NET Hosting Bundle, .NET SDK, Redis, URL Rewrite) from the Installers folder
    /// </summary>
    [HttpPost("{serverId}/install-component")]
    [Authorize(Roles = "Admin")]
    public Task<IActionResult> InstallComponent(int serverId, [FromBody] InstallComponentRequest request) =>
        Execute(async () => Ok(await _readinessService.InstallComponentAsync(serverId, request.Component)), "installing the component");

    /// <summary>
    /// Create the IIS site and application pool for a server
    /// </summary>
    [HttpPost("{serverId}/create-site")]
    [Authorize(Roles = "Admin")]
    public Task<IActionResult> CreateSite(int serverId, [FromBody] CreateSiteRequest request) =>
        Execute(async () => Ok(await _readinessService.CreateSiteAsync(serverId, request)), "creating the site");

    /// <summary>
    /// Import a .pfx certificate into LocalMachine\My on a server
    /// </summary>
    [HttpPost("{serverId}/certificates")]
    [Authorize(Roles = "Admin")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(11_000_000)]
    public Task<IActionResult> InstallCertificate(int serverId, [FromForm] InstallCertificateRequest request) =>
        Execute(async () => Ok(await _readinessService.InstallCertificateAsync(serverId, request.CertificateFile, request.Password)),
            "installing the certificate");

    /// <summary>
    /// Add http (80) and https (443) bindings with a certificate
    /// </summary>
    [HttpPost("{serverId}/bindings")]
    [Authorize(Roles = "Admin")]
    public Task<IActionResult> ConfigureBindings(int serverId, [FromBody] ConfigureBindingsRequest request) =>
        Execute(async () => Ok(await _readinessService.ConfigureBindingsAsync(serverId, request)), "configuring bindings");

    /// <summary>
    /// Use the IIS site's physical folder as the server's deployment path
    /// </summary>
    [HttpPost("{serverId}/sync-deployment-path")]
    [Authorize(Roles = "Admin")]
    public Task<IActionResult> SyncDeploymentPath(int serverId) =>
        Execute(async () => Ok(await _readinessService.SyncDeploymentPathAsync(serverId)), "updating the deployment path");

    /// <summary>
    /// Link the database instance this server's API uses
    /// </summary>
    [HttpPost("{serverId}/database-instance")]
    [Authorize(Roles = "Admin")]
    public Task<IActionResult> LinkDatabaseInstance(int serverId, [FromBody] LinkDatabaseInstanceRequest request) =>
        Execute(async () => Ok(await _readinessService.LinkDatabaseInstanceAsync(serverId, request.DatabaseInstanceId)),
            "linking the database instance");

    /// <summary>
    /// Create the SQL login and database user for the app pool identity and grant db_owner
    /// </summary>
    [HttpPost("{serverId}/grant-database-access")]
    [Authorize(Roles = "Admin")]
    public Task<IActionResult> GrantDatabaseAccess(int serverId) =>
        Execute(async () => Ok(await _readinessService.GrantDatabaseAccessAsync(serverId)), "granting database access");

    /// <summary>
    /// Check whether SQL Server is installed on the server; with connection details, also connects and lists databases
    /// </summary>
    [HttpPost("{serverId}/sql-server/check")]
    [Authorize(Roles = "Admin")]
    public Task<IActionResult> CheckSqlServer(int serverId, [FromBody] SqlServerConnectRequest? request) =>
        Execute(async () => Ok(await _sqlServerSetupService.GetSqlServerInfoAsync(serverId, request)), "checking SQL Server");

    /// <summary>
    /// Users of a database with their login, login state and roles
    /// </summary>
    [HttpPost("{serverId}/sql-server/database-users")]
    [Authorize(Roles = "Admin")]
    public Task<IActionResult> GetSqlDatabaseUsers(int serverId, [FromBody] SqlDatabaseUsersRequest request) =>
        Execute(async () => Ok(await _sqlServerSetupService.GetDatabaseUsersAsync(serverId, request)), "reading database users");

    /// <summary>
    /// Create (or reset) a SQL login with db_owner on a database. The password is never stored.
    /// </summary>
    [HttpPost("{serverId}/sql-server/logins")]
    [Authorize(Roles = "Admin")]
    public Task<IActionResult> CreateSqlLogin(int serverId, [FromBody] CreateSqlLoginRequest request) =>
        Execute(async () => Ok(await _sqlServerSetupService.CreateSqlLoginAsync(serverId, request)), "creating the SQL login");

    /// <summary>
    /// Enable SQL Server and Windows authentication on an instance (restarts SQL Server)
    /// </summary>
    [HttpPost("{serverId}/sql-server/enable-sql-authentication")]
    [Authorize(Roles = "Admin")]
    public Task<IActionResult> EnableSqlAuthentication(int serverId, [FromBody] EnableSqlAuthenticationRequest request) =>
        Execute(async () => Ok(await _sqlServerSetupService.EnableSqlAuthenticationAsync(serverId, request.InstanceName)),
            "enabling SQL authentication");

    /// <summary>
    /// Install SQL Server Express silently from the Installers folder (mixed mode, TCP on 1433)
    /// </summary>
    [HttpPost("{serverId}/sql-server/install")]
    [Authorize(Roles = "Admin")]
    public Task<IActionResult> InstallSqlServer(int serverId, [FromBody] InstallSqlServerRequest request) =>
        Execute(async () => Ok(await _sqlServerSetupService.InstallSqlServerAsync(serverId, request)), "installing SQL Server");

    private async Task<IActionResult> Execute(Func<Task<IActionResult>> action, string description)
    {
        try
        {
            return await action();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (ServerSetupException ex)
        {
            _logger.LogWarning(ex, "Server setup failed while {Action}", description);
            return StatusCode(502, new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while {Action}", description);
            return StatusCode(500, new { message = $"An error occurred while {description}" });
        }
    }
}
