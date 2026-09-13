using System.Diagnostics;

namespace Junction.Git;

/// <summary>
/// Hands a conflicted file to a three-way merge tool: the base, ours and theirs sides come
/// out of the index (stages 1, 2 and 3) into temp files, the merged side is the working
/// file itself, and the tool is run with the four in the order its preset expects. Waits
/// for the tool, so the caller can reload the file the tool wrote.
/// </summary>
public static class ExternalMergeLauncher
{
    /// <summary>The program and argument template a preset stands for; null for None.</summary>
    public static (string Program, string Args)? Command(ExternalToolOptions options) => options.MergeTool switch
    {
        MergeToolPreset.VSCode => ("code", "--wait --merge {local} {remote} {base} {merged}"),
        MergeToolPreset.BeyondCompare => ("bcompare", "{local} {remote} {base} {merged}"),
        MergeToolPreset.Meld => ("meld", "{local} {base} {remote} -o {merged}"),
        MergeToolPreset.KDiff3 => ("kdiff3", "{base} {local} {remote} -o {merged}"),
        MergeToolPreset.Custom when options.ExternalMergeToolPath.Trim().Length > 0 =>
            (options.ExternalMergeToolPath.Trim(),
                options.ExternalMergeToolArgs.Trim().Length > 0 ? options.ExternalMergeToolArgs.Trim() : "{base} {local} {remote} {merged}"),
        _ => null,
    };

    public static string Name(ExternalToolOptions options) => options.MergeTool switch
    {
        MergeToolPreset.VSCode => "VS Code",
        MergeToolPreset.BeyondCompare => "Beyond Compare",
        MergeToolPreset.Meld => "Meld",
        MergeToolPreset.KDiff3 => "KDiff3",
        MergeToolPreset.Custom => "the merge tool",
        _ => "",
    };

    /// <summary>Runs the tool on <paramref name="relativePath"/> and waits for it to close.
    /// Runs off the UI thread — it blocks for as long as the tool is open.</summary>
    public static bool TryRun(ExternalToolOptions options, GitRepository repo, string relativePath, out string error)
    {
        error = "";
        if (Command(options) is not { } command)
        {
            error = "Choose a merge tool in Options › External tools first.";
            return false;
        }

        var tempDir = Directory.CreateTempSubdirectory("junction-merge-");
        var safeName = Path.GetFileName(relativePath);
        // A side missing from the index (an add/add conflict has no base) is an empty file:
        // the tool still gets three inputs and a target.
        var basePath = Write(tempDir, "base-" + safeName, repo.GetFileText(":1", relativePath) ?? "");
        var localPath = Write(tempDir, "ours-" + safeName, repo.GetFileText(":2", relativePath) ?? "");
        var remotePath = Write(tempDir, "theirs-" + safeName, repo.GetFileText(":3", relativePath) ?? "");
        var mergedPath = Path.Combine(repo.Path, relativePath.Replace('/', Path.DirectorySeparatorChar));

        var replacements = new Dictionary<string, string>
        {
            ["{base}"] = basePath,
            ["{local}"] = localPath,
            ["{remote}"] = remotePath,
            ["{merged}"] = mergedPath,
            ["{repo}"] = repo.Path,
            ["{path}"] = relativePath,
        };
        var psi = new ProcessStartInfo
        {
            WorkingDirectory = repo.Path,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        var arguments = ExternalDiffLauncher.SplitArguments(ExternalDiffLauncher.Expand(command.Args, replacements)).ToList();
        ExternalDiffLauncher.ConfigureExecutable(psi, command.Program, arguments);
        try
        {
            using var process = Process.Start(psi);
            if (process == null) { error = "Could not start the merge tool."; return false; }
            process.WaitForExit();
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
        finally
        {
            try { tempDir.Delete(recursive: true); } catch { }
        }
    }

    private static string Write(DirectoryInfo dir, string name, string content)
    {
        var path = Path.Combine(dir.FullName, name);
        File.WriteAllText(path, content);
        return path;
    }
}
