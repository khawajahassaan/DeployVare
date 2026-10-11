using API.Data;
using API.DTOs.Requests;
using API.DTOs.Responses;
using API.Models.Entities;
using API.Models.Enums;
using API.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace API.Services.Implementations;

public class UserService : IUserService
{
    private readonly DeployVareDbContext _context;
    private readonly ILogger<UserService> _logger;

    public UserService(DeployVareDbContext context, ILogger<UserService> _logger)
    {
        _context = context;
        this._logger = _logger;
    }

    public async Task<List<UserDto>> GetAllUsersAsync()
    {
        var users = await _context.Users
            .Include(u => u.UserPermissions)
            .OrderBy(u => u.Username)
            .ToListAsync();

        return users.Select(MapToDto).ToList();
    }

    public async Task<UserDto?> GetUserByIdAsync(int id)
    {
        var user = await _context.Users
            .Include(u => u.UserPermissions)
            .FirstOrDefaultAsync(u => u.Id == id);

        return user == null ? null : MapToDto(user);
    }

    public async Task<UserDto> CreateUserAsync(CreateUserRequest request, string createdBy)
    {
        // Check if username already exists
        if (await _context.Users.AnyAsync(u => u.Username == request.Username))
        {
            throw new InvalidOperationException($"Username '{request.Username}' already exists");
        }

        // Hash password
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);

        var user = new User
        {
            Username = request.Username,
            PasswordHash = passwordHash,
            FullName = request.FullName,
            Email = request.Email,
            Role = request.Role,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        // Add permissions if specified
        if (request.Permissions.Any())
        {
            foreach (var permission in request.Permissions)
            {
                _context.UserPermissions.Add(new UserPermission
                {
                    UserId = user.Id,
                    Permission = permission,
                    GrantedAt = DateTime.UtcNow,
                    GrantedBy = createdBy
                });
            }
            await _context.SaveChangesAsync();
        }

        _logger.LogInformation("User '{Username}' created by {CreatedBy}", user.Username, createdBy);

        // Reload user with permissions
        user = await _context.Users
            .Include(u => u.UserPermissions)
            .FirstAsync(u => u.Id == user.Id);

        return MapToDto(user);
    }

    public async Task<UserDto?> UpdateUserAsync(int id, UpdateUserRequest request)
    {
        var user = await _context.Users.FindAsync(id);
        if (user == null)
            return null;

        // Update fields if provided
        if (!string.IsNullOrWhiteSpace(request.FullName))
            user.FullName = request.FullName;

        if (!string.IsNullOrWhiteSpace(request.Email))
            user.Email = request.Email;

        if (request.IsActive.HasValue)
            user.IsActive = request.IsActive.Value;

        if (request.Role.HasValue)
            user.Role = request.Role.Value;

        if (!string.IsNullOrWhiteSpace(request.NewPassword))
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);

        await _context.SaveChangesAsync();

        _logger.LogInformation("User {UserId} updated", id);

        // Reload with permissions
        user = await _context.Users
            .Include(u => u.UserPermissions)
            .FirstAsync(u => u.Id == id);

        return MapToDto(user);
    }

    public async Task<bool> DeleteUserAsync(int id)
    {
        var user = await _context.Users.FindAsync(id);
        if (user == null)
            return false;

        // Prevent deleting the last admin
        if (user.Role == UserRole.Admin)
        {
            var adminCount = await _context.Users.CountAsync(u => u.Role == UserRole.Admin && u.Id != id);
            if (adminCount == 0)
            {
                throw new InvalidOperationException("Cannot delete the last admin user");
            }
        }

        _context.Users.Remove(user);
        await _context.SaveChangesAsync();

        _logger.LogInformation("User {UserId} deleted", id);
        return true;
    }

    public async Task<List<Permission>> GetUserPermissionsAsync(int userId)
    {
        var user = await _context.Users
            .Include(u => u.UserPermissions)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null)
            return new List<Permission>();

        // Admins have all permissions
        if (user.Role == UserRole.Admin)
            return Enum.GetValues<Permission>().ToList();

        return user.UserPermissions.Select(up => up.Permission).ToList();
    }

    public async Task UpdateUserPermissionsAsync(int userId, List<Permission> permissions, string grantedBy)
    {
        var user = await _context.Users
            .Include(u => u.UserPermissions)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null)
            throw new InvalidOperationException($"User with ID {userId} not found");

        // Remove existing permissions
        _context.UserPermissions.RemoveRange(user.UserPermissions);

        // Add new permissions
        foreach (var permission in permissions)
        {
            _context.UserPermissions.Add(new UserPermission
            {
                UserId = userId,
                Permission = permission,
                GrantedAt = DateTime.UtcNow,
                GrantedBy = grantedBy
            });
        }

        await _context.SaveChangesAsync();

        _logger.LogInformation("Permissions updated for user {UserId} by {GrantedBy}", userId, grantedBy);
    }

    public async Task<bool> UserHasPermissionAsync(int userId, Permission permission)
    {
        var user = await _context.Users
            .Include(u => u.UserPermissions)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null || !user.IsActive)
            return false;

        // Admins have all permissions
        if (user.Role == UserRole.Admin)
            return true;

        return user.UserPermissions.Any(up => up.Permission == permission);
    }

    private UserDto MapToDto(User user)
    {
        return new UserDto
        {
            Id = user.Id,
            Username = user.Username,
            FullName = user.FullName,
            Email = user.Email,
            Role = user.Role.ToString(),
            IsActive = user.IsActive,
            CreatedAt = user.CreatedAt,
            LastLoginAt = user.LastLoginAt,
            Permissions = user.Role == UserRole.Admin
                ? Enum.GetValues<Permission>().Select(p => p.ToString()).ToList()
                : user.UserPermissions.Select(up => up.Permission.ToString()).ToList()
        };
    }
}

