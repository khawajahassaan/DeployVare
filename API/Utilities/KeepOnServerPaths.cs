namespace API.Utilities;

/// <summary>
/// Files and folders (relative to the site folder) that a deployment keeps on the server when they already
/// exist there, such as appsettings.json and upload folders.
/// </summary>
public static class KeepOnServerPaths
{
    public const int MaxEntries = 50;
    private static readonly char[] Separators = { ';', '\n', '\r' };
    private static readonly char[] InvalidChars = { '*', '?', ':', '"', '<', '>', '|' };

    /// <summary>Entries from a stored or entered list (no validation).</summary>
    public static List<string> Parse(string? value) => (value ?? string.Empty)
        .Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .ToList();

    /// <summary>Validates and normalizes a list; returns the ";"-separated form, or null when empty.</summary>
    public static string? Normalize(string? value)
    {
        var entries = new List<string>();

        foreach (var raw in Parse(value))
        {
            var entry = raw.Replace('\\', '/').Trim('/');

            var invalid = entry.Length == 0
                || entry.Length > 260
                || entry.Any(char.IsControl)
                || entry.IndexOfAny(InvalidChars) >= 0
                || entry.Split('/').Any(segment => segment.Trim() is "" or "." or "..");

            if (invalid)
                throw new InvalidOperationException(
                    $"'{raw}' is not a valid path to keep. Use a file or folder relative to the site folder, e.g. appsettings.json or wwwroot.");

            if (!entries.Contains(entry, StringComparer.OrdinalIgnoreCase))
                entries.Add(entry);
        }

        if (entries.Count > MaxEntries)
            throw new InvalidOperationException($"At most {MaxEntries} paths can be kept on the server.");

        return entries.Count == 0 ? null : string.Join(";", entries);
    }
}
