using System.Net.Http;
using System.Text.Json;
using HultUI.Core.Platform;
using Junction.GitLab;
using Xunit;

namespace Junction.Tests.GitLab;

/// <summary>
/// The split the design insists on — identity in settings, secret in the credential store —
/// and the three sign-in outcomes the accounts pane shows. The settings file is serialized
/// for real through the app's own JSON context, and the assertion is on the text: the token
/// string must not be anywhere in it.
/// </summary>
public class GitLabAccountsTests
{
    private const string Api = "/api/v4";
    private const string Token = "glpat-SECRET-1234567890";

    private static (GitLabAccounts Accounts, JunctionSettingsData Settings, InMemoryCredentialStore Store, int Persists)
        Setup(FakeGitLabHandler handler)
    {
        var settings = new JunctionSettingsData();
        var store = new InMemoryCredentialStore();
        int persists = 0;
        var accounts = new GitLabAccounts(settings.GitLabAccounts, store, () => persists++, _ => handler);
        return (accounts, settings, store, persists);
    }

    [Fact]
    public async Task SigningInWithATokenKeepsTheIdentityAndHidesTheSecret()
    {
        var handler = new FakeGitLabHandler()
            .Map($"GET {Api}/user", "user")
            .Map($"GET {Api}/personal_access_tokens/self", "token_self");
        var (accounts, settings, store, _) = Setup(handler);

        var res = await accounts.SignInWithTokenAsync("gitlab.com", Token);

        Assert.True(res.Ok, res.Failure?.Message);
        var account = res.Value;
        Assert.Equal("gitlab.com", account.Host);
        Assert.Equal("https://gitlab.com", account.WebBaseUrl);
        Assert.Equal("dosei", account.Username);
        Assert.Equal("Dana Osei", account.Name);
        Assert.Equal(GitLabTokenKind.Pat, account.Kind);
        Assert.Equal(["api", "write_repository"], account.Scopes);
        Assert.Equal(new DateTimeOffset(2036, 9, 21, 0, 0, 0, TimeSpan.Zero), account.ExpiresAt);
        Assert.True(account.CanWrite);

        Assert.Same(account, Assert.Single(settings.GitLabAccounts));
        Assert.Equal(Token, store.Read(GitLabAccounts.Service("gitlab.com"), GitLabAccounts.TokenAccount));
        Assert.True(accounts.StoredInKeyring("gitlab.com"));

        var json = JsonSerializer.Serialize(settings, JunctionSettingsJsonContext.Default.JunctionSettingsData);
        Assert.DoesNotContain("SECRET", json);
        Assert.Contains("\"dosei\"", json);
        Assert.Contains("\"Pat\"", json);       // the kind is a word in the file, not a number
    }

    [Fact]
    public async Task TheSavedAccountRoundTripsThroughTheSettingsFile()
    {
        var handler = new FakeGitLabHandler()
            .Map($"GET {Api}/user", "user")
            .Map($"GET {Api}/personal_access_tokens/self", "token_self");
        var (accounts, settings, _, _) = Setup(handler);
        await accounts.SignInWithTokenAsync("https://gitlab.com/", Token);

        var json = JsonSerializer.Serialize(settings, JunctionSettingsJsonContext.Default.JunctionSettingsData);
        var back = JsonSerializer.Deserialize(json, JunctionSettingsJsonContext.Default.JunctionSettingsData)!;
        back.Normalize();

        var a = Assert.Single(back.GitLabAccounts);
        Assert.Equal("gitlab.com", a.Host);
        Assert.Equal(GitLabTokenKind.Pat, a.Kind);
        Assert.Equal(["api", "write_repository"], a.Scopes);
    }

