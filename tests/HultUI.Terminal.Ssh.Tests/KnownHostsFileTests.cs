using Xunit;

namespace HultUI.Terminal.Ssh.Tests;

/// <summary>
/// OpenSSH's known_hosts as <c>ssh</c> reads it: names, ports, hashes, patterns, negation,
/// revocation; and writes that leave every other line alone.
/// </summary>
public sealed class KnownHostsFileTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("hultui-known-hosts-").FullName;
    private readonly byte[] _key = TestKeys.Blob(TestKeys.New());
    private readonly byte[] _other = TestKeys.Blob(TestKeys.New());

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string FilePath => Path.Combine(_dir, "known_hosts");

    private KnownHostsFile With(params string[] lines)
    {
        File.WriteAllText(FilePath, string.Join("\n", lines) + "\n");
        return new KnownHostsFile(FilePath);
    }

    private string Line(string hosts, byte[] key) => $"{hosts} ecdsa-sha2-nistp256 {Convert.ToBase64String(key)}";

    private HostKeyStatus Check(KnownHostsFile file, string host, int port = 22, byte[]? key = null) =>
        file.Check(host, port, key ?? _key, out _);

    [Fact]
    public void NoFile_KnowsNothing()
    {
        Assert.Equal(HostKeyStatus.Unknown, Check(new KnownHostsFile(FilePath), "example.com"));
    }

    [Fact]
    public void TheHostsKey_IsKnown_AnotherOfItsKind_IsChanged_WithTheOneOnFile()
    {
        var file = With(Line("example.com", _key));
        Assert.Equal(HostKeyStatus.Known, Check(file, "example.com"));
        Assert.Equal(HostKeyStatus.Known, Check(file, "EXAMPLE.com")); // names are not case-sensitive

        Assert.Equal(HostKeyStatus.Changed, file.Check("example.com", 22, _other, out var onFile));
        Assert.Equal([KnownHostsFile.Fingerprint(_key)], onFile);
        Assert.Equal(HostKeyStatus.Unknown, Check(file, "other.example.com"));
    }

    [Fact]
    public void AnotherKindOfKey_OnFile_LeavesThisOneUnknown()
    {
        var file = With($"example.com ssh-ed25519 {Convert.ToBase64String(Ed25519Blob())}");
        Assert.Equal(HostKeyStatus.Unknown, Check(file, "example.com"));
    }

    [Fact]
    public void APortOtherThan22_IsInBrackets()
    {
        var file = With(Line("[example.com]:2222", _key));
        Assert.Equal(HostKeyStatus.Known, Check(file, "example.com", 2222));
        Assert.Equal(HostKeyStatus.Unknown, Check(file, "example.com", 22));
        Assert.Equal(HostKeyStatus.Unknown, Check(With(Line("example.com", _key)), "example.com", 2222));
    }

    [Fact]
    public void Patterns_Match_AndANegatedOne_Excludes()
    {
        var file = With(Line("!bad.example.com,*.example.com,host?", _key));
        Assert.Equal(HostKeyStatus.Known, Check(file, "a.example.com"));
        Assert.Equal(HostKeyStatus.Known, Check(file, "host1"));
        Assert.Equal(HostKeyStatus.Unknown, Check(file, "host12"));
        Assert.Equal(HostKeyStatus.Unknown, Check(file, "bad.example.com"));
    }

    [Fact]
    public void HashedNames_Match_AndAddingHashed_WritesNoName()
    {
        var file = new KnownHostsFile(FilePath) { HashNewEntries = true };
        file.Add("secret.example.com", 2222, _key);

        var text = File.ReadAllText(FilePath);
        Assert.StartsWith("|1|", text);
        Assert.DoesNotContain("secret", text);
        Assert.Equal(HostKeyStatus.Known, Check(new KnownHostsFile(FilePath), "secret.example.com", 2222));
        Assert.Equal(HostKeyStatus.Unknown, Check(new KnownHostsFile(FilePath), "secret.example.com"));
    }

    [Fact]
    public void ARevokedKey_IsRevoked_ForEveryHost_EvenOneThatListsIt()
    {
        var file = With(Line("example.com", _key), "@revoked * ecdsa-sha2-nistp256 " + Convert.ToBase64String(_key));
        Assert.Equal(HostKeyStatus.Revoked, Check(file, "example.com"));
        Assert.Equal(HostKeyStatus.Revoked, Check(file, "anywhere"));
    }

    [Fact]
    public void LinesItDoesNotTrust_AreSkipped()
    {
        var file = With(
            "# a comment",
            "",
            "@cert-authority *.example.com ecdsa-sha2-nistp256 " + Convert.ToBase64String(_key),
            "example.com ssh-rsa " + Convert.ToBase64String(_key), // the type does not match the key
            "example.com ecdsa-sha2-nistp256 not-base64",
            "garbage");
        Assert.Equal(HostKeyStatus.Unknown, Check(file, "example.com"));
        Assert.Equal(HostKeyStatus.Unknown, Check(file, "a.example.com"));
    }

    [Fact]
    public void Add_Appends_AfterALastLineWithoutANewline_AndMakesTheFolder()
    {
        var nested = new KnownHostsFile(Path.Combine(_dir, "ssh", "known_hosts"));
        nested.Add("example.com", 22, _key);
        Assert.Equal(HostKeyStatus.Known, Check(nested, "example.com"));

        File.WriteAllText(FilePath, Line("first.example.com", _other)); // no newline at the end
        var file = new KnownHostsFile(FilePath);
        file.Add("example.com", 22, _key);
        Assert.Equal(HostKeyStatus.Known, Check(file, "example.com"));
        Assert.Equal(HostKeyStatus.Known, Check(file, "first.example.com", key: _other));
    }

    [Fact]
    public void Replace_RemovesOnlyThatHostsKeysOfThatKind_AndKeepsEveryOtherLine()
    {
        var hashed = new KnownHostsFile(Path.Combine(_dir, "hashed")) { HashNewEntries = true };
        hashed.Add("example.com", 22, _other);
        var hashedLine = File.ReadAllText(Path.Combine(_dir, "hashed")).Trim();
        var ed25519 = $"example.com ssh-ed25519 {Convert.ToBase64String(Ed25519Blob())}";
        var file = With("# kept", Line("example.com", _other), hashedLine, ed25519, Line("other.example.com", _other), "garbage");

        file.Replace("example.com", 22, _key);

        var lines = File.ReadAllLines(FilePath);
        Assert.Equal(["# kept", ed25519, Line("other.example.com", _other), "garbage", Line("example.com", _key)], lines);
        Assert.Equal(HostKeyStatus.Known, Check(file, "example.com"));
    }

    [Fact]
    public void TheKeysType_IsReadFromTheKey()
    {
        Assert.Equal("ecdsa-sha2-nistp256", KnownHostsFile.KeyType(_key));
        Assert.Equal("ssh-ed25519", KnownHostsFile.KeyType(Ed25519Blob()));
        Assert.Equal("", KnownHostsFile.KeyType([1, 2]));
    }

    /// <summary>An ed25519 key's wire form: its type and 32 bytes.</summary>
    private static byte[] Ed25519Blob()
    {
        using var stream = new MemoryStream();
        TestKeys.WriteString(stream, "ssh-ed25519"u8);
        TestKeys.WriteString(stream, new byte[32]);
        return stream.ToArray();
    }
}
