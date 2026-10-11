using API.Data;
using API.DTOs.Requests;
using API.DTOs.Responses;
using API.Models.Entities;
using API.Models.Enums;
using API.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace API.Services.Implementations;

public class AuthService : IAuthService
{
    private readonly DeployVareDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        DeployVareDbContext context,
        IConfiguration configuration,
        ILogger<AuthService> logger)
    {
        _context = context;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<LoginResponse> LoginAsync(LoginRequest request)
    {
        var user = await _context.Users
            .Include(u => u.UserPermissions)
            .FirstOrDefaultAsync(u => u.Username == request.Username && u.IsActive);

        if (user == null)
        {
            throw new UnauthorizedAccessException("Invalid username or password");
        }

        // Verify password
        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            throw new UnauthorizedAccessException("Invalid username or password");
        }

        // Update last login
        user.LastLoginAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        // Generate JWT token
        var token = GenerateJwtToken(user);

        // Get user permissions
        var permissions = user.Role == UserRole.Admin
            ? Enum.GetValues<Permission>().Select(p => p.ToString()).ToList()
            : user.UserPermissions.Select(up => up.Permission.ToString()).ToList();

        _logger.LogInformation("User {Username} logged in successfully", user.Username);

        return new LoginResponse
        {
            Token = token,
            Username = user.Username,
            FullName = user.FullName,
            Role = user.Role.ToString(),
            Permissions = permissions,
            ExpiresAt = DateTime.UtcNow.AddMinutes(GetTokenExpirationMinutes())
        };
    }

    public async Task<LoginResponse> RegisterAsync(RegisterRequest request)
    {
        // Check if user already exists
        if (await UserExistsAsync(request.Username))
        {
            throw new InvalidOperationException($"User '{request.Username}' already exists");
        }

        // Parse role
        if (!Enum.TryParse<UserRole>(request.Role, true, out var role))
        {
            throw new ArgumentException($"Invalid role: {request.Role}");
        }

        // Create new user
        var user = new User
        {
            Username = request.Username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            FullName = request.FullName,
            Email = request.Email,
            Role = role,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        _logger.LogInformation("User {Username} registered successfully with role {Role}", user.Username, user.Role);

        // Generate JWT token
        var token = GenerateJwtToken(user);

        return new LoginResponse
        {
            Token = token,
            Username = user.Username,
            FullName = user.FullName,
            Role = user.Role.ToString(),
            ExpiresAt = DateTime.UtcNow.AddMinutes(GetTokenExpirationMinutes())
        };
    }

    public async Task<bool> UserExistsAsync(string username)
    {
        return await _context.Users.AnyAsync(u => u.Username == username);
    }

    private string GenerateJwtToken(User user)
    {
        var jwtSettings = _configuration.GetSection("JwtSettings");
        var secretKey = jwtSettings["SecretKey"] ?? throw new InvalidOperationException("JWT SecretKey not configured");
        var issuer = jwtSettings["Issuer"] ?? "DeployVareAPI";
        var audience = jwtSettings["Audience"] ?? "DeployVareClient";
        var expirationMinutes = GetTokenExpirationMinutes();

        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Username),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Role, user.Role.ToString())
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expirationMinutes),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private int GetTokenExpirationMinutes()
    {
        var jwtSettings = _configuration.GetSection("JwtSettings");
        if (int.TryParse(jwtSettings["ExpirationMinutes"], out int minutes))
        {
            return minutes;
        }
        return 480; // Default 8 hours
    }
}



