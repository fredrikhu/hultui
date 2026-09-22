namespace Aperture.Core;

/// <summary>Why a call to a Jellyfin server did not produce a value.</summary>
public enum ApertureFailureKind
{
    /// <summary>DNS, refused connection, TLS — the server was never reached or never
    /// trusted. The usual cause is a wrong address or a server that is off.</summary>
    Network,
    /// <summary>The request ran out its timeout.</summary>
    Timeout,
    /// <summary>401: the access token is missing, expired or was revoked in the
    /// dashboard. Sign in again.</summary>
    Unauthorized,
    /// <summary>403: the user exists but is not allowed this — a library they cannot
    /// see, or an administrative endpoint.</summary>
    Forbidden,
    /// <summary>404: no such item. A deleted film, or a library this user lost access to.</summary>
    NotFound,
    /// <summary>5xx: the server failed. Nothing the client did is wrong.</summary>
    Server,
    /// <summary>The address answered, but not as a Jellyfin server would — a router's
    /// login page, a reverse proxy error, a version whose payload we cannot read.</summary>
    NotJellyfin,
    /// <summary>The credentials were rejected at sign-in: wrong password, or a user the
    /// server does not have. Distinct from <see cref="Unauthorized"/>, which is a token
    /// that stopped working.</summary>
    BadCredentials,
}

/// <summary>A failed call: what kind, what the server said, and the HTTP status if there
/// was one.</summary>
public sealed record ApertureFailure(ApertureFailureKind Kind, string Message, int? Status = null)
{
    /// <summary>The sentence a view shows. <paramref name="what"/> names the thing that
    /// was being fetched, for the 404 wording ("Episode is no longer on jellyfin.home").</summary>
    public string UserMessage(string host, string what = "That") => Kind switch
    {
        ApertureFailureKind.Network => $"Can't reach {host}",
        ApertureFailureKind.Timeout => $"{host} did not answer in time",
        ApertureFailureKind.Unauthorized => "Your session expired — sign in again",
        ApertureFailureKind.Forbidden => "Your account isn't allowed to see that",
        ApertureFailureKind.NotFound => $"{what} is no longer on {host}",
        ApertureFailureKind.Server => $"{host} had a problem with that request",
        ApertureFailureKind.NotJellyfin => $"{host} answered, but not like a Jellyfin server",
        ApertureFailureKind.BadCredentials => "Wrong username or password",
        _ => Message,
    };
}

/// <summary>A value or a failure. Never both, never neither — the same shape Junction's
/// GitLab client uses, for the same reason: an HTTP error is an outcome the UI renders,
/// not an exception it has to catch at every call site.</summary>
public readonly struct ApertureResult<T>
{
    private readonly T? _value;

    public ApertureFailure? Failure { get; }
    public bool Ok => Failure is null;

    /// <summary>The value; throws when the call failed. Check <see cref="Ok"/> first, or
    /// use <see cref="TryGet"/>.</summary>
    public T Value => Failure is null
        ? _value!
        : throw new InvalidOperationException($"Jellyfin call failed: {Failure.Kind} — {Failure.Message}");

    private ApertureResult(T? value, ApertureFailure? failure)
    {
        _value = value;
        Failure = failure;
    }

    public static ApertureResult<T> Success(T value) => new(value, null);
    public static ApertureResult<T> Fail(ApertureFailure failure) => new(default, failure);
    public static ApertureResult<T> Fail(ApertureFailureKind kind, string message, int? status = null)
        => new(default, new ApertureFailure(kind, message, status));

    public bool TryGet(out T value)
    {
        value = _value!;
        return Ok;
    }

    /// <summary>Carries a failure across to a result of another type.</summary>
    public ApertureResult<TOther> As<TOther>() => ApertureResult<TOther>.Fail(Failure!);
}
