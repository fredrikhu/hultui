using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GitClient.Platform;

public sealed class NativeFolderPicker : IFolderPicker
{
    private readonly PickerCommand? _command;

    public NativeFolderPicker()
    {
        _command = ResolveCommand();
    }

    public bool IsAvailable => _command != null;

    public async Task<string?> PickFolderAsync(string? initialDirectory = null, CancellationToken cancellationToken = default)
    {
        if (_command == null)
            return null;

        var psi = new ProcessStartInfo(_command.Executable)
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var arg in _command.BuildArguments(NormalizeInitialDirectory(initialDirectory)))
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi);
        if (process == null)
            return null;

        // Make cancellation actually close the OS dialog. Without this, the
        // token would only abort our awaits — the picker process (zenity,
        // osascript, …) would keep running until the user dismissed it.
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
            if (Directory.Exists(path))
                return Path.GetFullPath(path);

            var parent = Path.GetDirectoryName(path);
            return !string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent)
                ? Path.GetFullPath(parent)
                : null;
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

    private static IEnumerable<string> BuildWindowsArguments(string? initialDirectory)
    {
        var selectedPathLine = initialDirectory == null
            ? ""
            : $"$dialog.SelectedPath = '{EscapePowerShellSingleQuotedString(initialDirectory)}';";
        var script =
            "Add-Type -AssemblyName System.Windows.Forms; " +
            "$dialog = New-Object System.Windows.Forms.FolderBrowserDialog; " +
            "$dialog.Description = 'Open Git repository'; " +
            "$dialog.ShowNewFolderButton = $false; " +
            selectedPathLine +
            "if ($dialog.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) { " +
            "[Console]::Out.Write($dialog.SelectedPath) }";

        return ["-NoProfile", "-STA", "-Command", script];
    }

    private static IEnumerable<string> BuildMacArguments(string? initialDirectory)
    {
        if (initialDirectory == null)
        {
            return [
                "-e",
                "set selectedFolder to choose folder with prompt \"Open Git repository\"",
                "-e",
                "POSIX path of selectedFolder"
            ];
        }

        return [
            "-e",
            "set selectedFolder to choose folder with prompt \"Open Git repository\" default location POSIX file \"" +
            EscapeAppleScriptString(initialDirectory) + "\"",
            "-e",
            "POSIX path of selectedFolder"
        ];
    }

    private static IEnumerable<string> BuildZenityArguments(string? initialDirectory)
    {
        var args = new List<string>
        {
            "--file-selection",
            "--directory",
            "--title=Open Git repository",
        };

        if (initialDirectory != null)
            args.Add("--filename=" + EnsureTrailingSeparator(initialDirectory));

        return args;
    }

    private static IEnumerable<string> BuildKDialogArguments(string? initialDirectory)
    {
        var args = new List<string> { "--getexistingdirectory" };
        if (initialDirectory != null)
            args.Add(initialDirectory);
        args.Add("--title");
        args.Add("Open Git repository");
        return args;
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
            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(directory, name + extension);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }

    private static string EnsureTrailingSeparator(string path) =>
        Path.EndsInDirectorySeparator(path) ? path : path + Path.DirectorySeparatorChar;

    private static string EscapePowerShellSingleQuotedString(string value) => value.Replace("'", "''");

    private static string EscapeAppleScriptString(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private sealed record PickerCommand(string Executable, Func<string?, IEnumerable<string>> BuildArguments);
}
