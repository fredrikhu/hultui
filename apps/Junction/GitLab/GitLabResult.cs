namespace Junction.GitLab;

/// <summary>Why a call to GitLab did not produce a value.</summary>
public enum GitLabFailureKind
{
    /// <summary>DNS, refused connection, TLS — the host was never reached or never trusted.</summary>
    Network,
    /// <summary>The request ran out its timeout.</summary>
    Timeout,
    /// <summary>401: the token is missing, expired or revoked. The design's copy is
    /// "Sign in again".</summary>
    Unauthorized,
    /// <summary>403: the token works but lacks the scope, or the user lacks the role.</summary>
    Forbidden,
    /// <summary>404: no such project / merge request — or a project this token cannot see,
    /// which GitLab also reports as 404.</summary>
    NotFound,
    /// <summary>405: the action is not allowed in this state (an unmergeable MR).</summary>
    NotAllowed,
    /// <summary>409: the resource moved under us (SHA guard on merge/approve).</summary>
    Conflict,
    /// <summary>422: the server understood and refused (validation).</summary>
    Unprocessable,
    /// <summary>429, with <see cref="GitLabFailure.RetryAfter"/> when the server said.</summary>
    RateLimited,
    /// <summary>5xx.</summary>
    Server,
    /// <summary>The body was not the JSON the model expects — a proxy login page, a
    /// non-GitLab host, a version this client does not know.</summary>
    Invalid,
}

/// <summary>A failed call: what kind, what the server said, and when to try again.</summary>
public sealed record GitLabFailure(
    GitLabFailureKind Kind,
    string Message,
    int? Status = null,
    TimeSpan? RetryAfter = null)
{
    /// <summary>
    /// The sentence the UI shows, per the design's failure copy. <paramref name="host"/> and
    /// <paramref name="what"/> fill the 404 wording ("Project not found on gitlab.com — was
    /// it moved?").
    /// </summary>
    public string UserMessage(string host, string what = "Project") => Kind switch
    {
        GitLabFailureKind.Unauthorized => "Sign in again",
        GitLabFailureKind.Forbidden => "Your token needs the api scope for this",
        GitLabFailureKind.NotFound => $"{what} not found on {host} — was it moved?",
        GitLabFailureKind.RateLimited => RetryAfter is { } r
            ? $"{host} asked us to slow down — retrying in {(int)Math.Ceiling(r.TotalSeconds)} s"
            : $"{host} asked us to slow down",
        GitLabFailureKind.Conflict => "Refresh and try again",
        GitLabFailureKind.Timeout => $"{host} did not answer in time",
        GitLabFailureKind.Network => Message,
        _ => Message,
    };
}

/// <summary>A value or a failure. Never both, never neither.</summary>
public readonly struct GitLabResult<T>
{
    private readonly T? _value;

    public GitLabFailure? Failure { get; }
    public bool Ok => Failure is null;

    /// <summary>The value; throws when the call failed. Check <see cref="Ok"/> first, or
    /// use <see cref="TryGet"/>.</summary>
    public T Value => Failure is null
        ? _value!
        : throw new InvalidOperationException($"GitLab call failed: {Failure.Kind} — {Failure.Message}");

    private GitLabResult(T? value, GitLabFailure? failure)
    {
        _value = value;
        Failure = failure;
    }

    public static GitLabResult<T> Success(T value) => new(value, null);
    public static GitLabResult<T> Fail(GitLabFailure failure) => new(default, failure);
    public static GitLabResult<T> Fail(GitLabFailureKind kind, string message, int? status = null, TimeSpan? retryAfter = null)
        => new(default, new GitLabFailure(kind, message, status, retryAfter));

    public bool TryGet(out T value)
    {
        value = _value!;
        return Ok;
    }

    /// <summary>Carries a failure across to a result of another type.</summary>
    public GitLabResult<TOther> As<TOther>() => GitLabResult<TOther>.Fail(Failure!);
}
