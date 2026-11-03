using Meridian.Core;
using Xunit;
using FakeKeyring = Meridian.Tests.EnvironmentsViewTests.FakeKeyring;

namespace Meridian.Tests;

/// <summary>Secret values per environment: the order a key resolves in, the keyring's entry
/// names, and the loader the CLI runs resolving each environment as the app does.</summary>
public class SecretsTests : IDisposable
{
    private const string Root = @"C:\src\payments-api";
    private readonly string _dir = Directory.CreateTempSubdirectory("meridian-secrets-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private void Write(string relative, string content)
    {
        var path = Path.Combine(_dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    [Fact]
    public void TheMostSpecificValueWins()
    {
        var keyring = new FakeKeyring();
        var variables = new Dictionary<string, string>();
        SecretValue? Resolve() => Secrets.Resolve(keyring, Root, "staging", "client_secret", variables.GetValueOrDefault);

        Assert.Null(Resolve());

        variables["client_secret"] = "from-ci";
        Assert.Equal(new SecretValue("from-ci", SecretSource.SharedVariable), Resolve());

        keyring.Set(Root, null, "client_secret", "shared");
        Assert.Equal(new SecretValue("shared", SecretSource.SharedKeyring), Resolve());

        variables["staging__client_secret"] = "staging-from-ci";
        Assert.Equal(new SecretValue("staging-from-ci", SecretSource.Variable), Resolve());

        keyring.Set(Root, "staging", "client_secret", "staging-own");
        Assert.Equal(new SecretValue("staging-own", SecretSource.Keyring), Resolve());
        Assert.True(Resolve()!.Value.IsOwn);

        // Another environment's own values are not this one's.
        Assert.Equal(new SecretValue("shared", SecretSource.SharedKeyring),
            Secrets.Resolve(keyring, Root, "local", "client_secret", variables.GetValueOrDefault));
    }

    /// <summary>The shared entry keeps the name every entry had before values were per environment,
    /// so a machine's stored secrets serve every environment after the update, unmigrated.</summary>
    [Fact]
    public void TheSharedEntryKeepsItsOldName_AndAnEnvironmentsOwnCannotCollideWithIt()
    {
        Assert.Equal(@"meridian|C:\src\payments-api|client_secret", WindowsCredentialKeyring.Target(Root, null, "client_secret"));
        Assert.Equal(@"meridian:staging|C:\src\payments-api|client_secret", WindowsCredentialKeyring.Target(Root, "staging", "client_secret"));
        // A key may hold a "|"; the environment's part is before the root, which can't.
        Assert.NotEqual(WindowsCredentialKeyring.Target(Root, null, "staging|client_secret"),
            WindowsCredentialKeyring.Target(Root, "staging", "client_secret"));
    }

    [Fact]
    public void TheLoaderResolvesEachEnvironmentsOwnValue_ElseTheShared()
    {
        Write("api/get.req", "GET {{base_url}}/x\nheader A: {{client_secret}}\n");
        foreach (var env in new[] { "local", "staging", "ci" })
            Write($"envs/{env}.env", $"base_url=https://{env}.example\nsecret client_secret\n");
        var keyring = new FakeKeyring();
        keyring.Set(_dir, "local", "client_secret", "local-value");
        keyring.Set(_dir, "staging", "client_secret", "staging-value");

        Dictionary<string, string> Values(string env, out IReadOnlyList<string> missing)
        {
            var loaded = CollectionLoader.Load(_dir, env, keyring, out var error);
            Assert.True(loaded != null, error);
            missing = loaded!.MissingSecrets;
            return loaded.Environment;
        }

        Assert.Equal("local-value", Values("local", out _)["client_secret"]);
        Assert.Equal("staging-value", Values("staging", out _)["client_secret"]);
        Assert.False(Values("ci", out var missing).ContainsKey("client_secret"));
        Assert.Equal(["client_secret"], missing);

        keyring.Set(_dir, null, "client_secret", "shared-value");
        Assert.Equal("shared-value", Values("ci", out missing)["client_secret"]);
        Assert.Empty(missing);
        Assert.Equal("staging-value", Values("staging", out _)["client_secret"]);
    }
}
