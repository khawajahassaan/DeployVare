namespace API.DTOs.Responses;

public class DatabaseInstanceDto
{
    public int Id { get; set; }
    public int ApplicationId { get; set; }
    public string ApplicationName { get; set; } = string.Empty;
    public string InstanceName { get; set; } = string.Empty;
    public int CurrentPatchNumber { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}



