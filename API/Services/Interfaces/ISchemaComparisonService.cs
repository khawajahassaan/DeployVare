using API.DTOs.Responses;

namespace API.Services.Interfaces;

public interface ISchemaComparisonService
{
    Task<SchemaComparisonDto> CompareSchemaAsync(int sourceInstanceId, int targetInstanceId);
    Task<ProcedureDetailDto> GetProcedureDetailAsync(int sourceInstanceId, int targetInstanceId, string procedureName, string schema);
    Task<TransferResultDto> TransferObjectAsync(int sourceInstanceId, int targetInstanceId, string objectType, string objectName, string schema, string direction, string transferredBy);

    /// <summary>
    /// Compares schemas where either side can be a .dbsnap file or a live database instance.
    /// Exactly one of (sourceSnapStream, sourceInstanceId) must be non-null, and same for target.
    /// </summary>
    Task<SchemaComparisonDto> CompareWithSnapAsync(
        Stream? sourceSnapStream, string sourceSnapLabel,
        Stream? targetSnapStream, string targetSnapLabel,
        int? sourceInstanceId, int? targetInstanceId);
}



