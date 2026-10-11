using System.Text.RegularExpressions;
using API.Services;

namespace API.Models;

/// <summary>
/// Validated Azure DevOps access details. ToString() never includes the token, so the
/// object is safe to pass to structured logging.
/// </summary>
public sealed class AzureDevOpsCredentials
{
    private static readonly Regex OrganizationPattern = new(@"^[A-Za-z0-9](?:[A-Za-z0-9-]{0,48}[A-Za-z0-9])?$", RegexOptions.Compiled);
    private static readonly char[] InvalidNameChars = { '/', '\\', '?', '#', '%', ':', '*', '"', '<', '>', '|' };

    public string Organization { get; }
    public string Project { get; }
    public string PersonalAccessToken { get; }

    private AzureDevOpsCredentials(string organization, string project, string personalAccessToken)
    {
        Organization = organization;
        Project = project;
        PersonalAccessToken = personalAccessToken;
    }

    public static AzureDevOpsCredentials Create(string? organization, string? project, string? personalAccessToken)
    {
        var org = NormalizeOrganization(organization);
        var proj = ValidateName(project, "Project");

        if (string.IsNullOrWhiteSpace(personalAccessToken))
            throw new AzureDevOpsException(AzureDevOpsErrorCode.InvalidConfiguration, "A personal access token is required.");

        return new AzureDevOpsCredentials(org, proj, personalAccessToken.Trim());
    }

    /// <summary>
    /// Accepts "Transviti", "https://dev.azure.com/Transviti[/Project]" or "https://transviti.visualstudio.com"
    /// and returns the organization name.
    /// </summary>
    public static string NormalizeOrganization(string? organization)
    {
        var value = organization?.Trim() ?? string.Empty;

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            if (uri.Scheme != Uri.UriSchemeHttps)
                throw new AzureDevOpsException(AzureDevOpsErrorCode.InvalidConfiguration, "Azure DevOps URLs must use HTTPS.");

            if (uri.Host.Equals("dev.azure.com", StringComparison.OrdinalIgnoreCase))
                value = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
            else if (uri.Host.EndsWith(".visualstudio.com", StringComparison.OrdinalIgnoreCase))
                value = uri.Host[..^".visualstudio.com".Length];
            else
                throw new AzureDevOpsException(AzureDevOpsErrorCode.InvalidConfiguration,
                    "Only Azure DevOps Services URLs (dev.azure.com or *.visualstudio.com) are supported.");

            value = Uri.UnescapeDataString(value);
        }

        if (!OrganizationPattern.IsMatch(value))
            throw new AzureDevOpsException(AzureDevOpsErrorCode.InvalidConfiguration,
                "Organization must be an Azure DevOps organization name (letters, digits and hyphens) or its dev.azure.com URL.");

        return value;
    }

    /// <summary>Validates a project or repository name.</summary>
    public static string ValidateName(string? name, string label)
    {
        var value = name?.Trim() ?? string.Empty;

        if (value.Length == 0)
            throw new AzureDevOpsException(AzureDevOpsErrorCode.InvalidConfiguration, $"{label} is required.");

        if (value.Length > 200 || value.IndexOfAny(InvalidNameChars) >= 0 || value.Any(char.IsControl))
            throw new AzureDevOpsException(AzureDevOpsErrorCode.InvalidConfiguration, $"{label} '{value}' contains invalid characters.");

        return value;
    }

    /// <summary>Returns the short branch name ("main") from "main" or "refs/heads/main".</summary>
    public static string NormalizeBranch(string? branch)
    {
        var value = branch?.Trim() ?? string.Empty;
        if (value.StartsWith("refs/heads/", StringComparison.OrdinalIgnoreCase))
            value = value["refs/heads/".Length..];

        if (value.Length == 0)
            throw new AzureDevOpsException(AzureDevOpsErrorCode.InvalidConfiguration, "Branch is required.");

        var invalid = value.Contains("..") || value.StartsWith('/') || value.EndsWith('/') || value.EndsWith(".lock")
            || value.Any(c => char.IsControl(c) || char.IsWhiteSpace(c) || "~^:?*[\\".Contains(c));

        if (invalid)
            throw new AzureDevOpsException(AzureDevOpsErrorCode.InvalidConfiguration, $"Branch '{value}' is not a valid Git branch name.");

        return value;
    }

    public override string ToString() => $"{Organization}/{Project}";
}
