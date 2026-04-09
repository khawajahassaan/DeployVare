using API.DTOs.Responses;

namespace API.Services.Interfaces;

public interface ISchemaComparisonService
{
    Task<SchemaComparisonDto> CompareSchemaAsync(int sourceInstanceId, int targetInstanceId);
    Task<ProcedureDetailDto> GetProcedureDetailAsync(int sourceInstanceId, int targetInstanceId, string procedureName, string schema);
    Task<TransferResultDto> TransferObjectAsync(int sourceInstanceId, int targetInstanceId, string objectType, string objectName, string schema, string direction, string transferredBy);
}



