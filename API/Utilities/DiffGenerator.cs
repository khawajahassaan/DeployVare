namespace API.Utilities;

public static class DiffGenerator
{
    public static List<string> GenerateLineDiff(string? oldText, string? newText)
    {
        var changes = new List<string>();
        var sep = new[] { "\r\n", "\r", "\n" };

        if (string.IsNullOrEmpty(oldText) && string.IsNullOrEmpty(newText))
            return changes;

        if (string.IsNullOrEmpty(oldText))
        {
            foreach (var line in newText!.Split(sep, StringSplitOptions.None))
                changes.Add($"+ {line}");
            return changes;
        }

        if (string.IsNullOrEmpty(newText))
        {
            foreach (var line in oldText!.Split(sep, StringSplitOptions.None))
                changes.Add($"- {line}");
            return changes;
        }

        var oldLines = oldText.Split(sep, StringSplitOptions.None);
        var newLines = newText.Split(sep, StringSplitOptions.None);

        // LCS returns index pairs into (oldLines, newLines) — never loses sync on duplicate lines
        var matchedPairs = LcsIndexPairs(oldLines, newLines);

        int oi = 0, ni = 0;
        foreach (var (oldIdx, newIdx) in matchedPairs)
        {
            // Lines before the next common pair
            while (oi < oldIdx) { changes.Add($"- {oldLines[oi]}"); oi++; }
            while (ni < newIdx) { changes.Add($"+ {newLines[ni]}"); ni++; }

            // The common line itself
            changes.Add($"  {oldLines[oi]}");
            oi++;
            ni++;
        }

        // Tail — anything after the last matched pair
        while (oi < oldLines.Length) { changes.Add($"- {oldLines[oi]}"); oi++; }
        while (ni < newLines.Length) { changes.Add($"+ {newLines[ni]}"); ni++; }

        return changes;
    }

    /// <summary>
    /// Returns (oldIndex, newIndex) pairs representing the LCS, in ascending order.
    /// Tracking indices (not content) means duplicate lines like END/BEGIN/empty
    /// never cause positional sync-loss in the playback loop above.
    /// </summary>
    private static List<(int, int)> LcsIndexPairs(string[] a, string[] b)
    {
        int m = a.Length, n = b.Length;

        // For large inputs cap the DP matrix to avoid O(m*n) memory/time blowup.
        // If either side exceeds 1 000 lines, fall straight back to no-match so the
        // caller renders everything as added/removed (still correct, just no context).
        if ((long)m * n > 1_000_000)
            return new List<(int, int)>();

        // Build DP table on trimmed lines so whitespace-only differences are ignored
        var dp = new int[m + 1, n + 1];
        for (int i = 1; i <= m; i++)
        {
            var ai = a[i - 1].Trim();
            for (int j = 1; j <= n; j++)
            {
                dp[i, j] = ai == b[j - 1].Trim()
                    ? dp[i - 1, j - 1] + 1
                    : Math.Max(dp[i - 1, j], dp[i, j - 1]);
            }
        }

        // Backtrack to get index pairs (we insert at front so the result is ascending)
        var pairs = new List<(int, int)>();
        int x = m, y = n;
        while (x > 0 && y > 0)
        {
            if (a[x - 1].Trim() == b[y - 1].Trim())
            {
                pairs.Insert(0, (x - 1, y - 1));
                x--; y--;
            }
            else if (dp[x - 1, y] >= dp[x, y - 1])
                x--;
            else
                y--;
        }
        return pairs;
    }
}

