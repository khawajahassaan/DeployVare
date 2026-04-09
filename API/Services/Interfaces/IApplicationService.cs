using API.DTOs.Requests;
using API.DTOs.Responses;

namespace API.Services.Interfaces;

public interface IApplicationService
{
    Task<List<ApplicationDto>> GetAllApplicationsAsync();
    Task<ApplicationDto?> GetApplicationByIdAsync(int id);
    Task<ApplicationDto> CreateApplicationAsync(CreateApplicationRequest request, string createdBy);
    Task<bool> DeleteApplicationAsync(int id);
}




