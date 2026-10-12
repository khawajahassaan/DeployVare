using API.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Serilog;

// One-time SQL Server → PostgreSQL move of an existing database; runs instead of the web host
if (args.Length > 0 && args[0].Equals("copy-to-postgres", StringComparison.OrdinalIgnoreCase))
    return await PostgresCopier.RunAsync(args);

// Disable automatic claim type mapping (prevents "sub" from overriding ClaimTypes.NameIdentifier)
JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();

// Resolve content root to find appsettings.json.
// On macOS .app bundles the sidecar binary lives in Contents/MacOS/ but all bundled
// resources (including appsettings.json) are placed in Contents/Resources/ by Tauri.
// Tauri sets ASPNETCORE_CONTENTROOT / DOTNET_CONTENTROOT to that Resources path,
// but for robustness (and for ad-hoc manual runs) we also auto-detect it.
var contentRoot = Environment.GetEnvironmentVariable("ASPNETCORE_CONTENTROOT")
    ?? Environment.GetEnvironmentVariable("DOTNET_CONTENTROOT");

if (string.IsNullOrEmpty(contentRoot))
{
    var exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
    var macResourcesPath = Path.GetFullPath(Path.Combine(exeDir, "..", "Resources"));
    if (Directory.Exists(macResourcesPath) && File.Exists(Path.Combine(macResourcesPath, "appsettings.json")))
        contentRoot = macResourcesPath;
    else if (File.Exists(Path.Combine(exeDir, "appsettings.json")))
        contentRoot = exeDir;
    else
        // Started as "dotnet API.dll" (Azure App Service, most hosts) or by IIS: the process is dotnet / w3wp,
        // not the app, so its folder has no appsettings.json - use the folder the app was loaded from
        contentRoot = AppContext.BaseDirectory;
}

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = contentRoot
});

// Resolve a writable directory for log files.
// Contents/Resources/ inside a signed .app bundle is read-only, so fall back to
// the platform's local app-data directory when we cannot write next to the config.
var logDir = Path.Combine(contentRoot, "logs");
try { Directory.CreateDirectory(logDir); }
catch
{
    logDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DeployVare", "logs");
    Directory.CreateDirectory(logDir);
}

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(Path.Combine(logDir, "deployvare-.txt"), rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();

// Override FileUploadSettings paths with absolute writable paths.
// On macOS the .app bundle resources folder is read-only, so we use the
// DEPLOYVARE_DATA_DIR env var (set by the Tauri wrapper to app_data_dir).
// When running outside Tauri (dev, manual) we fall back to contentRoot/data.
var dataDir = Environment.GetEnvironmentVariable("DEPLOYVARE_DATA_DIR")
    ?? Path.Combine(contentRoot, "data");
var uploadsDir = Path.Combine(dataDir, "uploads");
var backupsDir = Path.Combine(dataDir, "backups");
Directory.CreateDirectory(uploadsDir);
Directory.CreateDirectory(backupsDir);
builder.Configuration["FileUploadSettings:UploadPath"] = uploadsDir;
builder.Configuration["FileUploadSettings:BackupPath"] = backupsDir;

// Add services to the container
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Serialize enums as strings instead of numbers
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "DeployVare API",
        Version = "v1",
        Description = "SQL Deployment Management API"
    });

    // Add JWT authentication to Swagger
    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Enter 'Bearer' [space] and then your token",
        Name = "Authorization",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// Configure Database – read DB_PROVIDER env var set by the Tauri sidecar launcher.
// "sqlite"    → SQLite file stored next to the app (default / local mode)
// "sqlserver" → SQL Server (cloud / enterprise mode, requires a connection string)
// "postgres"  → PostgreSQL (e.g. Supabase); DB_CONNECTION_STRING is an Npgsql connection string
var dbProvider = "sqlserver";//(Environment.GetEnvironmentVariable("DB_PROVIDER") ?? "sqlite").ToLowerInvariant();
var requestedProvider = Environment.GetEnvironmentVariable("DB_PROVIDER")?.Trim().ToLowerInvariant();
if (requestedProvider is "postgres" or "postgresql")
    dbProvider = "postgres";
var dbConnStr = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING")
                ?? builder.Configuration.GetConnectionString("DeployVareDb")!;

