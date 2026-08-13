using System.Text;
using Meridian.Core;
using Meridian.Tests.Fixtures;
using Xunit;

namespace Meridian.Tests;

/// <summary>P10's Core half: parsing, header materialisation, folder
/// inheritance, and the client-credentials grant.</summary>
public class AuthTests : IClassFixture<TestHttpServer>, IDisposable
{
    private readonly TestHttpServer _server;
    private readonly string _root = Directory.CreateTempSubdirectory("meridian-auth-").FullName;

    public AuthTests(TestHttpServer server) => _server = server;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void Write(string relative, string content)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static VariableResolver Resolver(params (string Key, string Value)[] values) =>
        new(environment: values.ToDictionary(v => v.Key, v => v.Value));

    // ── Parsing ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("none", AuthKind.None)]
    [InlineData("basic {{u}} {{p}}", AuthKind.Basic)]
    [InlineData("bearer {{t}}", AuthKind.Bearer)]
    [InlineData("apikey header X-Api-Key {{k}}", AuthKind.ApiKey)]
    [InlineData("oauth2 client_credentials {{url}} {{id}} {{secret}}", AuthKind.OAuth2)]
    public void EachTypeParses(string args, AuthKind kind)
    {
        var spec = Auth.Parse(args, out var error);
        Assert.True(spec != null, error);
        Assert.Equal(kind, spec!.Kind);
    }

