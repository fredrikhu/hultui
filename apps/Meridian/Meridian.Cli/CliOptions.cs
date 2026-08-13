namespace Meridian.Cli;

/// <summary>
/// The parsed command line. Hand-rolled rather than a parser package: the
/// surface is four flags, and Native AOT would rather not reflect over an
/// options class.
/// </summary>
public sealed record CliOptions(
    string Command,
    string Path,
    string? Env,
    string? JUnitPath,
    bool Bail,
    bool Quiet)
{
    public const int ExitUsage = 64;   // EX_USAGE, the sysexits convention

    public const string Usage = """
        meridian — run a collection of .req files

        usage:
          meridian run <collection> [--env <name>] [--report junit:<file>] [--bail] [--quiet]
          meridian list <collection> [--env <name>]

        options:
          --env <name>            the environment to resolve {{vars}} from (envs/<name>.env)
          --report junit:<file>   also write a JUnit XML report
          --bail                  stop at the first failed assertion
          --quiet                 print only the summary lines

        exit codes:
          0  every request passed
          1  a request failed, was unreachable, or the run was aborted
          64 usage error
        """;

    /// <summary>Parses <paramref name="args"/>, or returns null and sets
    /// <paramref name="error"/> — never throws, never writes.</summary>
    public static CliOptions? Parse(string[] args, out string? error)
    {
        error = null;
        if (args.Length == 0) { error = "no command"; return null; }

        string command = args[0];
        if (command is not ("run" or "list")) { error = $"unknown command \"{command}\""; return null; }
        if (args.Length < 2 || args[1].StartsWith('-')) { error = $"{command} needs a collection path"; return null; }

        string path = args[1];
        string? env = null, junit = null;
        bool bail = false, quiet = false;

        for (int i = 2; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--env":
                    if (++i >= args.Length) { error = "--env needs a name"; return null; }
                    env = args[i];
                    break;
                case "--report":
                {
                    if (++i >= args.Length) { error = "--report needs junit:<file>"; return null; }
                    var value = args[i];
                    if (!value.StartsWith("junit:", StringComparison.OrdinalIgnoreCase))
                    {
                        error = $"unknown report format \"{value}\" — expected junit:<file>";
                        return null;
                    }
                    junit = value["junit:".Length..];
                    if (junit.Length == 0) { error = "--report junit: needs a file path"; return null; }
                    break;
                }
                case "--bail":
                    bail = true;
                    break;
                case "--quiet":
                    quiet = true;
                    break;
                default:
                    error = $"unknown option \"{args[i]}\"";
                    return null;
            }
        }
        return new CliOptions(command, path, env, junit, bail, quiet);
    }
}
