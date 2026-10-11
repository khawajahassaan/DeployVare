using API.DTOs.Requests;
using API.DTOs.Responses;

namespace API.Services.Interfaces;

public interface IClientService
{
    Task<List<ClientDto>> GetAllClientsAsync();
    Task<ClientDto?> GetClientByIdAsync(int id);
    Task<ClientDto> CreateClientAsync(CreateClientRequest request, string createdBy);
    Task<ClientDto?> UpdateClientAsync(int id, UpdateClientRequest request);
    Task<ClientDto?> RegenerateLicenseKeyAsync(int id);
    Task<bool> DeleteClientAsync(int id);
}
