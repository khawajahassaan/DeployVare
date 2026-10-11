using System.Text.RegularExpressions;
using API.DTOs.Responses;
using API.Models;
using API.Models.Enums;
using API.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace API.Services.Implementations;

public class InstallerCatalog : IInstallerCatalog
{
    private static readonly Regex VersionPattern = new(@"\d+(?:\.\d+){1,3}", RegexOptions.Compiled);

    private readonly ServerSetupOptions _options;
    private readonly string _installersDirectory;

    public InstallerCatalog(IOptions<ServerSetupOptions> options, IWebHostEnvironment environment)
    {
        _options = options.Value;
        _installersDirectory = ResolveDirectory(_options.InstallersPath, environment.ContentRootPath,
            new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() });
    }

    /// <summary>
    /// Resolves a relative installers path against the content root and its parent folders (and any extra
    /// start folders), so it works from the project folder, from bin/ output and from a packaged app.
    /// Falls back to the path relative to the content root.
    /// </summary>
    public static string ResolveDirectory(string configuredPath, string contentRoot, IEnumerable<string> additionalStartFolders)
    {
        if (Path.IsPathRooted(configuredPath))
            return configuredPath;

        foreach (var start in new[] { contentRoot }.Concat(additionalStartFolders).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var directory = new DirectoryInfo(start);
            for (var depth = 0; directory != null && depth < 6; depth++, directory = directory.Parent)
            {
                var candidate = Path.GetFullPath(Path.Combine(directory.FullName, configuredPath));
                if (Directory.Exists(candidate))
                    return candidate;
            }
        }

        return Path.GetFullPath(Path.Combine(contentRoot, configuredPath));
    }

    public List<InstallerFileDto> GetInstallers()
    {
        return Enum.GetValues<ServerComponent>().Select(component =>
        {
            var pattern = GetPattern(component);
            var file = FindNewest(pattern);
            return new InstallerFileDto
            {
                Component = component.ToString(),
                Directory = _installersDirectory,
                Pattern = pattern,
                Available = file != null,
                FileName = file?.Name,
                Version = file == null ? null : ParseVersion(file.Name),
                SizeBytes = file?.Length
            };
        }).ToList();
    }

    public string? GetInstallerPath(ServerComponent component) => FindNewest(GetPattern(component))?.FullName;

    /// <summary>Extracts the first dotted version number from an installer file name.</summary>
    public static string? ParseVersion(string fileName)
    {
        var match = VersionPattern.Match(fileName);
        return match.Success ? match.Value : null;
    }

    private string GetPattern(ServerComponent component) =>
        _options.InstallerPatterns.TryGetValue(component.ToString(), out var pattern) ? pattern : $"{component}*";

    private FileInfo? FindNewest(string pattern)
    {
        if (!Directory.Exists(_installersDirectory))
            return null;

        var directory = new DirectoryInfo(_installersDirectory);
        var files = pattern
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .SelectMany(p => directory.EnumerateFiles(p, SearchOption.TopDirectoryOnly));

        return files
            .OrderByDescending(f => f.Length)
            .ThenByDescending(f => Version.TryParse(ParseVersion(f.Name), out var v) ? v : new Version(0, 0))
            .ThenByDescending(f => f.LastWriteTimeUtc)
            .FirstOrDefault();
    }
}
