namespace API.DTOs.Requests;

public class RegisterRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public string? Email { get; set; }
    public string Role { get; set; } = "User"; // Admin, User
}



