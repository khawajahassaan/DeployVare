using API.Models.Enums;

namespace API.DTOs.Requests;

public class UpdateUserRequest
{
    public string? FullName { get; set; }
    public string? Email { get; set; }
    public bool? IsActive { get; set; }
    public UserRole? Role { get; set; }
    public string? NewPassword { get; set; }
}




