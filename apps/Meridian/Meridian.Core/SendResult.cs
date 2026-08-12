namespace Meridian.Core;

/// <summary>What the engine was asked to send: the request after variable
/// resolution — the engine knows nothing about <c>{{vars}}</c>.</summary>
public sealed record RequestSpec(
    string Method,
    string Url,
    IReadOnlyList<(string Name, string Value)> Headers,
    string? BodyText = null,
    string? BodyContentType = null)
{
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// The four ways the network itself fails. Per the Azimuth status register these
/// are <b>warn, never fail</b> — an unreachable host and a wrong API response
/// must not share a colour — and each carries the mock's wording split into a
/// headline and a detail line.
/// </summary>
public enum InfraKind { DnsFailure, ConnectionRefused, TlsFailure, Timeout }

public sealed record InfraFailure(
    InfraKind Kind,
    string Headline,     // "could not resolve host"
    string Detail,       // "api.acme-pay.devv — NXDOMAIN"
    TimeSpan Elapsed);

public enum BodyKind { None, Text, Binary }

/// <summary>
/// A captured response body. Text bodies keep at most the viewer cap in
/// <see cref="Text"/>; a body over the cap is spilled — in full — to
/// <see cref="SpillPath"/> so "open full" can stream it. Binary bodies keep a
/// <see cref="Preview"/> for the hex stub instead of text.
/// </summary>
public sealed record ResponseBody(
    BodyKind Kind,
    string Text,
    byte[] Preview,
    long TotalBytes,
    bool Truncated,
    string? SpillPath)
{
    public static readonly ResponseBody None = new(BodyKind.None, "", [], 0, false, null);
}

/// <summary>
/// Where the time went, for the timeline waterfall. Connection-setup phases are
/// zero when the pool reused a connection (<see cref="ConnectionReused"/> —
/// the mock's "reused connection: no" line).
/// </summary>
public sealed record PhaseTimings(
    TimeSpan Dns,
    TimeSpan Connect,
    TimeSpan Tls,
    TimeSpan TimeToFirstByte,
    TimeSpan Download,
    TimeSpan Total,
    bool ConnectionReused,
    string HttpVersion,
    string? TlsProtocol);

public sealed record HttpResponseData(
    int StatusCode,
    string ReasonPhrase,
    IReadOnlyList<(string Name, string Value)> Headers,
    string? ContentType,
    ResponseBody Body,
    PhaseTimings Timings,
    string FinalUrl);

/// <summary>Either a response (any status — a 500 is a <i>successful send</i>)
/// or an infra failure. Never both.</summary>
public sealed record SendResult
{
    public HttpResponseData? Response { get; private init; }
    public InfraFailure? Failure { get; private init; }

    public static SendResult Ok(HttpResponseData response) => new() { Response = response };
    public static SendResult Infra(InfraFailure failure) => new() { Failure = failure };
}
