namespace API.DTOs.Requests;

public class UpdateDatabaseInstanceRequest
{
    public string? InstanceName { get; set; }
    public string? ConnectionString { get; set; }
    public int? CurrentPatchNumber { get; set; }
    public bool? IsActive { get; set; }
}



