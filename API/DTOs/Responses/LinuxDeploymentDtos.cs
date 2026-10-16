namespace API.DTOs.Responses;

/// <summary>Latest build of one tier and the artifact a Linux deployment would use.</summary>
public class LinuxBuildOptionDto
{
    public string ServerType { get; set; } = string.Empty;
    public AzureBuildDto? Build { get; set; }
    public string? ConfiguredArtifactName { get; set; }
    public string? SuggestedArtifactName { get; set; }
    public bool SuggestedIsLinuxSpecific { get; set; }
    public string? Note { get; set; }
    /// <summary>Why no build is offered (no artifact source, no successful build, ...).</summary>
    public string? Error { get; set; }
    public bool RunDatabaseMigrations { get; set; }
}

public class LinuxBuildsDto
{
    public string ApplicationName { get; set; } = string.Empty;
    /// <summary>Lower-case name used for default folders, site files and the service, e.g. auditvare.</summary>
    public string Slug { get; set; } = string.Empty;
    public LinuxBuildOptionDto Api { get; set; } = new();
    public LinuxBuildOptionDto Frontend { get; set; } = new();
}

public class LinuxConnectionTestDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Os { get; set; }
    public bool SupportedOs { get; set; }
    public bool? SudoWorks { get; set; }
    public string? DotNetRuntimes { get; set; }
    public bool? NginxInstalled { get; set; }
    public bool? ApacheInstalled { get; set; }
    public long? DiskFreeMb { get; set; }
    /// <summary>Enforcing / Permissive / Disabled on Red Hat family servers; null when SELinux is not present.</summary>
    public string? SeLinux { get; set; }
}

public class LinuxDeploymentKitResultDto
{
    public string Tier { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public string BuildNumber { get; set; } = string.Empty;
    public string ArtifactName { get; set; } = string.Empty;
    public bool Succeeded { get; set; }
    public List<string> Output { get; set; } = new();
}

public class LinuxDeploymentResultDto
{
    public bool Succeeded { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<LinuxDeploymentKitResultDto> Kits { get; set; } = new();
    public DateTime StartedAt { get; set; }
    public DateTime CompletedAt { get; set; }
}
