using System.Net.Http;
using System.Web;
using Junction.GitLab;
using Xunit;

namespace Junction.Tests.GitLab;

/// <summary>
/// The browser sign-in, driven without a browser: the test plays the instance's part on the
/// loopback redirect and the token endpoint. Proves the PKCE arithmetic against the RFC's own
/// vector, that no client secret ever goes over the wire, that a redirect with the wrong
/// state is refused, and that the whole dance completes on a free port.
/// </summary>
public class GitLabOAuthTests
{
    [Fact]
    public void TheChallengeMatchesRfc7636sVector()
    {
        // RFC 7636 §B: this verifier must yield exactly this S256 challenge.
        Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
            GitLabOAuth.ChallengeFor("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));
    }

    [Fact]
    public void AFreshPkcePairIsUrlSafeAndLongEnough()
    {
        var pkce = GitLabOAuth.CreatePkce();

        Assert.InRange(pkce.Verifier.Length, 43, 128);
        Assert.DoesNotContain('+', pkce.Verifier);
        Assert.DoesNotContain('/', pkce.Verifier);
        Assert.DoesNotContain('=', pkce.Verifier);
        Assert.Equal(GitLabOAuth.ChallengeFor(pkce.Verifier), pkce.Challenge);
        Assert.NotEqual(pkce.Verifier, GitLabOAuth.CreatePkce().Verifier);
    }

    [Fact]
    public void TheAuthorizeUrlAsksForS256AndCarriesNoSecret()
    {
        var url = GitLabOAuth.AuthorizeUrl("https://gitlab.com/", "abc123", GitLabOAuth.RedirectUri(), "st", "ch");
        var uri = new Uri(url);
        var q = HttpUtility.ParseQueryString(uri.Query);

        Assert.Equal("/oauth/authorize", uri.AbsolutePath);
        Assert.Equal("abc123", q["client_id"]);
        Assert.Equal("http://localhost:7171/auth/redirect", q["redirect_uri"]);
        Assert.Equal("code", q["response_type"]);
        Assert.Equal("S256", q["code_challenge_method"]);
        Assert.Equal("ch", q["code_challenge"]);
        Assert.Equal("api read_user", q["scope"]);
        Assert.Null(q["client_secret"]);
    }

    [Fact]
    public async Task TheCodeExchangePostsTheVerifierAndNoSecret()
    {
        var handler = new FakeGitLabHandler().Map("POST /oauth/token", "oauth_token");

        var tokens = await GitLabOAuth.ExchangeCodeAsync("https://gitlab.com", "abc123", "the-code", "the-verifier",
            GitLabOAuth.RedirectUri(), handler);

        Assert.True(tokens.Ok, tokens.Failure?.Message);
        Assert.StartsWith("gloas-", tokens.Value.AccessToken);
        Assert.StartsWith("gloar-", tokens.Value.RefreshToken);
        Assert.Equal(["api", "read_user"], tokens.Value.Scopes);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1789000000 + 7200), tokens.Value.ExpiresAt);

