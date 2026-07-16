using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GitClient.Platform;

/// <summary>
/// Shells a native OS "save as" dialog, same strategy as <see cref="NativeFolderPicker"/>:
/// Windows SaveFileDialog via <c>pwsh -STA</c>, macOS <c>choose file name</c>,
/// Linux zenity/kdialog. The chosen path is read from the child's stdout.
/// </summary>
public sealed class NativeSaveFilePicker : ISaveFilePicker
{
    private sealed record PickerCommand(string Executable,
        Func<string, string?, string?, string?, IEnumerable<string>> BuildArguments);

    private readonly PickerCommand? _command;

    public NativeSaveFilePicker()
    {
        _command = ResolveCommand();
    }

    public bool IsAvailable => _command != null;

    public async Task<string?> PickSaveFileAsync(string suggestedFileName, string? initialDirectory = null,
        string? filterLabel = null, string? extension = null, CancellationToken cancellationToken = default)
    {
        if (_command == null)
            return null;

        var psi = new ProcessStartInfo(_command.Executable)
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        var dir = NormalizeInitialDirectory(initialDirectory);
        foreach (var arg in _command.BuildArguments(suggestedFileName, dir, filterLabel, extension))
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi);
        if (process == null)
            return null;

        // Cancellation must close the OS dialog, not just abandon the await.
        await using var killOnCancel = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) { }
        });

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
            return null;

        var selected = (await stdoutTask).Trim();
        return string.IsNullOrWhiteSpace(selected) ? null : selected;
    }

    private static string? NormalizeInitialDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        try
        {
            return Directory.Exists(path) ? Path.GetFullPath(path) : null;
        }
        catch
        {
            return null;
        }
    }

    private static PickerCommand? ResolveCommand()
    {
        if (OperatingSystem.IsWindows())
        {
            var powershell = FindExecutable("pwsh") ?? FindExecutable("powershell");
            return powershell == null ? null : new PickerCommand(powershell, BuildWindowsArguments);
        }

        if (OperatingSystem.IsMacOS())
        {
            var osascript = FindExecutable("osascript");
            return osascript == null ? null : new PickerCommand(osascript, BuildMacArguments);
        }

        if (OperatingSystem.IsLinux())
        {
            if (FindExecutable("zenity") is { } zenity)
                return new PickerCommand(zenity, BuildZenityArguments);
            if (FindExecutable("kdialog") is { } kdialog)
                return new PickerCommand(kdialog, BuildKDialogArguments);
        }

        return null;
    }

    private static IEnumerable<string> BuildWindowsArguments(
        string fileName, string? initialDirectory, string? filterLabel, string? extension)
    {
        var filter = extension == null
            ? "All files (*.*)|*.*"
            : $"{filterLabel ?? extension.ToUpperInvariant() + " files"} (*.{extension})|*.{extension}|All files (*.*)|*.*";
        var initialDirLine = initialDirectory == null
            ? ""
            : $"$dialog.InitialDirectory = '{EscapePowerShellSingleQuotedString(initialDirectory)}'; ";
        var defaultExtLine = extension == null
            ? ""
            : $"$dialog.DefaultExt = '{EscapePowerShellSingleQuotedString(extension)}'; ";
        var script =
            "Add-Type -AssemblyName System.Windows.Forms; " +
            "$dialog = New-Object System.Windows.Forms.SaveFileDialog; " +
            $"$dialog.FileName = '{EscapePowerShellSingleQuotedString(fileName)}'; " +
            $"$dialog.Filter = '{EscapePowerShellSingleQuotedString(filter)}'; " +
            initialDirLine + defaultExtLine +
            "$dialog.OverwritePrompt = $true; " +
            "if ($dialog.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) { " +
            "[Console]::Out.Write($dialog.FileName) }";

        return ["-NoProfile", "-STA", "-Command", script];
    }

    private static IEnumerable<string> BuildMacArguments(
        string fileName, string? initialDirectory, string? filterLabel, string? extension)
    {
        var name = EscapeAppleScriptString(fileName);
        var location = initialDirectory == null
            ? ""
            : $" default location POSIX file \"{EscapeAppleScriptString(initialDirectory)}\"";
        return [
            "-e",
            $"set selectedFile to choose file name with prompt \"Save as\" default name \"{name}\"{location}",
            "-e",
            "POSIX path of selectedFile"
        ];
    }

    private static IEnumerable<string> BuildZenityArguments(
        string fileName, string? initialDirectory, string? filterLabel, string? extension)
    {
        var args = new List<string>
        {
            "--file-selection",
            "--save",
            "--title=Save as",
            "--filename=" + (initialDirectory == null
                ? fileName
                : Path.Combine(initialDirectory, fileName)),
        };
        if (extension != null)
            args.Add($"--file-filter={filterLabel ?? extension} | *.{extension}");
        return args;
    }

    private static IEnumerable<string> BuildKDialogArguments(
        string fileName, string? initialDirectory, string? filterLabel, string? extension)
    {
        var start = initialDirectory == null ? fileName : Path.Combine(initialDirectory, fileName);
        return ["--getsavefilename", start, "--title", "Save as"];
    }

    private static string? FindExecutable(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var extensions = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD")
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            : [""];

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var ext in extensions)
            {
                var candidate = Path.Combine(directory, name + ext);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }

    private static string EscapePowerShellSingleQuotedString(string value) => value.Replace("'", "''");

    private static string EscapeAppleScriptString(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
