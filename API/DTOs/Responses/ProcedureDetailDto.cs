namespace API.DTOs.Responses;

public class ProcedureDetailDto
{
    public string ProcedureName { get; set; } = string.Empty;
    public string Schema { get; set; } = string.Empty;
    public string SourceDefinition { get; set; } = string.Empty;
    public string TargetDefinition { get; set; } = string.Empty;
    public string Diff { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty; // Modified, Added, Removed
}




