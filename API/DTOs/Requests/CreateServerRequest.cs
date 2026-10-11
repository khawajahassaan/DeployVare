using System.ComponentModel.DataAnnotations;
using API.Models.Enums;

namespace API.DTOs.Requests;

public class CreateServerRequest
{
    [Required]
    [Range(1, int.MaxValue, ErrorMessage = "Select an application for this server.")]
    public int ApplicationId { get; set; }

    [Required]
    [MaxLength(200)]
    public string ServerName { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    [RegularExpression(ServerHostName.Pattern, ErrorMessage = ServerHostName.ErrorMessage)]
    public string HostName { get; set; } = string.Empty;

    public int Port { get; set; } = 22;

    [Required]
    [MaxLength(100)]
    public string Username { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Password { get; set; }

    public string? SshPrivateKey { get; set; }

    [Required]
    [MaxLength(500)]
    public string DeploymentPath { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string AppPoolName { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string WebsiteName { get; set; } = string.Empty;

    [MaxLength(500)]
    public string BackupPath { get; set; } = @"C:\DeployVare\Backups";

    public ServerType ServerType { get; set; } = ServerType.Api;
}


