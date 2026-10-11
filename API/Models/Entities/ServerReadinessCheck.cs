namespace API.Models.Entities;

/// <summary>
/// Latest readiness report for a server (one row per server, replaced on every check).
/// </summary>
public class ServerReadinessCheck
{
    public int Id { get; set; }
    public int ApplicationServerId { get; set; }
    public DateTime CheckedAt { get; set; }
    public string CheckedBy { get; set; } = string.Empty;
    public bool IsReady { get; set; }

    /// <summary>Serialized ServerReadinessReportDto (contains no credentials).</summary>
    public string ReportJson { get; set; } = string.Empty;

    // Navigation properties
    public ApplicationServer ApplicationServer { get; set; } = null!;
}