if (dbProvider == "postgres")
{
    PostgresSetup.ConfigureNpgsql();
    // The app keeps injecting DeployVareDbContext; the Postgres subclass brings its own migrations
    builder.Services.AddDbContext<DeployVareDbContext, PostgresDeployVareDbContext>(options => options.UseNpgsql(dbConnStr));
}
else
{
    builder.Services.AddDbContext<DeployVareDbContext>(options =>
    {
        if (dbProvider == "sqlserver")
            options.UseSqlServer(dbConnStr);
        else
            options.UseSqlite(dbConnStr);
    });
}

// Configure JWT Authentication
var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var secretKey = jwtSettings["SecretKey"] ?? throw new InvalidOperationException("JWT SecretKey is not configured");

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings["Issuer"],
        ValidAudience = jwtSettings["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
        ClockSkew = TimeSpan.Zero,
        NameClaimType = ClaimTypes.Name,
        RoleClaimType = ClaimTypes.Role
    };
});

builder.Services.AddAuthorization(options =>
{
    // Policy-based authorization for fine-grained permissions
    // These policies can be used with [Authorize(Policy = "PolicyName")] on controllers
    options.AddPolicy("ManageUsers", policy => 
        policy.RequireRole("Admin"));  // Only admins can manage users
    options.AddPolicy("ManageApplications", policy => 
        policy.RequireAssertion(context => 
            context.User.IsInRole("Admin")));  // Can be extended to check permissions
    options.AddPolicy("DeploySql", policy => 
        policy.RequireAssertion(context => 
            context.User.IsInRole("Admin")));  // Can be extended to check permissions
});

