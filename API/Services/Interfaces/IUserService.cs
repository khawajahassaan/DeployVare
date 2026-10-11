using API.DTOs.Requests;
using API.DTOs.Responses;
using API.Models.Enums;

namespace API.Services.Interfaces;

public interface IUserService
{
    Task<List<UserDto>> GetAllUsersAsync();
    Task<UserDto?> GetUserByIdAsync(int id);
    Task<UserDto> CreateUserAsync(CreateUserRequest request, string createdBy);
    Task<UserDto?> UpdateUserAsync(int id, UpdateUserRequest request);
    Task<bool> DeleteUserAsync(int id);
    Task<List<Permission>> GetUserPermissionsAsync(int userId);
    Task UpdateUserPermissionsAsync(int userId, List<Permission> permissions, string grantedBy);
    Task<bool> UserHasPermissionAsync(int userId, Permission permission);
}




