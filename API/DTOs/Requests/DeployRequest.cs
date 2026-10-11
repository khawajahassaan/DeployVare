using Microsoft.AspNetCore.Http;

namespace API.DTOs.Requests;

public class DeployRequest
{
    public int DatabaseInstanceId { get; set; }
    public IFormFile ZipFile { get; set; } = null!;
}



