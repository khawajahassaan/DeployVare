namespace API.DTOs.Requests;

public class CreateDatabaseInstanceRequest
{
    public int ApplicationId { get; set; }
    public string InstanceName { get; set; } = string.Empty;
    public string ConnectionString { get; set; } = string.Empty; // Will be encrypted before storing
    public int CurrentPatchNumber { get; set; } = 0;
}



