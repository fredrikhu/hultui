using System.Net;
using HultUI.WebView.Cef;
using Xunit;

namespace HultUI.WebView.Engines.Tests;

/// <summary>
/// <see cref="WebViewOptions.ContentFolder"/> on CEF (decided 2026-10-03): the rules here, the
/// serving through the CEF test process (<c>--content-folder</c>), each run with a profile of
/// its own.
/// </summary>
public class CefContentFolderTests
{
    // ──── Rules ────

    [Theory]
    [InlineData("f.html", "text/html", "utf-8")]
    [InlineData("f.htm", "text/html", "utf-8")]
    [InlineData("f.css", "text/css", "utf-8")]
    [InlineData("f.js", "text/javascript", "utf-8")]
    [InlineData("f.mjs", "text/javascript", "utf-8")]
    [InlineData("f.json", "application/json", "utf-8")]
    [InlineData("f.svg", "image/svg+xml", "utf-8")]
    [InlineData("f.txt", "text/plain", "utf-8")]
    [InlineData("f.png", "image/png", null)]
    [InlineData("f.jpeg", "image/jpeg", null)]
    [InlineData("f.JPG", "image/jpeg", null)]
    [InlineData("f.gif", "image/gif", null)]
    [InlineData("f.webp", "image/webp", null)]
    [InlineData("f.ico", "image/x-icon", null)]
    [InlineData("f.woff", "font/woff", null)]
    [InlineData("f.woff2", "font/woff2", null)]
    [InlineData("f.wasm", "application/wasm", null)]
    [InlineData("f.mp4", "video/mp4", null)]
    [InlineData("f.webm", "video/webm", null)]
    [InlineData("f.mp3", "audio/mpeg", null)]
    [InlineData("f.ogg", "audio/ogg", null)]
    [InlineData("f", "application/octet-stream", null)]
    [InlineData("f.bin", "application/octet-stream", null)]
    public void EachListedExtension_HasItsType_AndTextACharset(string file, string mime, string? charset)
        => Assert.Equal((mime, charset), ContentFolder.TypeOf(file));

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("bytes=10-19", 10L, 19L)]
    [InlineData("bytes=990-", 990L, 999L)]
    [InlineData("bytes=-5", 995L, 999L)]
    [InlineData("bytes=-5000", 0L, 999L)]
    [InlineData("bytes=995-2000", 995L, 999L)]
    [InlineData("bytes=1000-", 1000L, 999L)]   // past the end: unsatisfiable, answered 416
    [InlineData("bytes=0-1,5-6", null, null)]  // several: the whole file
    [InlineData("bytes=5-1", null, null)]
    [InlineData("items=0-1", null, null)]
    public void ARange_IsOneOrNone(string? header, long? first, long? last)
    {
        var range = ContentFolder.RangeOf(header, 1000);
        Assert.Equal(first, range?.First);
        Assert.Equal(last, range?.Last);
    }

    [LinuxFact]
    public void APath_ThatLeavesTheFolder_IsRefused()
    {
        var temp = Directory.CreateTempSubdirectory("hultui-content-rules-").FullName;
        try
        {
            var folder = Directory.CreateDirectory(Path.Combine(temp, "web")).FullName;
            Directory.CreateDirectory(Path.Combine(folder, "sub"));
            File.WriteAllText(Path.Combine(folder, "index.html"), "");
            File.WriteAllText(Path.Combine(folder, "sub", "inner.txt"), "");
            File.WriteAllText(Path.Combine(temp, "outside.txt"), "");
            File.CreateSymbolicLink(Path.Combine(folder, "link.txt"), Path.Combine(temp, "outside.txt"));
            File.CreateSymbolicLink(Path.Combine(folder, "linkdir"), temp);
            File.CreateSymbolicLink(Path.Combine(folder, "inlink.txt"), Path.Combine(folder, "sub", "inner.txt"));

            Assert.Equal((Path.Combine(folder, "index.html"), 200), ContentFolder.Resolve(folder, "/index.html"));
            Assert.Equal((Path.Combine(folder, "sub", "inner.txt"), 200), ContentFolder.Resolve(folder, "/sub/inner.txt"));
            Assert.Equal((Path.Combine(folder, "sub", "inner.txt"), 200), ContentFolder.Resolve(folder, "/sub%2Finner.txt"));
            Assert.Equal(200, ContentFolder.Resolve(folder, "/inlink.txt").Status);   // a link that stays inside
            Assert.Equal(200, ContentFolder.Resolve(folder, "/index.html?x=1").Status);
            foreach (var path in new[] { "/../outside.txt", "/%2e%2e/outside.txt", "/%2E%2E%2Foutside.txt", "/..%2foutside.txt",
                                         "/sub/..%2f..%2foutside.txt", "/sub/../../outside.txt", "/link.txt", "/linkdir/outside.txt",
                                         "/index.html%00.txt" })
                Assert.True(ContentFolder.Resolve(folder, path) is (null, 403), path);
            Assert.Equal((null, 404), ContentFolder.Resolve(folder, "/missing.txt"));
            Assert.Equal((null, 404), ContentFolder.Resolve(folder, "/sub/"));   // a folder
            Assert.Equal((null, 404), ContentFolder.Resolve(folder, "/"));       // the folder's own address
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    // ──── Processes ────

    private static (int ExitCode, string Output) Run(string what)
        => CefEngineTests.RunCefProcessWith(Guid.NewGuid().ToString("N"), ["--content-folder", what], null);

    private static bool Online()
    {
        try { return Dns.GetHostAddresses("example.org").Length > 0; }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException or ArgumentException) { return false; }
    }

    /// <summary>The Windows test's twin, and the rest of the serving, in one run.</summary>
    [CefFact]
    public void AFolder_IsServedAtTheAppOrigin_ToItsOwnPageOnly()
    {
        var run = Run("serve");
        Assert.True(run.ExitCode == 0, run.Output);
        // The stylesheet and the JSON by relative paths, and the message accepted by default;
        // a second page with a folder of its own gets its own at the same address.
        Assert.Contains("page a: \"rgb(10, 200, 30) 42\" from https://app.example/", run.Output);
        Assert.Contains("page b: \"rgb(200, 10, 30) 7\" from https://app.example/", run.Output);
        Assert.Contains("accepted by default: True", run.Output);
        // Each type as served.
        foreach (var t in new[] { "html=text/html; charset=utf-8", "css=text/css; charset=utf-8", "js=text/javascript; charset=utf-8",
                                  "mjs=text/javascript; charset=utf-8", "json=application/json; charset=utf-8",
                                  "svg=image/svg+xml; charset=utf-8", "png=image/png", "jpeg=image/jpeg", "jpg=image/jpeg", "gif=image/gif",
                                  "webp=image/webp", "ico=image/x-icon", "woff=font/woff", "woff2=font/woff2", "wasm=application/wasm",
                                  "mp4=video/mp4", "webm=video/webm", "mp3=audio/mpeg", "ogg=audio/ogg", "txt=text/plain; charset=utf-8",
                                  "bin=application/octet-stream" })
            Assert.Contains(" " + t, run.Output.Replace("types: \"", "types: \" "));
        // Ranges, with the bytes they name.
        Assert.Contains("bytes=10-19:206,bytes 10-19/1000,10,10..19 ", run.Output);
        Assert.Contains("bytes=990-:206,bytes 990-999/1000,10,222..231 ", run.Output);
        Assert.Contains("bytes=-5:206,bytes 995-999/1000,5,227..231 ", run.Output);
        Assert.Contains("bytes=1000-:416,bytes */1000,0,- ", run.Output);
        Assert.Contains("bytes=0-1,5-6:200,null,1000,0..231", run.Output);
        // The ways out: refused (Chromium itself folds a plain %2e%2e/, which then misses).
        Assert.Contains("paths: \"/..%2foutside.txt=403 /%2e%2e%2foutside.txt=403 /%2e%2e/outside.txt=404 " +
                        "/sub/..%2f..%2foutside.txt=403 /link.txt=403 /linkdir/outside.txt=403 /inlink.txt=200 " +
                        "/sub/inner.txt=200 /sub%2finner.txt=200 /sub/=404 /=404 /missing.txt=404\"", run.Output);
        // Another origin may run a file as a script but not read one: no CORS headers.
        Assert.Contains("""from another origin: "{\"script\":\"loaded\",\"fetch\":\"refused\"}""", run.Output);
        // Without the option, the address is nothing.
        Assert.Contains("without a folder: failed HostNameNotResolved", run.Output);
    }

    [CefFact]
    public void WithoutTheSandbox_AFolder_LoadsWithItsFiles_AndNothingElse()
    {
        var run = Run("own");
        Assert.True(run.ExitCode == 0, run.Output);
        Assert.Contains("sandboxed: False", run.Output);
        Assert.Contains("own page: loaded", run.Output);
        // Its files load; the frame and the WebSocket are refused by its policy before they are
        // requests, and the service worker's fetch gets nowhere.
        Assert.Contains("""own page saw: "{\"data\":\"rgb(10, 200, 30) 42\",\"socket\":\"error\",\"worker\":\"blocked\",""" +
                        """\"violations\":[\"wss://ws.postman-echo.com/raw\",\"https://example.org\"]}""", run.Output);
    }

    /// <summary>The review's point 2: a service worker's requests have no page, and the request
    /// context's handler refuses them. Seen without the stricter policy, which would stop the
    /// fetch first.</summary>
    [CefFact]
    public void WithoutTheSandbox_AServiceWorkersRequests_PassTheFilter()
    {
        var run = Run("own-filter");
        Assert.True(run.ExitCode == 0, run.Output);
        Assert.Contains("refused without Chromium's sandbox (not the application's own content): https://example.org/from-a-service-worker", run.Output);
        Assert.Contains("refused without Chromium's sandbox (not the application's own content): https://example.org/frame", run.Output);
        Assert.Contains("\\\"worker\\\":\\\"blocked\\\"", run.Output);
        if (!Online()) return;
        // With the sandbox, the same worker reaches the internet: the probe can tell.
        var on = Run("own-sandboxed");
        Assert.Contains("\\\"worker\\\":\\\"loaded\\\"", on.Output);
    }

    /// <summary>
    /// WebView2 waited two seconds for an mDNS lookup on every navigation to <c>app.local</c>,
    /// which is why the origin is <c>app.example</c>. CEF does not: its scheme handler answers
    /// before any lookup, for either name. Kept as a guard for the origin.
    /// </summary>
    [CefFact]
    public void TheOrigin_LoadsInMilliseconds()
    {
        var run = Run("timing");
        Assert.True(run.ExitCode == 0, run.Output);
        foreach (var origin in new[] { WebViewOptions.AppOrigin, "https://app.local" })
        {
            var line = run.Output.Split('\n').First(l => l.StartsWith(origin + ":"));
            var times = line[(origin.Length + 1)..].Replace("ms", "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(long.Parse).ToList();
            Assert.All(times, t => Assert.True(t >= 0, line));
            Assert.All(times.Skip(1), t => Assert.True(t < 500, line));
        }
    }
}