        var form = HttpUtility.ParseQueryString(handler.Seen.Single().Body!);
        Assert.Equal("authorization_code", form["grant_type"]);
        Assert.Equal("the-code", form["code"]);
        Assert.Equal("the-verifier", form["code_verifier"]);
        Assert.Equal("abc123", form["client_id"]);
        Assert.Null(form["client_secret"]);
    }

    [Fact]
    public async Task ARefusedExchangeIsUnauthorizedWithTheServersReason()
    {
        var handler = new FakeGitLabHandler().MapJson("POST /oauth/token",
            "{\"error\":\"invalid_grant\",\"error_description\":\"The provided authorization grant is invalid\"}", 400);

        var tokens = await GitLabOAuth.ExchangeCodeAsync("https://gitlab.com", "abc123", "x", "y", GitLabOAuth.RedirectUri(), handler);

        Assert.Equal(GitLabFailureKind.Unauthorized, tokens.Failure!.Kind);
        Assert.Equal("The provided authorization grant is invalid", tokens.Failure.Message);
    }

    [Fact]
    public void TheRedirectQueryIsReadAsTheBrowserSendsIt()
    {
        var cb = GitLabOAuth.LoopbackListener.Parse("/auth/redirect?code=abc%2Bdef&state=s1");
        Assert.Equal("abc+def", cb.Code);
        Assert.Equal("s1", cb.State);
        Assert.Null(cb.Error);

        var denied = GitLabOAuth.LoopbackListener.Parse("/auth/redirect?error=access_denied&error_description=The+user+denied&state=s1");
        Assert.Null(denied.Code);
        Assert.Equal("access_denied", denied.Error);
        Assert.Equal("The user denied", denied.ErrorDescription);
    }

    [Fact]
    public async Task TheWholeDanceCompletesOnAFreePort()
    {
        string? challengeSeen = null;
        var handler = new FakeGitLabHandler().Map("POST /oauth/token", req =>
        {
            // The instance's side of PKCE: the verifier must hash to the challenge it saw.
            var form = HttpUtility.ParseQueryString(req.Content!.ReadAsStringAsync().Result);
            return GitLabOAuth.ChallengeFor(form["code_verifier"]!) == challengeSeen
                ? FakeGitLabHandler.Json(FakeGitLabHandler.Recording("oauth_token"))
                : FakeGitLabHandler.Json("{\"error\":\"invalid_grant\"}", 400);
        });

        var tokens = await GitLabOAuth.SignInAsync("https://gitlab.com", "abc123", openBrowser: url =>
        {
            // "The browser": read what the app asked for, then come back on the redirect.
            var q = HttpUtility.ParseQueryString(new Uri(url).Query);
            challengeSeen = q["code_challenge"];
            var redirect = $"{q["redirect_uri"]}?code=the-code&state={Uri.EscapeDataString(q["state"]!)}";
            _ = Task.Run(async () =>
            {
                using var http = new HttpClient();
                var page = await http.GetStringAsync(redirect);
                Assert.Contains("close this tab", page);
            });
            return true;
        }, handler, port: 0);

        Assert.True(tokens.Ok, tokens.Failure?.Message);
        Assert.StartsWith("gloas-", tokens.Value.AccessToken);
    }

    [Fact]
    public async Task ARedirectWithSomeoneElsesStateIsRefused()
    {
        var handler = new FakeGitLabHandler();

        var tokens = await GitLabOAuth.SignInAsync("https://gitlab.com", "abc123", openBrowser: url =>
        {
            var q = HttpUtility.ParseQueryString(new Uri(url).Query);
            _ = Task.Run(() => new HttpClient().GetStringAsync($"{q["redirect_uri"]}?code=the-code&state=forged"));
            return true;
        }, handler, port: 0);

        Assert.False(tokens.Ok);
        Assert.Equal(GitLabFailureKind.Invalid, tokens.Failure!.Kind);
        Assert.Empty(handler.Seen);    // the code was never exchanged
    }

    [Fact]
    public async Task ADeniedSignInSaysSoWithoutExchangingAnything()
    {
        var handler = new FakeGitLabHandler();

        var tokens = await GitLabOAuth.SignInAsync("https://gitlab.com", "abc123", openBrowser: url =>
        {
            var q = HttpUtility.ParseQueryString(new Uri(url).Query);
            _ = Task.Run(() => new HttpClient().GetAsync($"{q["redirect_uri"]}?error=access_denied&state={q["state"]}"));
            return true;
        }, handler, port: 0);

        Assert.Equal(GitLabFailureKind.Unauthorized, tokens.Failure!.Kind);
        Assert.Equal("Sign-in was cancelled in the browser", tokens.Failure.Message);
        Assert.Empty(handler.Seen);
    }

    [Fact]
    public async Task NoRegisteredApplicationMeansNoBrowserIsOpened()
    {
        bool opened = false;

        var tokens = await GitLabOAuth.SignInAsync("https://gitlab.com", clientId: "", openBrowser: _ => opened = true, port: 0);

        Assert.False(tokens.Ok);
        Assert.False(opened);
        Assert.Contains("personal access token", tokens.Failure!.Message);
    }
}
