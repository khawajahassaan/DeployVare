namespace API.DTOs.Requests;

public class TransferObjectRequest
{
    public int SourceInstanceId { get; set; }
    public int TargetInstanceId { get; set; }
    public string ObjectType { get; set; } = string.Empty; // "StoredProcedure" or "Function"
    public string ObjectName { get; set; } = string.Empty;
    public string Schema { get; set; } = "dbo";
    public string Direction { get; set; } = string.Empty; // "SourceToTarget" or "TargetToSource"
}




