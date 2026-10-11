using API.DTOs.Responses;
using API.Models.Enums;

namespace API.Services.Interfaces;

/// <summary>Finds installer files for server components in the configured Installers folder.</summary>
public interface IInstallerCatalog
{
    List<InstallerFileDto> GetInstallers();

    /// <summary>Full local path of the newest installer for the component, or null when none is available.</summary>
    string? GetInstallerPath(ServerComponent component);
}
