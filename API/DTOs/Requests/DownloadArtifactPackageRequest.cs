using System.ComponentModel.DataAnnotations;
using API.Models.Enums;
using Microsoft.AspNetCore.Http;

namespace API.DTOs.Requests;

/// <summary>Asks for a complete offline manual-deployment ZIP for the selected server (nothing is sent to that server).</summary>
public class DownloadArtifactPackageRequest
{
    [Required]
    public int ApplicationServerId { get; set; }

    [Required]
    public ArtifactEnvironment Environment { get; set; }

    /// <summary>Build shown to the user; re-validated against the configured source before packaging.</summary>
    [Required]
    public int BuildId { get; set; }

    [MaxLength(200)]
    public string? ArtifactName { get; set; }

    /// <summary>
    /// Defaults to WithoutLicense, so a caller that does not send the field gets the package it always got.
    /// </summary>
    public LicensingMode LicensingMode { get; set; } = LicensingMode.WithoutLicense;

    /// <summary>Licence to include in the package, required only when <see cref="LicensingMode"/> is WithLicense.</summary>
    public int? LicenseId { get; set; }

    /// <summary>When true, the package restores the baseline .bak (and installs SQL Express if needed). API servers only.</summary>
    public bool FreshDatabase { get; set; }

    public CertificateMode CertificateMode { get; set; } = CertificateMode.Default;

    /// <summary>IIS host header / HTTPS host name, e.g. api.client.com. Taken from Server Setup bindings when omitted.</summary>
    [MaxLength(253)]
    public string? SiteHostName { get; set; }

    /// <summary>SQL instance name used for a fresh Express install. Default SQLEXPRESS.</summary>
    [MaxLength(16)]
    public string? SqlInstanceName { get; set; }

    /// <summary>Database name override. Defaults to the linked instance's catalog.</summary>
    [MaxLength(128)]
    public string? DatabaseName { get; set; }

    /// <summary>Required when <see cref="CertificateMode"/> is UserProvided.</summary>
    public IFormFile? CertificateFile { get; set; }

    /// <summary>
    /// When true, the download is one zip with both the API kit and the frontend kit.
    /// This-server-only packages stay unchanged when the field is omitted.
    /// </summary>
    public bool Combined { get; set; }

    /// <summary>
    /// Adds the offline installers (.NET Hosting Bundle, SDK, Memurai, URL Rewrite, SQL Express). Off makes a much smaller
    /// package for servers that already have the prerequisites. Defaults to on, the package as it always was.
    /// </summary>
    public bool IncludeInstallers { get; set; } = true;

    /// <summary>Client-generated ID; the build reports its stages under it (GET apideployments/progress/{id}).</summary>
    [MaxLength(64)]
    public string? ProgressId { get; set; }

    /// <summary>Opposite-type server on the same application. Resolved automatically when there is only one.</summary>
    public int? CounterpartServerId { get; set; }

    /// <summary>HTTPS host name for the counterpart site (the selected server still uses <see cref="SiteHostName"/>).</summary>
    [MaxLength(253)]
    public string? CounterpartSiteHostName { get; set; }
}
