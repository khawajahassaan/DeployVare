using API.Models.Enums;

namespace API.DTOs.Requests;

public class CreateUserRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public string? Email { get; set; }
    public UserRole Role { get; set; } = UserRole.User;
    public List<Permission> Permissions { get; set; } = new();
}




