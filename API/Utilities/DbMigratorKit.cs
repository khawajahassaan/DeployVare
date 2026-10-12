using System.Text.Json;

namespace API.Utilities;

/// <summary>
/// Copies DbMigrator out of a published API folder for the database kit of a three-tier package.
/// The API project references DbMigrator, so DbMigrator.exe ships inside the API build next to every
/// API assembly. Only DbMigrator and the assemblies its .deps.json lists are copied, so the database
/// server gets a few megabytes instead of the whole web application.
/// </summary>
public static class DbMigratorKit
{
    public const string ExeName = "DbMigrator.exe";

    public record Result(bool Found, bool Trimmed, int FileCount);

    public static Result Copy(string appDirectory, string destination)
    {
        var exe = FindExe(appDirectory);
        if (exe == null) return new Result(false, false, 0);

        var baseDir = Path.GetDirectoryName(exe)!;
        var files = TrimmedFileList(baseDir);
        var trimmed = files != null;

        // Without a usable dependency list (the API publish does not carry DbMigrator.deps.json), take what the
        // .NET host can load from the folder: DbMigrator itself, every assembly next to it and the native
        // runtimes. The web application's content (wwwroot, templates, upload folders) is left behind.
        files ??= LoadableFiles(baseDir);

        foreach (var relative in files)
        {
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(Path.Combine(baseDir, relative), target, overwrite: true);
        }

        return new Result(true, trimmed, files.Count);
    }

    private static List<string> LoadableFiles(string baseDir)
    {
        var top = Directory.EnumerateFiles(baseDir, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => name!.StartsWith("DbMigrator.", StringComparison.OrdinalIgnoreCase)
                           || name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(name => name!);
        var runtimes = Path.Combine(baseDir, "runtimes");
        var native = Directory.Exists(runtimes)
            ? Directory.EnumerateFiles(runtimes, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(baseDir, f))
            : Enumerable.Empty<string>();
        return top.Concat(native).ToList();
    }

    private static string? FindExe(string appDirectory)
    {
        var top = Path.Combine(appDirectory, ExeName);
        if (File.Exists(top)) return top;
        return Directory.EnumerateFiles(appDirectory, ExeName, SearchOption.AllDirectories).FirstOrDefault();
    }

    /// <summary>
    /// Relative paths DbMigrator needs, from DbMigrator.deps.json; null when the list is missing. An assembly the list
    /// names but the folder lacks is skipped, not fatal: the API publish leaves out what ASP.NET Core already ships
    /// (Microsoft.Extensions.*, EventLog, Pkcs), and DbMigrator runs without them (they load only on demand), exactly
    /// as it does from the API folder itself. Copying the whole folder instead added hundreds of MB of API files.
    /// </summary>
    private static List<string>? TrimmedFileList(string baseDir)
    {
        var depsPath = Path.Combine(baseDir, "DbMigrator.deps.json");
        var runtimeConfig = Path.Combine(baseDir, "DbMigrator.runtimeconfig.json");
        if (!File.Exists(depsPath) || !File.Exists(runtimeConfig)) return null;

        using var deps = JsonDocument.Parse(File.ReadAllText(depsPath));
        if (!deps.RootElement.TryGetProperty("targets", out var targets)) return null;
        var target = targets.EnumerateObject().FirstOrDefault().Value;
        if (target.ValueKind != JsonValueKind.Object) return null;

        var libraries = target.EnumerateObject().ToDictionary(p => p.Name, p => p.Value, StringComparer.OrdinalIgnoreCase);
        var root = libraries.Keys.FirstOrDefault(k => k.StartsWith("DbMigrator/", StringComparison.OrdinalIgnoreCase));
        if (root == null) return null;

        // Dependency closure from DbMigrator itself
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var name = pending.Pop();
            if (!visited.Add(name) || !libraries.TryGetValue(name, out var library)) continue;
            if (!library.TryGetProperty("dependencies", out var dependencies)) continue;
            foreach (var dependency in dependencies.EnumerateObject())
                pending.Push($"{dependency.Name}/{dependency.Value.GetString()}");
        }

        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ExeName, "DbMigrator.dll", "DbMigrator.deps.json", "DbMigrator.runtimeconfig.json"
        };

        foreach (var name in visited)
        {
            var library = libraries[name];

            // Managed and native assemblies are published flat into the application folder
            foreach (var section in new[] { "runtime", "native" })
            {
                if (!library.TryGetProperty(section, out var assets)) continue;
                foreach (var asset in assets.EnumerateObject())
                {
                    var fileName = Path.GetFileName(asset.Name);
                    if (File.Exists(Path.Combine(baseDir, fileName))) files.Add(fileName);
                }
            }

            // RID-specific assets keep their runtimes/<rid>/... path in a portable publish; a RID-specific
            // publish flattens them instead. Either way, copy what is present.
            if (library.TryGetProperty("runtimeTargets", out var runtimeTargets))
            {
                foreach (var asset in runtimeTargets.EnumerateObject())
                {
                    var relative = asset.Name.Replace('/', Path.DirectorySeparatorChar);
                    if (File.Exists(Path.Combine(baseDir, relative))) files.Add(relative);
                    else if (File.Exists(Path.Combine(baseDir, Path.GetFileName(relative)))) files.Add(Path.GetFileName(relative));
                }
            }
        }

        return files.Where(f => File.Exists(Path.Combine(baseDir, f))).ToList();
    }
}
