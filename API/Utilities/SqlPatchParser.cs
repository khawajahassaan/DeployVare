namespace API.Utilities;

public static class SqlPatchParser
{
    public static SqlPatchInfo? ParsePatchFileName(string fileName)
    {
        // Expected format: {PatchNumber}_{Description}_{Author}.sql
        // Example: 111_Alter_Expenses_Table_MuhammadSaad.sql

        // Remove .sql extension
        var nameWithoutExtension = fileName.Replace(".sql", "", StringComparison.OrdinalIgnoreCase);
        
        // Split by underscore
        var parts = nameWithoutExtension.Split('_');

        if (parts.Length < 3)
        {
            return null; // Invalid format
        }

        // First part is patch number
        if (!int.TryParse(parts[0], out int patchNumber))
        {
            return null; // Invalid patch number
        }

        // Last part is author
        var author = parts[^1];

        // Everything in between is description
        var description = string.Join(" ", parts.Skip(1).Take(parts.Length - 2));

        return new SqlPatchInfo
        {
            PatchNumber = patchNumber,
            FileName = fileName,
            Description = description,
            Author = author,
            Script = string.Empty // Will be filled by caller
        };
    }
}

public class SqlPatchInfo
{
    public int PatchNumber { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Script { get; set; } = string.Empty;
}

public class SqlScriptInfo
{
    public string Name { get; set; } = string.Empty;
    public string Script { get; set; } = string.Empty;
}




