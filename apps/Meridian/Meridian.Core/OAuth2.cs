using System.Text.Json;

namespace Meridian.Core;

/// <summary>A fetched token and when it stops being usable.</summary>
public sealed record OAuthToken(string AccessToken, DateTimeOffset ExpiresAt)
{
    /// <summary>Refresh a minute early: a token that expires while the request
    /// is in flight fails with a 401 that looks like a bug in the API.</summary>
    private static readonly TimeSpan Margin = TimeSpan.FromSeconds(60);

    public bool IsValid(DateTimeOffset now) => now + Margin < ExpiresAt;
    public TimeSpan Remaining(DateTimeOffset now) => ExpiresAt - now;
}

/// <summary>
/// The client-credentials grant — the one flow that needs no browser, which is
/// why it ships first (auth-code is the plan's stretch goal, and shipping its
/// UI without the loopback listener would be a phantom control).
///
/// <para>Tokens are cached per (endpoint, client, scopes) in memory for the
/// session. They are deliberately NOT written to the collection: a bearer token
/// is a live credential with a short life, and the whole file format promise is
/// that credentials never land in the repo.</para>
/// </summary>
public sealed class OAuth2(HttpEngine engine)
{
    private readonly Dictionary<string, OAuthToken> _cache = new(StringComparer.Ordinal);

    public OAuthToken? Cached(AuthSpec spec) => _cache.GetValueOrDefault(spec.TokenKey);

    public void Forget(AuthSpec spec) => _cache.Remove(spec.TokenKey);

    /// <summary>
    /// A usable token: the cached one while it lives, else a fresh grant.
    /// Returns null with a reason when the endpoint refuses or answers with
    /// something that isn't a token — the caller surfaces that instead of
    /// sending an unauthenticated request.
    /// </summary>
    public async Task<(OAuthToken? Token, string? Error)> TokenFor(
        AuthSpec spec, VariableResolver resolver, DateTimeOffset now, CancellationToken ct = default)
    {
        if (spec.Kind != AuthKind.OAuth2) return (null, null);
        if (Cached(spec) is { } cached && cached.IsValid(now)) return (cached, null);

        var url = resolver.Resolve(spec.A);
        var clientId = resolver.Resolve(spec.B);
        var clientSecret = resolver.Resolve(spec.C);
        var unresolved = url.Unresolved.Concat(clientId.Unresolved).Concat(clientSecret.Unresolved).Distinct().ToList();
        if (unresolved.Count > 0)
            return (null, $"unresolved in the auth config: {string.Join(", ", unresolved.Select(n => "{{" + n + "}}"))}");

        var form = new List<string>
        {
            "grant_type=client_credentials",
            "client_id=" + Uri.EscapeDataString(clientId.Text),
            "client_secret=" + Uri.EscapeDataString(clientSecret.Text),
        };
        if (spec.Scopes.Length > 0)
            form.Add("scope=" + Uri.EscapeDataString(resolver.Resolve(spec.Scopes).Text));

        var result = await engine.SendAsync(new RequestSpec("POST", url.Text,
            [("Accept", "application/json")], string.Join('&', form), "application/x-www-form-urlencoded"), ct);

        if (result.Failure is { } failure)
            return (null, $"token endpoint unreachable — {failure.Headline}");
        var response = result.Response!;
        if (response.StatusCode is < 200 or >= 300)
            return (null, $"token endpoint said {response.StatusCode}");

        var token = ParseToken(response.Body.Text, now, out var parseError);
        if (token == null) return (null, parseError);

        _cache[spec.TokenKey] = token;
        return (token, null);
    }

    /// <summary>The RFC 6749 token response. A missing <c>expires_in</c> means
    /// "no expiry given" — treated as an hour, the conventional default, rather
    /// than forever.</summary>
    internal static OAuthToken? ParseToken(string json, DateTimeOffset now, out string? error)
    {
        error = null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("access_token", out var access)
                || access.ValueKind != JsonValueKind.String
                || access.GetString() is not { Length: > 0 } value)
            {
                error = "the token endpoint's answer has no access_token";
                return null;
            }
            double seconds = doc.RootElement.TryGetProperty("expires_in", out var expires)
                && expires.ValueKind == JsonValueKind.Number
                ? expires.GetDouble()
                : 3600;
            return new OAuthToken(value, now.AddSeconds(seconds));
        }
        catch (JsonException)
        {
            error = "the token endpoint did not answer with JSON";
            return null;
        }
    }
}
