namespace API.DTOs.Requests;

public class CreateApplicationRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}



