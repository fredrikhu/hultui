using System.Diagnostics;
using GitClient;

namespace GitClient.Git;

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
            FileName = executable,
            WorkingDirectory = request.RepoPath,
            UseShellExecute = false,
        };

        foreach (var arg in SplitArguments(Expand(argsTemplate, replacements)))
            psi.ArgumentList.Add(arg);

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
