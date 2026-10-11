using API.DTOs.Requests;
using API.DTOs.Responses;

namespace API.Services.Interfaces;

public interface IApplicationServerService
{
    Task<ApplicationServerDto?> CreateAsync(CreateServerRequest request);
    Task<ApplicationServerDto?> GetByIdAsync(int id);
    Task<List<ApplicationServerDto>> GetByApplicationIdAsync(int applicationId);
    Task<List<ApplicationServerDto>> GetAllAsync(int? clientId = null);
    Task<ApplicationServerDto?> UpdateAsync(int id, UpdateServerRequest request);
    Task<bool> DeleteAsync(int id);
    Task<(bool Success, string Message)> TestConnectionAsync(int id);
}





