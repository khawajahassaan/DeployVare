using System.IO.Compression;
using System.Security.Cryptography;
using API.Models.Enums;
using API.Services;

namespace API.Utilities;

public sealed record ArtifactPackageFile(string RelativePath, long Size, string Sha256);

/// <summary>
/// A downloaded artifact prepared for the existing IIS deployment engine.
/// </summary>
public sealed class ArtifactPackage
{
    /// <summary>Folder whose contents are deployed into the server's deployment path.</summary>
    public string ContentDirectory { get; init; } = string.Empty;

    /// <summary>ZIP of <see cref="ContentDirectory"/> (no base folder), ready to upload and expand remotely.</summary>
    public string PackageZipPath { get; init; } = string.Empty;

    public List<ArtifactPackageFile> Files { get; init; } = new();
    public List<string> Warnings { get; init; } = new();
    public bool ContainsWebConfig { get; init; }
}

/// <summary>
/// Safely extracts a build artifact ZIP into a workspace, selects the deployable folder and validates it.
/// </summary>
public static class ArtifactPackageBuilder
{
    public const long DefaultMaxExtractedBytes = 4L * 1024 * 1024 * 1024; // 4 GB
    public const int DefaultMaxEntries = 50_000;

    public static ArtifactPackage Build(
        string artifactZipPath,
        string workspaceDirectory,
        string artifactName,
        ServerType serverType,
        string? artifactSubfolder,
        long maxExtractedBytes = DefaultMaxExtractedBytes,
        int maxEntries = DefaultMaxEntries)
    {
        var extractRoot = Path.Combine(workspaceDirectory, "extracted");
        SafeExtract(artifactZipPath, extractRoot, maxExtractedBytes, maxEntries);

        // Azure DevOps artifact ZIPs wrap everything in a folder named after the artifact (e.g. "drop/")
        var contentRoot = extractRoot;
        var topFolder = Path.Combine(extractRoot, artifactName);
        if (Directory.GetFiles(extractRoot).Length == 0
            && Directory.GetDirectories(extractRoot).Length == 1
            && Directory.Exists(topFolder))
        {
            contentRoot = topFolder;
        }

        if (!string.IsNullOrWhiteSpace(artifactSubfolder))
        {
            var selected = SafeCombine(contentRoot, artifactSubfolder.Trim().Trim('/', '\\'));
            if (!Directory.Exists(selected))
            {
                var available = Directory.GetDirectories(contentRoot).Select(Path.GetFileName).OrderBy(n => n).ToList();
                throw new AzureDevOpsException(AzureDevOpsErrorCode.InvalidArtifact,
                    $"Folder '{artifactSubfolder}' was not found in artifact '{artifactName}'."
                    + (available.Count > 0 ? $" Available folders: {string.Join(", ", available)}." : string.Empty));
            }
            contentRoot = selected;
        }

        // A publish step with "zip after publish" produces a single ZIP; deploy its contents instead
        var rootFiles = Directory.GetFiles(contentRoot);
        if (rootFiles.Length == 1 && Directory.GetDirectories(contentRoot).Length == 0
            && rootFiles[0].EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            var nestedRoot = Path.Combine(workspaceDirectory, "nested");
            SafeExtract(rootFiles[0], nestedRoot, maxExtractedBytes, maxEntries);
            contentRoot = nestedRoot;
        }

        var files = Directory.GetFiles(contentRoot, "*", SearchOption.AllDirectories);
        if (files.Length == 0)
            throw new AzureDevOpsException(AzureDevOpsErrorCode.InvalidArtifact,
                $"Artifact '{artifactName}' contains no files to deploy" + (string.IsNullOrWhiteSpace(artifactSubfolder) ? "." : $" in folder '{artifactSubfolder}'."));

        var warnings = new List<string>();
        var rootFileNames = Directory.GetFiles(contentRoot).Select(Path.GetFileName).ToList();

        if (serverType == ServerType.Api)
        {
            // An ASP.NET application on IIS always has its assemblies at the site root
            if (!rootFileNames.Any(n => n!.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
                throw new AzureDevOpsException(AzureDevOpsErrorCode.InvalidArtifact,
                    "No application assemblies (.dll) were found at the root of the package. "
                    + "Check the artifact name and the folder-inside-artifact setting.");
        }
        else if (!rootFileNames.Any(n => n!.Equals("index.html", StringComparison.OrdinalIgnoreCase)))
        {
            warnings.Add("No index.html found at the root of the frontend package.");
        }

        var packageFiles = files
            .Select(f => new ArtifactPackageFile(
                Path.GetRelativePath(contentRoot, f).Replace('\\', '/'),
                new FileInfo(f).Length,
                ComputeSha256(f)))
            .OrderBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var packageZipPath = Path.Combine(workspaceDirectory, "package.zip");
        ZipFile.CreateFromDirectory(contentRoot, packageZipPath, CompressionLevel.Fastest, includeBaseDirectory: false);

        return new ArtifactPackage
        {
            ContentDirectory = contentRoot,
            PackageZipPath = packageZipPath,
            Files = packageFiles,
            Warnings = warnings,
            ContainsWebConfig = rootFileNames.Any(n => n!.Equals("web.config", StringComparison.OrdinalIgnoreCase))
        };
    }

    /// <summary>
    /// Prepares a SQL scripts artifact (e.g. "sqlscripts", the SQLScripts_New folder) for DbMigrator: extracts it
    /// safely, removes the artifact's root folder and re-zips the content.
    /// </summary>
    public static (string ContentDirectory, string PackageZipPath, int SqlFileCount) BuildScriptsPackage(
        string artifactZipPath,
        string workspaceDirectory,
        string artifactName,
        long maxExtractedBytes = DefaultMaxExtractedBytes,
        int maxEntries = DefaultMaxEntries)
    {
        var extractRoot = Path.Combine(workspaceDirectory, "sqlscripts-extracted");
        SafeExtract(artifactZipPath, extractRoot, maxExtractedBytes, maxEntries);

        var contentRoot = extractRoot;
        var topFolder = Path.Combine(extractRoot, artifactName);
        if (Directory.GetFiles(extractRoot).Length == 0
            && Directory.GetDirectories(extractRoot).Length == 1
            && Directory.Exists(topFolder))
        {
            contentRoot = topFolder;
        }

        var sqlFileCount = Directory.GetFiles(contentRoot, "*.sql", SearchOption.AllDirectories).Length;
        if (sqlFileCount == 0)
            throw new AzureDevOpsException(AzureDevOpsErrorCode.InvalidArtifact, $"Artifact '{artifactName}' contains no .sql scripts.");

        var packageZipPath = Path.Combine(workspaceDirectory, "sqlscripts.zip");
        ZipFile.CreateFromDirectory(contentRoot, packageZipPath, CompressionLevel.Fastest, includeBaseDirectory: false);

        return (contentRoot, packageZipPath, sqlFileCount);
    }

    /// <summary>
    /// Extracts a ZIP, rejecting entries that would escape the destination (path traversal) and
    /// archives that exceed the entry-count or size limits (ZIP bombs).
    /// </summary>
    public static void SafeExtract(string zipPath, string destinationDirectory, long maxExtractedBytes, int maxEntries)
    {
        Directory.CreateDirectory(destinationDirectory);
        var destinationRoot = Path.GetFullPath(destinationDirectory);

        try
        {
            using var archive = ZipFile.OpenRead(zipPath);

            if (archive.Entries.Count > maxEntries)
                throw new AzureDevOpsException(AzureDevOpsErrorCode.InvalidArtifact,
                    $"Artifact contains {archive.Entries.Count} entries, above the limit of {maxEntries}.");

            long totalBytes = 0;
            foreach (var entry in archive.Entries)
            {
                var targetPath = SafeCombine(destinationRoot, entry.FullName);

                if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
                {
                    Directory.CreateDirectory(targetPath);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

                using var source = entry.Open();
                using var target = new FileStream(targetPath, FileMode.CreateNew, FileAccess.Write);

                // Count actual bytes written; the declared entry length can be forged
                var buffer = new byte[81920];
                int read;
                while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                {
                    totalBytes += read;
                    if (totalBytes > maxExtractedBytes)
                        throw new AzureDevOpsException(AzureDevOpsErrorCode.InvalidArtifact,
                            $"Artifact expands beyond the {maxExtractedBytes / (1024 * 1024)} MB extraction limit.");
                    target.Write(buffer, 0, read);
                }
            }
        }
        catch (InvalidDataException ex)
        {
            throw new AzureDevOpsException(AzureDevOpsErrorCode.InvalidArtifact, "The artifact is not a valid ZIP archive or is corrupt.", ex);
        }
        catch (IOException ex) when (ex is not FileNotFoundException)
        {
            throw new AzureDevOpsException(AzureDevOpsErrorCode.InvalidArtifact, $"The artifact could not be extracted: {ex.Message}", ex);
        }
    }

    /// <summary>Combines a relative path with a root, throwing if the result escapes the root.</summary>
    public static string SafeCombine(string rootDirectory, string relativePath)
    {
        var root = Path.GetFullPath(rootDirectory);
        var normalized = relativePath.Replace('\\', '/');

        if (Path.IsPathRooted(normalized) || normalized.Split('/').Any(segment => segment == ".."))
            throw new AzureDevOpsException(AzureDevOpsErrorCode.InvalidArtifact, $"Rejected unsafe path '{relativePath}' in artifact.");

        var combined = Path.GetFullPath(Path.Combine(root, normalized));
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        if (!combined.Equals(root, comparison) && !combined.StartsWith(rootWithSeparator, comparison))
            throw new AzureDevOpsException(AzureDevOpsErrorCode.InvalidArtifact, $"Rejected unsafe path '{relativePath}' in artifact.");

        return combined;
    }

    private static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
