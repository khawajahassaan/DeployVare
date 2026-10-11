using API.DTOs.Requests;
using API.DTOs.Responses;

namespace API.Services.Interfaces;

public interface IDatabaseInstanceService
{
    Task<List<DatabaseInstanceDto>> GetAllInstancesAsync(int? clientId = null);
    Task<List<DatabaseInstanceDto>> GetInstancesByApplicationIdAsync(int applicationId);
    Task<DatabaseInstanceDto?> GetInstanceByIdAsync(int id);
    Task<DatabaseInstanceDto> CreateInstanceAsync(CreateDatabaseInstanceRequest request, string createdBy);
    Task<DatabaseInstanceDto?> UpdateInstanceAsync(int id, UpdateDatabaseInstanceRequest request, string updatedBy);
    Task<bool> DeleteInstanceAsync(int id);
    Task<bool> TestConnectionAsync(int id);
}




