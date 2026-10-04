using Xunit;

namespace HultUI.Terminal.Ssh.Tests;

/// <summary>
/// OpenSSH's client configuration as <c>ssh</c> reads it: which block applies, the first value
/// winning, key files adding up, includes, tokens, and what it leaves alone.
/// </summary>
public sealed class SshConfigFileTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("hultui-ssh-config-").FullName;
    private static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private SshConfigFile With(params string[] lines)
    {
        var path = Path.Combine(_dir, "config");
        File.WriteAllText(path, string.Join("\n", lines) + "\n");
        return new SshConfigFile(path);
    }

    [Fact]
    public void AHostsBlock_GivesItsKeyFileUserAndName()
    {
        // As the user's own: one key per host, and nothing else.
        var config = With(
            "Host pathmaker.home",
            "  HostName pathmaker.home",
            "  User root",
            "  IdentityFile ~/.ssh/pathmaker",
            "  IdentitiesOnly yes",
            "Host steam.home",
            "  User root",
            "  IdentityFile ~/.ssh/steam");

        var pathmaker = config.Resolve("pathmaker.home");
        Assert.Equal(("pathmaker.home", "root", true), (pathmaker.HostName, pathmaker.User, pathmaker.IdentitiesOnly));
        Assert.Equal([Path.Combine(Home, ".ssh", "pathmaker").Replace('\\', '/')], pathmaker.IdentityFiles.Select(Slashes));

        var other = config.Resolve("jellyfin.home");
        Assert.Equal(("jellyfin.home", null, false), (other.HostName, other.User, other.IdentitiesOnly));
        Assert.Empty(other.IdentityFiles);
        Assert.Null(other.Port);
    }

    [Fact]
    public void TheFirstValue_Wins_AndKeyFiles_AddUp_InOrder()
    {
        var config = With(
            "Host build",
            "  HostName build.example.com",
            "  Port 2222",
            "  IdentityFile /keys/build",
            "Host *",
            "  User nobody",
            "  Port 22",
            "  HostName ignored.example.com",
            "  IdentityFile /keys/everyone");

        var build = config.Resolve("build");
        Assert.Equal(("build.example.com", 2222, "nobody"), (build.HostName, build.Port, build.User));
        Assert.Equal(["/keys/build", "/keys/everyone"], build.IdentityFiles);
    }

    [Fact]
    public void Patterns_Match_TheNameGiven_AndANegatedOne_Excludes()
    {
        var config = With(
            "Host *.home !router.home",
            "  User admin",
            "Host web?",
            "  User www");

        Assert.Equal("admin", config.Resolve("NAS.home").User); // names are not case-sensitive
        Assert.Null(config.Resolve("router.home").User);
        Assert.Equal("www", config.Resolve("web1").User);
        Assert.Null(config.Resolve("web12").User);
    }

    [Fact]
    public void BothWaysOfWritingAValue_AndQuotes_AndComments_AreRead()
    {
        var config = With(
            "# a comment",
            "Host=quoted",
            "  User = alice # who",
            "  IdentityFile=\"/keys/with space\"",
            "  identitiesonly YES",
            "  Port=2200");

        var quoted = config.Resolve("quoted");
        Assert.Equal(("alice", 2200, true), (quoted.User, quoted.Port, quoted.IdentitiesOnly));
        Assert.Equal(["/keys/with space"], quoted.IdentityFiles);
    }

    [Fact]
    public void Tokens_AreExpanded()
    {
        var config = With(
            "Host alias",
            "  HostName %h.example.com",
            "  User deploy",
            "  Port 2022",
            "  IdentityFile /keys/%r@%h:%p-%n%%");

        var alias = config.Resolve("alias");
        Assert.Equal("alias.example.com", alias.HostName);
        Assert.Equal(["/keys/deploy@alias.example.com:2022-alias%"], alias.IdentityFiles);
    }

    [Fact]
    public void Includes_AreFollowed_WhereTheyStand()
    {
        var sub = Path.Combine(_dir, "conf.d");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, "a.conf"), "Host included\n  User from-a\n");
        File.WriteAllText(Path.Combine(sub, "b.conf"), "Host included\n  User from-b\n  Port 2022\n");
        var config = With(
            $"Include {sub.Replace('\\', '/')}/*.conf",
            "Host included",
            "  User from-main");

        var included = config.Resolve("included");
        Assert.Equal(("from-a", 2022), (included.User, included.Port)); // a before b, both before the main file
    }

    [Fact]
    public void MatchBlocks_ApplyToNothing_AndProxyJump_IsReported()
    {
        var config = With(
            "Match host *",
            "  User matched",
            "Host behind",
            "  ProxyJump bastion",
            "Host direct",
            "  ProxyJump none");

        Assert.Null(config.Resolve("behind").User);
        Assert.Equal("bastion", config.Resolve("behind").ProxyJump);
        Assert.Null(config.Resolve("direct").ProxyJump);
    }

    [Fact]
    public void NoFile_GivesTheNameAsItIs()
    {
        var config = new SshConfigFile(Path.Combine(_dir, "missing"));
        var host = config.Resolve("example.com");
        Assert.Equal(("example.com", "example.com", null, null), (host.Alias, host.HostName, host.Port, host.User));
        Assert.Contains(SshConfigFile.DefaultIdentityFiles, f => f.EndsWith("id_ed25519", StringComparison.Ordinal));
    }

    private static string Slashes(string path) => path.Replace('\\', '/');
}
