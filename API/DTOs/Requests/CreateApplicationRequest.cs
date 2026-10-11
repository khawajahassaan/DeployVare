namespace API.DTOs.Requests;

public class CreateApplicationRequest
{
    public int ClientId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}



