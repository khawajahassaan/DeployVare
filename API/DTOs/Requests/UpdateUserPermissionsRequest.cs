using API.Models.Enums;

namespace API.DTOs.Requests;

public class UpdateUserPermissionsRequest
{
    public List<Permission> Permissions { get; set; } = new();
}




