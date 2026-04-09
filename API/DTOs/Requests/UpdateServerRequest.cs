using System.ComponentModel.DataAnnotations;

namespace API.DTOs.Requests;

public class UpdateServerRequest
{
    [MaxLength(200)]
    public string? ServerName { get; set; }

    [MaxLength(200)]
    public string? HostName { get; set; }

    public int? Port { get; set; }

    [MaxLength(100)]
    public string? Username { get; set; }

    [MaxLength(500)]
    public string? Password { get; set; }

    public string? SshPrivateKey { get; set; }

    [MaxLength(500)]
    public string? DeploymentPath { get; set; }

    [MaxLength(200)]
    public string? AppPoolName { get; set; }

    [MaxLength(200)]
    public string? WebsiteName { get; set; }

    [MaxLength(500)]
    public string? BackupPath { get; set; }

    public bool? IsActive { get; set; }
}