    [Theory]
    [InlineData("", "needs a type")]
    [InlineData("basic user", "needs a user and a password")]
    [InlineData("bearer", "needs a token")]
    [InlineData("apikey cookie X {{k}}", "header|query")]
    [InlineData("oauth2 password {{u}} {{p}}", "client_credentials")]
    [InlineData("magic", "unknown auth type")]
    public void BadAuthLinesSayWhy(string args, string fragment)
    {
        Assert.Null(Auth.Parse(args, out var error));
        Assert.Contains(fragment, error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("basic {{u}} {{p}}")]
    [InlineData("bearer {{t}}")]
    [InlineData("apikey query key {{k}}")]
    [InlineData("oauth2 client_credentials {{url}} {{id}} {{secret}} a:read b:write")]
    public void FormatRoundTrips(string args)
    {
        var spec = Auth.Parse(args, out _)!;
        Assert.Equal(args, Auth.Format(spec));
    }

    // ── Headers ──────────────────────────────────────────────────────────

    [Fact]
    public void BasicIsBase64OfUserColonPassword()
    {
        var spec = Auth.Parse("basic {{user}} {{pass}}", out _)!;
        var header = Auth.HeaderFor(spec, Resolver(("user", "merchant_bot"), ("pass", "hunter2")), null, out var missing);

        Assert.Empty(missing);
        Assert.Equal("Authorization", header!.Value.Name);
        Assert.Equal("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("merchant_bot:hunter2")),
            header.Value.Value);
    }

    [Fact]
    public void BearerAndApiKeyMaterialise()
    {
        var bearer = Auth.HeaderFor(Auth.Parse("bearer {{t}}", out _)!, Resolver(("t", "abc")), null, out _);
        Assert.Equal(("Authorization", "Bearer abc"), bearer!.Value);

        var apikey = Auth.HeaderFor(Auth.Parse("apikey header X-Api-Key {{k}}", out _)!,
            Resolver(("k", "sekret")), null, out _);
        Assert.Equal(("X-Api-Key", "sekret"), apikey!.Value);
    }

    [Fact]
    public void AnUnresolvedCredentialIsReportedNotSentBlank()
    {
        var spec = Auth.Parse("bearer {{missing}}", out _)!;
        Auth.HeaderFor(spec, Resolver(), null, out var missing);

        Assert.Equal(["missing"], missing);
    }

    [Fact]
    public void ApiKeyInQueryIsAQueryPairNotAHeader()
    {
        var spec = Auth.Parse("apikey query api_key {{k}}", out _)!;

        Assert.Null(Auth.HeaderFor(spec, Resolver(("k", "v")), null, out _));
        Assert.Equal(("api_key", "v"), Auth.QueryFor(spec, Resolver(("k", "v")))!.Value);
    }

    [Fact]
    public void OAuth2ContributesTheFetchedToken()
    {
        var spec = Auth.Parse("oauth2 client_credentials {{u}} {{i}} {{s}}", out _)!;
        var header = Auth.HeaderFor(spec, Resolver(), "tok-from-endpoint", out _);

        Assert.Equal(("Authorization", "Bearer tok-from-endpoint"), header!.Value);
    }

    // ── Inheritance ──────────────────────────────────────────────────────

    private ReqDocument? ReadDoc(string path) =>
        File.Exists(path) ? ReqDocument.Parse(File.ReadAllText(path)) : null;

    [Fact]
    public void ARequestInheritsTheNearestFolderDefault()
    {
        Write("_folder.req", "auth bearer {{root_token}}\n");
        Write("charges/_folder.req", "auth basic {{u}} {{p}}\n");
        Write("charges/get.req", $"GET {_server.Origin}/ok\n");

        var path = Path.Combine(_root, "charges", "get.req");
        var (spec, from) = Auth.Effective(path, ReadDoc(path)!, _root, ReadDoc);

        Assert.Equal(AuthKind.Basic, spec.Kind);   // nearest wins over the root
        Assert.Equal(Path.Combine(_root, "charges"), from);
    }

    [Fact]
    public void ItsOwnAuthLineWinsOverTheFolder()
    {
        Write("charges/_folder.req", "auth basic {{u}} {{p}}\n");
        Write("charges/get.req", $"GET {_server.Origin}/ok\nauth bearer {{{{own}}}}\n");

        var path = Path.Combine(_root, "charges", "get.req");
        var (spec, from) = Auth.Effective(path, ReadDoc(path)!, _root, ReadDoc);

        Assert.Equal(AuthKind.Bearer, spec.Kind);
        Assert.Null(from);   // not inherited
    }

    [Fact]
    public void NoFolderDefaultMeansNoAuth()
    {
        Write("lone.req", $"GET {_server.Origin}/ok\n");
        var path = Path.Combine(_root, "lone.req");

        var (spec, from) = Auth.Effective(path, ReadDoc(path)!, _root, ReadDoc);

        Assert.Equal(AuthKind.None, spec.Kind);
        Assert.Null(from);
    }

    [Fact]
    public void TheWalkStopsAtTheCollectionRoot()
    {
        // A _folder.req ABOVE the collection must not reach in — a folder
        // default is a folder's business, not the whole disk's.
        var outer = Directory.GetParent(_root)!.FullName;
        var stray = Path.Combine(outer, "_folder.req");
        bool created = !File.Exists(stray);
        if (created) File.WriteAllText(stray, "auth bearer {{stray}}\n");
        try
        {
            Write("inner.req", $"GET {_server.Origin}/ok\n");
            var path = Path.Combine(_root, "inner.req");

            var (spec, _) = Auth.Effective(path, ReadDoc(path)!, _root, ReadDoc);

            Assert.Equal(AuthKind.None, spec.Kind);
        }
        finally
        {
            if (created) File.Delete(stray);
        }
    }

    // ── OAuth2 ───────────────────────────────────────────────────────────

    [Fact]
    public void TheTokenResponseIsParsedWithItsExpiry()
    {
        var now = DateTimeOffset.UnixEpoch;
        var token = OAuth2.ParseToken("""{"access_token":"abc","token_type":"Bearer","expires_in":3600}""", now, out var error);

        Assert.Null(error);
        Assert.Equal("abc", token!.AccessToken);
        Assert.Equal(now.AddHours(1), token.ExpiresAt);
        Assert.True(token.IsValid(now));
    }

    [Fact]
    public void AMissingExpiryFallsBackToAnHourNotForever()
    {
        var now = DateTimeOffset.UnixEpoch;
        var token = OAuth2.ParseToken("""{"access_token":"abc"}""", now, out _);

        Assert.Equal(now.AddHours(1), token!.ExpiresAt);
    }

    [Theory]
    [InlineData("""{"error":"invalid_client"}""", "no access_token")]
    [InlineData("<html>nope</html>", "did not answer with JSON")]
    public void ANonTokenAnswerSaysWhy(string body, string fragment)
    {
        Assert.Null(OAuth2.ParseToken(body, DateTimeOffset.UnixEpoch, out var error));
        Assert.Contains(fragment, error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ATokenAboutToExpireIsNotValid()
    {
        // The refresh margin: a token with 30 s left must not be handed to a
        // request that may take longer than that to answer.
        var now = DateTimeOffset.UnixEpoch;
        var token = new OAuthToken("abc", now.AddSeconds(30));

        Assert.False(token.IsValid(now));
        Assert.True(new OAuthToken("abc", now.AddSeconds(120)).IsValid(now));
    }

    [Fact]
    public async Task TheGrantFetchesCachesAndReuses()
    {
        using var engine = new HttpEngine();
        var oauth = new OAuth2(engine);
        var spec = Auth.Parse($"oauth2 client_credentials {_server.Origin}/token id secret scope:a", out _)!;
        var now = DateTimeOffset.Now;

        var (token, error) = await oauth.TokenFor(spec, Resolver(), now);

        Assert.Null(error);
        Assert.Equal("tok-abc123", token!.AccessToken);
        // Cached: a second call returns the same instance without a round trip.
        var (again, _) = await oauth.TokenFor(spec, Resolver(), now);
        Assert.Same(token, again);

        oauth.Forget(spec);
        Assert.Null(oauth.Cached(spec));
    }

    [Fact]
    public async Task AnUnresolvedClientSecretStopsTheGrant()
    {
        using var engine = new HttpEngine();
        var oauth = new OAuth2(engine);
        var spec = Auth.Parse($"oauth2 client_credentials {_server.Origin}/token {{{{id}}}} {{{{secret}}}}", out _)!;

        var (token, error) = await oauth.TokenFor(spec, Resolver(), DateTimeOffset.Now);

        Assert.Null(token);
        Assert.Contains("{{id}}", error);
    }

    [Fact]
    public async Task ARefusingEndpointIsReported()
    {
        using var engine = new HttpEngine();
        var oauth = new OAuth2(engine);
        var spec = Auth.Parse($"oauth2 client_credentials {_server.Origin}/teapot id secret", out _)!;

        var (token, error) = await oauth.TokenFor(spec, Resolver(), DateTimeOffset.Now);

        Assert.Null(token);
        Assert.Contains("418", error);
    }
}
