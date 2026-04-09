namespace API.Utilities;

public static class DiffGenerator
{
    public static List<string> GenerateLineDiff(string? oldText, string? newText)
    {
        var changes = new List<string>();

        if (string.IsNullOrEmpty(oldText) && !string.IsNullOrEmpty(newText))
        {
            // All lines added
            var lines = newText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            foreach (var line in lines)
            {
                changes.Add($"+ {line}");
            }
            return changes;
        }

        if (!string.IsNullOrEmpty(oldText) && string.IsNullOrEmpty(newText))
        {
            // All lines removed
            var lines = oldText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            foreach (var line in lines)
            {
                changes.Add($"- {line}");
            }
            return changes;
        }

        if (string.IsNullOrEmpty(oldText) && string.IsNullOrEmpty(newText))
        {
            return changes;
        }

        // Both exist - do line-by-line diff
        var oldLines = oldText!.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var newLines = newText!.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

        // Simple diff algorithm (Myers' diff algorithm would be better, but this works for basic comparison)
        var lcs = LongestCommonSubsequence(oldLines, newLines);
        
        int oldIndex = 0;
        int newIndex = 0;
        int lcsIndex = 0;

        while (oldIndex < oldLines.Length || newIndex < newLines.Length)
        {
            if (lcsIndex < lcs.Count)
            {
                // Find next common line using the same trim-based comparison used by LCS building
                var commonLineTrimmed = lcs[lcsIndex].Trim();

                // Add all removed lines before common line
                while (oldIndex < oldLines.Length && oldLines[oldIndex].Trim() != commonLineTrimmed)
                {
                    changes.Add($"- {oldLines[oldIndex]}");
                    oldIndex++;
                }

                // Add all added lines before common line
                while (newIndex < newLines.Length && newLines[newIndex].Trim() != commonLineTrimmed)
                {
                    changes.Add($"+ {newLines[newIndex]}");
                    newIndex++;
                }

                // Add common line
                if (oldIndex < oldLines.Length && newIndex < newLines.Length)
                {
                    changes.Add($"  {oldLines[oldIndex]}");
                    oldIndex++;
                    newIndex++;
                    lcsIndex++;
                }
            }
            else
            {
                // No more common lines - add remaining as removed/added
                if (oldIndex < oldLines.Length)
                {
                    changes.Add($"- {oldLines[oldIndex]}");
                    oldIndex++;
                }
                else if (newIndex < newLines.Length)
                {
                    changes.Add($"+ {newLines[newIndex]}");
                    newIndex++;
                }
            }
        }

        return changes;
    }

    private static List<string> LongestCommonSubsequence(string[] oldLines, string[] newLines)
    {
        int m = oldLines.Length;
        int n = newLines.Length;
        int[,] dp = new int[m + 1, n + 1];

        // Build LCS table
        for (int i = 1; i <= m; i++)
        {
            for (int j = 1; j <= n; j++)
            {
                if (oldLines[i - 1].Trim() == newLines[j - 1].Trim())
                {
                    dp[i, j] = dp[i - 1, j - 1] + 1;
                }
                else
                {
                    dp[i, j] = Math.Max(dp[i - 1, j], dp[i, j - 1]);
                }
            }
        }

        // Backtrack to find LCS
        var lcs = new List<string>();
        int x = m, y = n;

        while (x > 0 && y > 0)
        {
            if (oldLines[x - 1].Trim() == newLines[y - 1].Trim())
            {
                lcs.Insert(0, oldLines[x - 1]);
                x--;
                y--;
            }
            else if (dp[x - 1, y] > dp[x, y - 1])
            {
                x--;
            }
            else
            {
                y--;
            }
        }

        return lcs;
    }
}

