using Xunit;

namespace HultUI.Terminal.Ssh.Tests;

/// <summary>A fact that runs only where OpenSSH's <c>ssh-keygen</c> is found: on <c>PATH</c>, or
/// where Git for Windows keeps it.</summary>
internal sealed class SshKeygenFactAttribute : FactAttribute
{
    public SshKeygenFactAttribute()
    {
        if (Program is null) Skip = "ssh-keygen is not installed";
    }

    public static string? Program { get; } = Find();

    private static string? Find()
    {
        var name = OperatingSystem.IsWindows() ? "ssh-keygen.exe" : "ssh-keygen";
        var places = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Append(@"C:\Program Files\Git\usr\bin");
        foreach (var dir in places)
        {
            if (dir.Length == 0) continue;
            var path = Path.Combine(dir, name);
            // Windows lists placeholders for optional features it has not installed: a real
            // ssh-keygen is far bigger than a few bytes.
            if (File.Exists(path) && new FileInfo(path).Length > 4096) return path;
        }
        return null;
    }
}
