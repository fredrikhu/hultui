using System.Text;

namespace Meridian.Core;

public enum AuthKind { None, Basic, Bearer, ApiKey, OAuth2 }

/// <summary>
/// A parsed <c>auth</c> line. The file stores <i>which keys to use</i>, never
/// the values: every field here is a template, so the secrets live in the
/// environment and the keyring (P4) rather than in the collection.
/// <code>
/// auth none
/// auth basic {{user}} {{password}}
/// auth bearer {{token}}
/// auth apikey header X-Api-Key {{api_key}}
/// auth oauth2 client_credentials {{base_url}}/oauth/token {{client_id}} {{client_secret}} charges:write
/// </code>
/// </summary>
public sealed record AuthSpec(
    AuthKind Kind,
    string A = "",
    string B = "",
    string C = "",
    string D = "",
    string Scopes = "")
{
    public static readonly AuthSpec None = new(AuthKind.None);

    /// <summary>OAuth2's cache key: one token per (endpoint, client), so two
    /// requests sharing a client share the token, and changing the client id
    /// gets a fresh one rather than a stale grant.</summary>
    public string TokenKey => $"oauth2|{A}|{B}|{Scopes}";
}

public static class Auth
{
    public static AuthSpec? Parse(string args, out string? error)
    {
        error = null;
        var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) { error = "auth needs a type"; return null; }

        switch (parts[0].ToLowerInvariant())
        {
            case "none":
                return AuthSpec.None;

            case "basic" when parts.Length >= 3:
                return new AuthSpec(AuthKind.Basic, parts[1], parts[2]);
            case "basic":
                error = "auth basic needs a user and a password";
                return null;

            case "bearer" when parts.Length >= 2:
                return new AuthSpec(AuthKind.Bearer, parts[1]);
            case "bearer":
                error = "auth bearer needs a token";
                return null;

            case "apikey" when parts.Length >= 4 && parts[1] is "header" or "query":
                return new AuthSpec(AuthKind.ApiKey, parts[1], parts[2], parts[3]);
            case "apikey":
                error = "auth apikey needs: header|query <name> <value>";
                return null;

            case "oauth2" when parts.Length >= 5 && parts[1] == "client_credentials":
                return new AuthSpec(AuthKind.OAuth2, parts[2], parts[3], parts[4], "",
                    string.Join(' ', parts.Skip(5)));
            case "oauth2":
                error = "auth oauth2 needs: client_credentials <token-url> <client-id> <client-secret> [scopes]";
                return null;

            default:
                error = $"unknown auth type \"{parts[0]}\"";
                return null;
        }
    }

    public static string Format(AuthSpec spec) => spec.Kind switch
    {
        AuthKind.Basic => $"basic {spec.A} {spec.B}",
        AuthKind.Bearer => $"bearer {spec.A}",
        AuthKind.ApiKey => $"apikey {spec.A} {spec.B} {spec.C}",
        AuthKind.OAuth2 => $"oauth2 client_credentials {spec.A} {spec.B} {spec.C}"
            + (spec.Scopes.Length > 0 ? " " + spec.Scopes : ""),
        _ => "none",
    };

    /// <summary>
    /// The header (or query pair) an auth spec contributes, with every template
    /// already resolved. OAuth2 is not here — it needs a network round trip, so
    /// <see cref="OAuth2.TokenFor"/> fetches the token and the caller passes it
    /// in as <paramref name="oauthToken"/>.
    /// </summary>
    public static (string Name, string Value)? HeaderFor(
        AuthSpec spec, VariableResolver resolver, string? oauthToken, out IReadOnlyList<string> unresolved)
    {
        var missing = new List<string>();
        string Resolve(string template)
        {
            var result = resolver.Resolve(template);
            missing.AddRange(result.Unresolved);
            return result.Text;
        }

        (string, string)? header = spec.Kind switch
        {
            AuthKind.Basic => ("Authorization",
                "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Resolve(spec.A)}:{Resolve(spec.B)}"))),
            AuthKind.Bearer => ("Authorization", "Bearer " + Resolve(spec.A)),
            AuthKind.ApiKey when spec.A == "header" => (Resolve(spec.B), Resolve(spec.C)),
            AuthKind.OAuth2 when oauthToken != null => ("Authorization", "Bearer " + oauthToken),
            _ => null,
        };
        unresolved = missing;
        return header;
    }

    /// <summary>The query pair an <c>apikey query</c> spec adds; null otherwise.</summary>
    public static (string Name, string Value)? QueryFor(AuthSpec spec, VariableResolver resolver) =>
        spec.Kind == AuthKind.ApiKey && spec.A == "query"
            ? (resolver.Resolve(spec.B).Text, resolver.Resolve(spec.C).Text)
            : null;

    /// <summary>
    /// The auth a request actually uses: its own <c>auth</c> line, or the
    /// nearest <c>_folder.req</c> above it that has one. Walking up stops at
    /// the collection root — a folder default is a folder's business, not the
    /// whole disk's.
    /// </summary>
    public static (AuthSpec Spec, string? InheritedFrom) Effective(
        string requestPath, ReqDocument doc, string collectionRoot, Func<string, ReqDocument?> readFolderFile)
    {
        if (doc.Auth is { } own && Parse(own.Args, out _) is { } ownSpec)
            return (ownSpec, null);

        var dir = Path.GetDirectoryName(Path.GetFullPath(requestPath));
        var root = Path.GetFullPath(collectionRoot);
        // The StartsWith guard IS the boundary: stepping to the root's parent
        // ends the loop, so a _folder.req outside the collection is never read.
        while (dir != null && dir.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            var folderFile = Path.Combine(dir, "_folder.req");
            if (readFolderFile(folderFile) is { Auth: { } inherited }
                && Parse(inherited.Args, out _) is { } spec)
                return (spec, dir);
            dir = Path.GetDirectoryName(dir);
        }
        return (AuthSpec.None, null);
    }
}
