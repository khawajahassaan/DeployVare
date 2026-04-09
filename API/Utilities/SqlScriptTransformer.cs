using System.Text.RegularExpressions;

namespace API.Utilities;

public static class SqlScriptTransformer
{
    /// <summary>
    /// Transforms CREATE PROCEDURE/FUNCTION or ALTER PROCEDURE/FUNCTION to CREATE OR ALTER
    /// This makes the script idempotent (works whether the object exists or not)
    /// </summary>
    public static string TransformToCreateOrAlter(string script)
    {
        if (string.IsNullOrWhiteSpace(script))
            return script;

        // Patterns to match CREATE/ALTER PROCEDURE/FUNCTION
        var patterns = new[]
        {
            new { Pattern = @"\bCREATE\s+PROCEDURE\b", Replacement = "CREATE OR ALTER PROCEDURE" },
            new { Pattern = @"\bALTER\s+PROCEDURE\b", Replacement = "CREATE OR ALTER PROCEDURE" },
            new { Pattern = @"\bCREATE\s+PROC\b", Replacement = "CREATE OR ALTER PROC" },
            new { Pattern = @"\bALTER\s+PROC\b", Replacement = "CREATE OR ALTER PROC" },
            new { Pattern = @"\bCREATE\s+FUNCTION\b", Replacement = "CREATE OR ALTER FUNCTION" },
            new { Pattern = @"\bALTER\s+FUNCTION\b", Replacement = "CREATE OR ALTER FUNCTION" }
        };

        // Replace first occurrence only (the actual CREATE/ALTER statement)
        foreach (var pattern in patterns)
        {
            var regex = new Regex(pattern.Pattern, RegexOptions.IgnoreCase);
            if (regex.IsMatch(script))
            {
                // Replace only the first occurrence
                return regex.Replace(script, pattern.Replacement, 1);
            }
        }

        return script;
    }

    /// <summary>
    /// Extracts the object name from a CREATE/ALTER PROCEDURE/FUNCTION statement
    /// </summary>
    public static string ExtractObjectName(string script)
    {
        if (string.IsNullOrWhiteSpace(script))
            return string.Empty;

        // Match CREATE/ALTER PROCEDURE/FUNCTION followed by the object name
        var pattern = @"(?:CREATE|ALTER)\s+(?:OR\s+ALTER\s+)?(?:PROCEDURE|PROC|FUNCTION)\s+(?:\[?(?<schema>\w+)\]?\.)?\[?(?<name>\w+)\]?";
        var match = Regex.Match(script, pattern, RegexOptions.IgnoreCase);

        if (match.Success)
        {
            var schema = match.Groups["schema"].Success ? match.Groups["schema"].Value : "dbo";
            var name = match.Groups["name"].Value;
            return $"{schema}.{name}";
        }

        return string.Empty;
    }
}