    [Fact]
    public async Task AReadOnlyTokenIsRefusedAndNothingIsStored()
    {
        var handler = new FakeGitLabHandler()
            .Map($"GET {Api}/user", "user")
            .Map($"GET {Api}/personal_access_tokens/self", "token_self_readonly");
        var (accounts, settings, store, _) = Setup(handler);

        var res = await accounts.SignInWithTokenAsync("gitlab.com", Token);

        Assert.False(res.Ok);
        Assert.Equal(GitLabFailureKind.Forbidden, res.Failure!.Kind);
        Assert.Contains("read_api", res.Failure.Message);
        Assert.Contains("api scope", res.Failure.Message);
        Assert.Empty(settings.GitLabAccounts);
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public async Task ARefusedTokenNamesTheHost()
    {
        var handler = new FakeGitLabHandler().Map($"GET {Api}/user", "error_401", 401);
        var (accounts, settings, store, _) = Setup(handler);

        var res = await accounts.SignInWithTokenAsync("gitlab.com", "glpat-wrong");

        Assert.Equal(GitLabFailureKind.Unauthorized, res.Failure!.Kind);
        Assert.Equal("gitlab.com did not accept that token", res.Failure.Message);
        Assert.Empty(settings.GitLabAccounts);
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public async Task AHostWithNoApiIsNotGitLab()
    {
        var handler = new FakeGitLabHandler().Map($"GET {Api}/user", _ => FakeGitLabHandler.Html("<html>intranet</html>", 404));
        var (accounts, _, _, _) = Setup(handler);

        var res = await accounts.SignInWithTokenAsync("https://intranet.acme.internal", Token);

        Assert.Equal(GitLabFailureKind.Invalid, res.Failure!.Kind);
        Assert.Contains("does not look like a GitLab instance", res.Failure.Message);
    }

    [Fact]
    public async Task AnOAuthAccessTokenPastedAsAPatStillSignsIn()
    {
        // /personal_access_tokens/self answers 404 for an OAuth token; the user is known.
        var handler = new FakeGitLabHandler()
            .Map($"GET {Api}/user", "user")
            .MapJson($"GET {Api}/personal_access_tokens/self", "{\"message\":\"404 Not Found\"}", 404);
        var (accounts, _, _, _) = Setup(handler);

        var res = await accounts.SignInWithTokenAsync("gitlab.com", "gloas-pasted");

        Assert.True(res.Ok, res.Failure?.Message);
        Assert.Equal(["api"], res.Value.Scopes);
        Assert.Null(res.Value.ExpiresAt);
    }

    [Fact]
    public async Task AClientComesBackForTheHostAndUsesTheStoredToken()
    {
        var handler = new FakeGitLabHandler()
            .Map($"GET {Api}/user", "user")
            .Map($"GET {Api}/personal_access_tokens/self", "token_self");
        var (accounts, _, _, _) = Setup(handler);
        await accounts.SignInWithTokenAsync("gitlab.com", Token);

        var client = accounts.ClientFor("GITLAB.COM");
        Assert.NotNull(client);
        Assert.Same(client, accounts.ClientFor("gitlab.com"));
        Assert.Null(accounts.ClientFor("gitlab.other"));
        Assert.Null(accounts.ClientFor(GitLabRemote.TryParse("git@github.com:x/y.git")!));

        await client.GetCurrentUserAsync();
        Assert.Equal(Token, handler.Seen[^1].Request.Headers.GetValues("PRIVATE-TOKEN").Single());
    }

    [Fact]
    public async Task SigningOutForgetsTheAccountAndTheSecrets()
    {
        var handler = new FakeGitLabHandler()
            .Map($"GET {Api}/user", "user")
            .Map($"GET {Api}/personal_access_tokens/self", "token_self");
        var (accounts, settings, store, _) = Setup(handler);
        await accounts.SignInWithTokenAsync("gitlab.com", Token);
        var client = accounts.ClientFor("gitlab.com");

        await accounts.SignOutAsync("gitlab.com");

        Assert.Empty(settings.GitLabAccounts);
        Assert.Equal(0, store.Count);
        Assert.Null(accounts.ClientFor("gitlab.com"));
        Assert.NotSame(client, accounts.ClientFor("gitlab.com"));
        Assert.False(accounts.HasAccount("gitlab.com"));
    }

    [Fact]
    public async Task APlatformWithNoStoreKeepsTheTokenForTheSessionAndSaysSo()
    {
        var handler = new FakeGitLabHandler()
            .Map($"GET {Api}/user", "user")
            .Map($"GET {Api}/personal_access_tokens/self", "token_self");
        var settings = new JunctionSettingsData();
        var accounts = new GitLabAccounts(settings.GitLabAccounts, new NoCredentialStore(), () => { }, _ => handler);

        var res = await accounts.SignInWithTokenAsync("gitlab.com", Token);

        Assert.True(res.Ok, res.Failure?.Message);
        Assert.False(accounts.StoredInKeyring("gitlab.com"));
        Assert.Equal(Token, accounts.TokenFor("gitlab.com"));
        Assert.NotNull(accounts.ClientFor("gitlab.com"));
    }

    [Fact]
    public async Task SigningInAgainReplacesTheAccountRatherThanAddingASecond()
    {
        var handler = new FakeGitLabHandler()
            .Map($"GET {Api}/user", "user")
            .Map($"GET {Api}/personal_access_tokens/self", "token_self");
        var (accounts, settings, store, _) = Setup(handler);

        await accounts.SignInWithTokenAsync("gitlab.com", "glpat-first");
        await accounts.SignInWithTokenAsync("https://GitLab.com", "glpat-second");

        Assert.Single(settings.GitLabAccounts);
        Assert.Equal("glpat-second", store.Read(GitLabAccounts.Service("gitlab.com"), GitLabAccounts.TokenAccount));
    }

    [Fact]
    public async Task AnExpiringOAuthTokenIsRefreshedBeforeAClientIsHandedOut()
    {
        var handler = new FakeGitLabHandler()
            .Map("POST /oauth/token", "oauth_token")
            .Map($"GET {Api}/user", "user");
        var settings = new JunctionSettingsData();
        var store = new InMemoryCredentialStore();
        settings.GitLabAccounts.Add(new GitLabAccountData
        {
            Host = "gitlab.com", WebBaseUrl = "https://gitlab.com", Username = "dosei", Kind = GitLabTokenKind.OAuth,
            ClientId = "abc123", ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(30), Scopes = ["api"],
        });
        store.Write(GitLabAccounts.Service("gitlab.com"), GitLabAccounts.TokenAccount, "gloas-old");
        store.Write(GitLabAccounts.Service("gitlab.com"), GitLabAccounts.RefreshAccount, "gloar-old");
        int persists = 0;
        var accounts = new GitLabAccounts(settings.GitLabAccounts, store, () => persists++, _ => handler);

        var client = await accounts.FreshClientForAsync("gitlab.com");

        Assert.NotNull(client);
        var refresh = handler.Seen.Single(s => s.Request.RequestUri!.AbsolutePath == "/oauth/token");
        Assert.Contains("grant_type=refresh_token", refresh.Body);
        Assert.Contains("refresh_token=gloar-old", refresh.Body);
        Assert.StartsWith("gloas-", store.Read(GitLabAccounts.Service("gitlab.com"), GitLabAccounts.TokenAccount));
        Assert.StartsWith("gloar-fedcba", store.Read(GitLabAccounts.Service("gitlab.com"), GitLabAccounts.RefreshAccount));
        Assert.True(settings.GitLabAccounts[0].ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(30) || settings.GitLabAccounts[0].ExpiresAt == DateTimeOffset.FromUnixTimeSeconds(1789000000 + 7200));
        Assert.Equal(1, persists);

        await client.GetCurrentUserAsync();
        Assert.Equal("gloas-0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            handler.Seen[^1].Request.Headers.Authorization!.Parameter);
    }

    [Fact]
    public void ExpiryReadsTheWayThePaneShowsIt()
    {
        var soon = new GitLabAccountData { ExpiresAt = DateTimeOffset.UtcNow.AddDays(9) };
        var far = new GitLabAccountData { ExpiresAt = DateTimeOffset.UtcNow.AddDays(200) };
        var never = new GitLabAccountData();
        var gone = new GitLabAccountData { ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1) };

        Assert.True(soon.ExpiresSoon);
        Assert.InRange(soon.DaysLeft!.Value, 8, 9);
        Assert.False(far.ExpiresSoon);
        Assert.Null(never.DaysLeft);
        Assert.False(never.ExpiresSoon);
        Assert.True(gone.IsExpired);
        Assert.True(gone.ExpiresSoon);
    }
}