// Configure CORS
builder.Services.AddCors(options =>
{
    // CORS_ALLOWED_ORIGINS (comma-separated, e.g. https://deployvare.vercel.app) limits a public deployment to
    // its own frontend; unset keeps the previous allow-any-origin behaviour (desktop app, local development)
    var allowedOrigins = (Environment.GetEnvironmentVariable("CORS_ALLOWED_ORIGINS") ?? builder.Configuration["Cors:AllowedOrigins"] ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(o => o.TrimEnd('/'))
        .ToArray();

    options.AddPolicy("AllowAll", policy =>
    {
        if (allowedOrigins.Length > 0)
            policy.WithOrigins(allowedOrigins);
        else
            policy.AllowAnyOrigin();
        policy.AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Register Services
builder.Services.AddScoped<API.Services.Interfaces.IEncryptionService, API.Services.Implementations.EncryptionService>();
builder.Services.AddScoped<API.Services.Interfaces.ISqlExecutionService, API.Services.Implementations.SqlExecutionService>();
builder.Services.AddScoped<API.Services.Interfaces.IBackupService, API.Services.Implementations.BackupService>();
builder.Services.AddScoped<API.Services.Interfaces.IDeploymentService, API.Services.Implementations.DeploymentService>();
builder.Services.AddScoped<API.Services.Interfaces.ISchemaComparisonService, API.Services.Implementations.SchemaComparisonService>();
builder.Services.AddScoped<API.Services.Interfaces.IClientService, API.Services.Implementations.ClientService>();
builder.Services.AddScoped<API.Services.Interfaces.IApplicationService, API.Services.Implementations.ApplicationService>();
builder.Services.AddScoped<API.Services.Interfaces.IDatabaseInstanceService, API.Services.Implementations.DatabaseInstanceService>();
builder.Services.AddScoped<API.Services.Interfaces.IAuthService, API.Services.Implementations.AuthService>();
builder.Services.AddScoped<API.Services.Interfaces.IUserService, API.Services.Implementations.UserService>();

// API Deployment Services
builder.Services.AddScoped<API.Services.Interfaces.ISshService, API.Services.Implementations.SshService>();
builder.Services.AddScoped<API.Services.Interfaces.IIisManagementService, API.Services.Implementations.IisManagementService>();
builder.Services.AddScoped<API.Services.Interfaces.IApiDeploymentService, API.Services.Implementations.ApiDeploymentService>();
builder.Services.AddScoped<API.Services.Interfaces.IApplicationServerService, API.Services.Implementations.ApplicationServerService>();

// Azure DevOps integration (credentials are stored per client in the database, not in configuration)
builder.Services.Configure<API.Models.AzureDevOpsOptions>(builder.Configuration.GetSection(API.Models.AzureDevOpsOptions.SectionName));
builder.Services.AddHttpClient<API.Services.Interfaces.IAzureDevOpsService, API.Services.Implementations.AzureDevOpsService>(client =>
{
    // Timeouts are enforced per request by the service (short for REST calls, long for artifact downloads)
    client.Timeout = Timeout.InfiniteTimeSpan;
});
builder.Services.AddScoped<API.Services.Interfaces.IAzureDevOpsConfigService, API.Services.Implementations.AzureDevOpsConfigService>();

// Server readiness / setup (IIS, site, runtimes, Redis, SSL, bindings)
builder.Services.Configure<API.Models.ServerSetupOptions>(builder.Configuration.GetSection(API.Models.ServerSetupOptions.SectionName));
builder.Services.AddSingleton<API.Services.Interfaces.IInstallerCatalog, API.Services.Implementations.InstallerCatalog>();
builder.Services.AddScoped<API.Services.Interfaces.IServerReadinessService, API.Services.Implementations.ServerReadinessService>();
builder.Services.AddScoped<API.Services.Interfaces.ISqlServerSetupService, API.Services.Implementations.SqlServerSetupService>();
builder.Services.AddScoped<API.Services.Interfaces.IFrontendRuntimeConfigService, API.Services.Implementations.FrontendRuntimeConfigService>();
builder.Services.AddScoped<API.Services.Interfaces.IDatabaseMigrationService, API.Services.Implementations.DatabaseMigrationService>();
builder.Services.AddSingleton<API.Services.Interfaces.IDeploymentProgressTracker, API.Services.Implementations.DeploymentProgressTracker>();
builder.Services.AddScoped<API.Services.Interfaces.IManualPackageService, API.Services.Implementations.ManualPackageService>();

// Licensing: the signing key lives outside the application folder, so only this installation can issue licences
builder.Services.Configure<API.Models.LicensingOptions>(builder.Configuration.GetSection(API.Models.LicensingOptions.SectionName));
builder.Services.AddSingleton<API.Services.Interfaces.ISigningKeyStore, API.Services.Implementations.SigningKeyStore>();
builder.Services.AddScoped<API.Services.Interfaces.ILicensingService, API.Services.Implementations.LicensingService>();

// File storage – local by default; switch to B2 when the env var is set by the Tauri sidecar launcher
var b2Enabled = string.Equals(Environment.GetEnvironmentVariable("B2_ENABLED"), "true", StringComparison.OrdinalIgnoreCase);
if (b2Enabled)
{
    builder.Services.AddSingleton<API.Services.Interfaces.IFileStorageService>(_ =>
        new API.Services.Implementations.B2FileStorageService(
            Environment.GetEnvironmentVariable("B2_KEY_ID")!,
            Environment.GetEnvironmentVariable("B2_APP_KEY")!,
            Environment.GetEnvironmentVariable("B2_BUCKET")!,
            Environment.GetEnvironmentVariable("B2_ENDPOINT")!,
            Environment.GetEnvironmentVariable("B2_REGION")!
        ));
}
else
{
    builder.Services.AddSingleton<API.Services.Interfaces.IFileStorageService,
        API.Services.Implementations.LocalFileStorageService>();
}

var app = builder.Build();

// Seed database with admin user
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<DeployVareDbContext>();
    try
    {
        // For SQLite: EnsureCreated is sufficient (no migrations infra needed).
        // For SQL Server: run pending migrations as before.
        if (dbProvider is "sqlserver" or "postgres")
        {
            await context.Database.MigrateAsync();
            Log.Information("Database migrations applied successfully ({Provider})", dbProvider);
        }
        else
        {
            await context.Database.EnsureCreatedAsync();
            Log.Information("SQLite database ensured/created");
        }
        
        // Seed admin user
        await DbSeeder.SeedAsync(context);
    }
    catch (Exception ex)
    {
        Log.Error(ex, "An error occurred while seeding the database");
    }
}

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("AllowAll");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Upload and backup folders are created under the data directory at startup (see FileUploadSettings above).
// Do not use Directory.GetCurrentDirectory() here: under IIS it is C:\Windows\System32\inetsrv, which is not writable.

Log.Information("DeployVare API Started");

app.Run();
return 0;
