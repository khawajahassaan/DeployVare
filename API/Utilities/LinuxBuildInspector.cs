using System.Text.Json;
using API.DTOs.Responses;
using API.Models.Enums;
using API.Services;

namespace API.Utilities;

/// <summary>
/// Chooses the Linux build artifact of an Azure DevOps build and finds how a published .NET backend starts on Linux.
/// </summary>
public static class LinuxBuildInspector
{
    public sealed record ArtifactChoice(string Name, bool IsLinuxSpecific, string? Note);

    public sealed record BackendEntry(string EntryFile, bool SelfContained, int DotNetMajor, string? Note);

    /// <summary>
    /// The requested artifact when given; otherwise one whose name marks it as a Linux build ("linux", "linux-x64",
    /// "drop-linux"), preferring one named after the configured artifact; otherwise the configured artifact.
    /// </summary>
    public static ArtifactChoice SelectArtifact(
        IReadOnlyList<AzureBuildArtifactDto> artifacts, string? configuredName, string? requestedName, ServerType serverType)
    {
        var downloadable = artifacts.Where(a => a.IsDownloadable).ToList();

        if (!string.IsNullOrWhiteSpace(requestedName))
        {
            var requested = downloadable.FirstOrDefault(a => a.Name.Equals(requestedName.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? throw new AzureDevOpsException(AzureDevOpsErrorCode.ArtifactNotFound,
                    $"The build has no downloadable artifact named '{requestedName}'.");
            return new ArtifactChoice(requested.Name, IsLinuxName(requested.Name), null);
        }

        var linux = downloadable.Where(a => IsLinuxName(a.Name)).ToList();
        if (linux.Count > 0)
        {
            var preferred = !string.IsNullOrWhiteSpace(configuredName)
                ? linux.FirstOrDefault(a => a.Name.StartsWith(configuredName, StringComparison.OrdinalIgnoreCase))
                : null;
            return new ArtifactChoice((preferred ?? linux[0]).Name, true, null);
        }

        var fallback = !string.IsNullOrWhiteSpace(configuredName)
            ? downloadable.FirstOrDefault(a => a.Name.Equals(configuredName, StringComparison.OrdinalIgnoreCase))
            : downloadable.Count == 1 ? downloadable[0] : null;
        if (fallback == null)
            throw new AzureDevOpsException(AzureDevOpsErrorCode.ArtifactNotFound,
                "The build has no artifact with 'linux' in its name and no single default artifact. "
                + $"Artifacts: {string.Join(", ", downloadable.Select(a => a.Name))}.");

        var note = serverType == ServerType.Frontend
            ? $"No Linux-specific artifact; using '{fallback.Name}' (static frontend files run on any OS)."
            : $"No Linux-specific artifact; using '{fallback.Name}'. It works on Linux only if it is a framework-dependent "
              + "(portable) publish. Add a linux-x64 publish to the pipeline for a dedicated Linux build.";
        return new ArtifactChoice(fallback.Name, false, note);
    }

    public static bool IsLinuxName(string name) =>
        name.Contains("linux", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Finds the web application's entry point in a published folder: the *.runtimeconfig.json that targets
    /// ASP.NET Core names App.dll; a self-contained publish must also carry the native App binary (linux-x64).
    /// </summary>
    public static BackendEntry FindBackendEntry(string contentDirectory)
    {
        var candidates = Directory.GetFiles(contentDirectory, "*.runtimeconfig.json")
            .Select(path => (Path: path, Stem: Path.GetFileName(path)[..^".runtimeconfig.json".Length]))
            .Where(c => !c.Stem.Equals("DbMigrator", StringComparison.OrdinalIgnoreCase)
                        && !c.Stem.Equals("testhost", StringComparison.OrdinalIgnoreCase)
                        && File.Exists(Path.Combine(contentDirectory, c.Stem + ".dll")))
            .Select(c => (c.Path, c.Stem, Config: ReadRuntimeConfig(c.Path)))
            .OrderByDescending(c => c.Config.IsAspNetCore)
            .ToList();

        if (candidates.Count == 0)
            throw new AzureDevOpsException(AzureDevOpsErrorCode.InvalidArtifact,
                "No *.runtimeconfig.json with a matching .dll was found at the root of the backend artifact, so the "
                + "application's entry point is unknown. Check the artifact and the folder-inside-artifact setting.");

        var (_, stem, config) = candidates[0];
        var major = config.FrameworkMajor ?? 10;

        if (!config.SelfContained)
            return new BackendEntry(stem + ".dll", false, major, null);

        if (File.Exists(Path.Combine(contentDirectory, stem)))
            return new BackendEntry(stem, true, major, null);

        throw new AzureDevOpsException(AzureDevOpsErrorCode.InvalidArtifact,
            File.Exists(Path.Combine(contentDirectory, stem + ".exe"))
                ? $"'{stem}' is a self-contained Windows (win-x64) build and cannot run on Linux. Publish it with "
                  + "'-r linux-x64' or as framework-dependent (no runtime identifier) and select that artifact."
                : $"'{stem}' is self-contained but its Linux executable '{stem}' is missing from the artifact.");
    }

    private sealed record RuntimeConfigInfo(bool SelfContained, bool IsAspNetCore, int? FrameworkMajor);

    private static RuntimeConfigInfo ReadRuntimeConfig(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (!document.RootElement.TryGetProperty("runtimeOptions", out var options))
                return new RuntimeConfigInfo(false, false, null);

            // Framework-dependent: "framework" or "frameworks"; self-contained: "includedFrameworks"
            var selfContained = options.TryGetProperty("includedFrameworks", out var included);
            var frameworks = new List<JsonElement>();
            if (selfContained) frameworks.AddRange(included.EnumerateArray());
            if (options.TryGetProperty("framework", out var single)) frameworks.Add(single);
            if (options.TryGetProperty("frameworks", out var many)) frameworks.AddRange(many.EnumerateArray());

            var names = frameworks
                .Select(f => (Name: f.TryGetProperty("name", out var n) ? n.GetString() : null,
                              Version: f.TryGetProperty("version", out var v) ? v.GetString() : null))
                .ToList();
            var aspNet = names.Any(f => string.Equals(f.Name, "Microsoft.AspNetCore.App", StringComparison.OrdinalIgnoreCase));
            var version = names.Select(f => f.Version).FirstOrDefault(v => !string.IsNullOrEmpty(v));
            int? major = version != null && int.TryParse(version.Split('.')[0], out var m) ? m : null;
            return new RuntimeConfigInfo(selfContained, aspNet, major);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or IOException)
        {
            return new RuntimeConfigInfo(false, false, null);
        }
    }
}
