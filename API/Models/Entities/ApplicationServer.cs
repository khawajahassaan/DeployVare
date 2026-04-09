using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace API.Models.Entities;

public class ApplicationServer
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int ApplicationId { get; set; }

    [ForeignKey(nameof(ApplicationId))]
    public Application Application { get; set; } = null!;

    [Required]
    [MaxLength(200)]
    public string ServerName { get; set; } = string.Empty; // Friendly name (e.g., "Production Server 1")

    [Required]
    [MaxLength(200)]
    public string HostName { get; set; } = string.Empty; // IP or hostname for SSH connection

    public int Port { get; set; } = 22; // SSH port

    [Required]
    [MaxLength(100)]
    public string Username { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? EncryptedPassword { get; set; } // AES-256 encrypted password

    [MaxLength(2000)]
    public string? SshPrivateKey { get; set; } // SSH private key (optional, more secure)

    [Required]
    [MaxLength(500)]
    public string DeploymentPath { get; set; } = string.Empty; // e.g., C:\inetpub\wwwroot\myapp

    [Required]
    [MaxLength(200)]
    public string AppPoolName { get; set; } = string.Empty; // IIS App Pool name

    [Required]
    [MaxLength(200)]
    public string WebsiteName { get; set; } = string.Empty; // IIS Website name

    [MaxLength(500)]
    public string BackupPath { get; set; } = @"C:\DeployVare\Backups"; // Where to store backups on remote server

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    // Navigation properties
    public ICollection<ApiDeployment> ApiDeployments { get; set; } = new List<ApiDeployment>();
}





