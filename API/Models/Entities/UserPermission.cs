using API.Models.Enums;

namespace API.Models.Entities;

public class UserPermission
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public Permission Permission { get; set; }
    public DateTime GrantedAt { get; set; }
    public string GrantedBy { get; set; } = string.Empty;
}




