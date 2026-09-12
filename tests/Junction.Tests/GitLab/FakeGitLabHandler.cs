using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;

namespace Junction.Tests.GitLab;

/// <summary>
/// Plays recorded GitLab responses back to the client under test. Routes are matched on
/// <c>METHOD /path?query</c> exactly as the client sends them, so a test that maps
/// <c>GET /api/v4/projects/acme%2Fcore-api</c> also proves the path was encoded. Every
/// request is kept for assertions on what went over the wire — the header a token rode in,
/// the form a code exchange posted.
/// </summary>
internal sealed class FakeGitLabHandler : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> _routes = new(StringComparer.Ordinal);
    private readonly List<(HttpRequestMessage Request, string? Body)> _seen = [];

    public IReadOnlyList<(HttpRequestMessage Request, string? Body)> Seen => _seen;

    public FakeGitLabHandler Map(string route, Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        _routes[route] = respond;
        return this;
    }

    public FakeGitLabHandler Map(string route, string recording, int status = 200, params (string Name, string Value)[] headers) =>
        Map(route, _ => Json(Recording(recording), status, headers));

    public FakeGitLabHandler MapJson(string route, string json, int status = 200, params (string Name, string Value)[] headers) =>
        Map(route, _ => Json(json, status, headers));

    public FakeGitLabHandler Throw(string route, Exception ex) => Map(route, _ => throw ex);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string? body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        _seen.Add((request, body));
        var key = $"{request.Method} {request.RequestUri!.PathAndQuery}";
        if (_routes.TryGetValue(key, out var respond)) return respond(request);
        return Json("{\"message\":\"404 Not Found (no route " + key.Replace("\"", "'") + ")\"}", 404);
    }

    public static HttpResponseMessage Json(string json, int status = 200, params (string Name, string Value)[] headers)
    {
        var res = new HttpResponseMessage((HttpStatusCode)status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        foreach (var (name, value) in headers) res.Headers.TryAddWithoutValidation(name, value);
        return res;
    }

    public static HttpResponseMessage Html(string html, int status = 200) => new((HttpStatusCode)status)
    {
        Content = new StringContent(html, Encoding.UTF8, "text/html"),
    };

    /// <summary>The embedded <c>GitLab/Recordings/&lt;name&gt;.json</c>.</summary>
    public static string Recording(string name)
    {
        var asm = Assembly.GetExecutingAssembly();
        var resource = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(".Recordings." + name + ".json", StringComparison.Ordinal))
            ?? throw new FileNotFoundException($"No recording named {name}.json is embedded");
        using var stream = asm.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
