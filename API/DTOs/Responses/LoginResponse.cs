namespace API.DTOs.Responses;

public class LoginResponse
{
    public string Token { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public string Role { get; set; } = string.Empty;
    public List<string> Permissions { get; set; } = new();
    public DateTime ExpiresAt { get; set; }
}



