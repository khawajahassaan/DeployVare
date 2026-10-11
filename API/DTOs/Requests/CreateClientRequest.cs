namespace API.DTOs.Requests;

public class CreateClientRequest
{
    public string Name { get; set; } = string.Empty;
    public string ClientCode { get; set; } = string.Empty;
}
