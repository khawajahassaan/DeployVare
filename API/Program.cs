using API.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Serilog;

// Disable automatic claim type mapping (prevents "sub" from overriding ClaimTypes.NameIdentifier)
JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/deployvare-.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();

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
var dbProvider = (Environment.GetEnvironmentVariable("DB_PROVIDER") ?? "sqlite").ToLowerInvariant();
var dbConnStr = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING")
                ?? builder.Configuration.GetConnectionString("DeployVareDb")!;

builder.Services.AddDbContext<DeployVareDbContext>(options =>
{
    if (dbProvider == "sqlserver")
        options.UseSqlServer(dbConnStr);
    else
        options.UseSqlite(dbConnStr);
});

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
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Register Services
builder.Services.AddScoped<API.Services.Interfaces.IEncryptionService, API.Services.Implementations.EncryptionService>();
builder.Services.AddScoped<API.Services.Interfaces.ISqlExecutionService, API.Services.Implementations.SqlExecutionService>();
builder.Services.AddScoped<API.Services.Interfaces.IBackupService, API.Services.Implementations.BackupService>();
builder.Services.AddScoped<API.Services.Interfaces.IDeploymentService, API.Services.Implementations.DeploymentService>();
builder.Services.AddScoped<API.Services.Interfaces.ISchemaComparisonService, API.Services.Implementations.SchemaComparisonService>();
builder.Services.AddScoped<API.Services.Interfaces.IApplicationService, API.Services.Implementations.ApplicationService>();
builder.Services.AddScoped<API.Services.Interfaces.IDatabaseInstanceService, API.Services.Implementations.DatabaseInstanceService>();
builder.Services.AddScoped<API.Services.Interfaces.IAuthService, API.Services.Implementations.AuthService>();
builder.Services.AddScoped<API.Services.Interfaces.IUserService, API.Services.Implementations.UserService>();

// API Deployment Services
builder.Services.AddScoped<API.Services.Interfaces.ISshService, API.Services.Implementations.SshService>();
builder.Services.AddScoped<API.Services.Interfaces.IIisManagementService, API.Services.Implementations.IisManagementService>();
builder.Services.AddScoped<API.Services.Interfaces.IApiDeploymentService, API.Services.Implementations.ApiDeploymentService>();
builder.Services.AddScoped<API.Services.Interfaces.IApplicationServerService, API.Services.Implementations.ApplicationServerService>();

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
        if (dbProvider == "sqlserver")
        {
            await context.Database.MigrateAsync();
            Log.Information("Database migrations applied successfully");
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

// Create upload and backup directories
var uploadPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
var backupPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "backups");
Directory.CreateDirectory(uploadPath);
Directory.CreateDirectory(backupPath);

Log.Information("DeployVare API Started");

app.Run();
