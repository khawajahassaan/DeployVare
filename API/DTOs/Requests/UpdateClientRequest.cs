namespace API.DTOs.Requests;

public class UpdateClientRequest
{
    public string? Name { get; set; }
    public string? ClientCode { get; set; }
    public bool? IsActive { get; set; }
}
