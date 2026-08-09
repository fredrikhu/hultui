using System.Diagnostics;
using Junction;

namespace Junction.Git;

public sealed class ExternalDiffRequest
{
    public required string RepoPath { get; init; }
    public required string RelativePath { get; init; }
    public required string LeftLabel { get; init; }
    public required string LeftContent { get; init; }
    public required string RightLabel { get; init; }
    public required string RightContent { get; init; }
}

public static class ExternalDiffLauncher
{
    public static bool TryLaunch(ExternalToolOptions options, ExternalDiffRequest request, out string error)
    {
        error = "";

        var executable = options.ExternalDiffToolPath.Trim();
        if (string.IsNullOrWhiteSpace(executable))
        {
            error = "Configure an external diff executable first.";
            return false;
        }

        var argsTemplate = string.IsNullOrWhiteSpace(options.ExternalDiffToolArgs)
            ? "{left} {right}"
            : options.ExternalDiffToolArgs;

        var tempDir = Directory.CreateTempSubdirectory("hultgit-diff-");
        var safeName = SanitizeFileName(Path.GetFileName(request.RelativePath));
        var leftPath = Path.Combine(tempDir.FullName, $"{request.LeftLabel}-{safeName}");
        var rightPath = Path.Combine(tempDir.FullName, $"{request.RightLabel}-{safeName}");
        File.WriteAllText(leftPath, request.LeftContent);
        File.WriteAllText(rightPath, request.RightContent);

        var replacements = new Dictionary<string, string>
        {
            ["{repo}"] = request.RepoPath,
            ["{path}"] = request.RelativePath,
            ["{left}"] = leftPath,
            ["{right}"] = rightPath,
            ["{base}"] = leftPath,
            ["{merged}"] = rightPath,
        };

        var psi = new ProcessStartInfo
        {
            WorkingDirectory = request.RepoPath,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        var arguments = SplitArguments(Expand(argsTemplate, replacements)).ToList();
        ConfigureExecutable(psi, executable, arguments);

        try
        {
            var process = Process.Start(psi);
            if (process == null)
            {
                tempDir.Delete(recursive: true);
                error = "Could not start external diff tool.";
                return false;
            }

            process.EnableRaisingEvents = true;
            process.Exited += (_, _) =>
            {
                process.Dispose();
                TryDelete(tempDir.FullName);
            };
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or FileNotFoundException or System.ComponentModel.Win32Exception)
        {
            tempDir.Delete(recursive: true);
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Launches an editor/tool with the given arguments and no temp files — used to
    /// open a real file in place (e.g. a conflicted working-tree file in VS Code's
    /// inline merge editor). Resolves the executable against PATH/PATHEXT the same
    /// way as <see cref="TryLaunch"/> (so a bare "code" finds code.cmd on Windows).
    /// </summary>
    public static bool TryOpen(string workingDirectory, string executable,
        IReadOnlyList<string> arguments, out string error)
    {
        error = "";
        var exe = executable.Trim();
        if (string.IsNullOrWhiteSpace(exe))
        {
            error = "No editor configured.";
            return false;
        }

        var psi = new ProcessStartInfo
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        ConfigureExecutable(psi, exe, arguments.ToList());

        try
        {
            var process = Process.Start(psi);
            if (process == null)
            {
                error = "Could not start the editor.";
                return false;
            }
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) => process.Dispose();
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or FileNotFoundException or System.ComponentModel.Win32Exception)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Sets <see cref="ProcessStartInfo.FileName"/> and arguments, resolving the
    /// configured command against PATH/PATHEXT. A bare command like "code" is a
    /// <c>code.cmd</c> shim on Windows, and Win32 CreateProcess (which .NET uses
    /// when <see cref="ProcessStartInfo.UseShellExecute"/> is false) cannot run a
    /// .cmd/.bat directly nor apply PATHEXT — so those are routed through cmd.exe.
    /// </summary>
    private static void ConfigureExecutable(ProcessStartInfo psi, string executable, List<string> arguments)
    {
        var resolved = ResolveOnPath(executable) ?? executable;

        var isScript = OperatingSystem.IsWindows() &&
            (resolved.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) ||
             resolved.EndsWith(".bat", StringComparison.OrdinalIgnoreCase));

        if (isScript)
        {
            psi.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            // cmd /c strips the outermost pair of quotes, so wrap the whole command
            // in an extra pair — otherwise a script path containing spaces (e.g.
            // "...\Microsoft VS Code\bin\code.cmd") gets mangled when combined with
            // quoted arguments.
            var inner = string.Join(' ', new[] { resolved }.Concat(arguments).Select(QuoteArg));
            psi.Arguments = $"/c \"{inner}\"";
        }
        else
        {
            psi.FileName = resolved;
            foreach (var arg in arguments)
                psi.ArgumentList.Add(arg);
        }
    }

    /// <summary>
    /// Mimics a shell's PATH lookup: returns the full path to <paramref name="command"/>,
    /// trying each PATHEXT extension on Windows. Returns null if not found (the caller
    /// falls back to the raw name so the OS can still surface a sensible error).
    /// </summary>
    private static string? ResolveOnPath(string command)
    {
        if (command.Contains(Path.DirectorySeparatorChar) || command.Contains(Path.AltDirectorySeparatorChar))
            return File.Exists(command) ? command : null;

        var pathDirs = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var extensions = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD")
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [""];

        foreach (var dir in pathDirs)
        {
            // Only match a bare (extensionless) file when the command already
            // carries an extension. On Windows an extensionless PATH entry isn't
            // executable via CreateProcess — VS Code ships both a `code` bash
            // script and a runnable `code.cmd`, and we must pick the latter.
            if (Path.HasExtension(command))
            {
                var direct = Path.Combine(dir, command);
                if (File.Exists(direct))
                    return direct;
            }

            foreach (var ext in extensions)
            {
                var candidate = Path.Combine(dir, command + ext);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }

    private static string QuoteArg(string arg)
        => arg.Length > 0 && !arg.Contains(' ') && !arg.Contains('"')
            ? arg
            : "\"" + arg.Replace("\"", "\\\"") + "\"";

    private static string Expand(string template, Dictionary<string, string> replacements)
    {
        var result = template;
        foreach (var replacement in replacements)
            result = result.Replace(replacement.Key, replacement.Value, StringComparison.Ordinal);
        return result;
    }

    private static IEnumerable<string> SplitArguments(string text)
    {
        var args = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(ch) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    args.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0)
            args.Add(current.ToString());

        return args;
    }

    private static string SanitizeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return "file";

        foreach (var invalid in Path.GetInvalidFileNameChars())
            fileName = fileName.Replace(invalid, '_');

        return fileName;
    }

    private static void TryDelete(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Temp cleanup is best-effort.
        }
    }
}
