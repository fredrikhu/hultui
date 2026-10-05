using Xunit;

namespace HultUI.WebView.Engines.Tests;

/// <summary>
/// The loopback server <see cref="CefPolicyTests"/> serves its pages from. Its retry on a taken
/// port reused a listener that the failed start had disposed, so the first taken port failed
/// every test in the class — seen as <c>MenuEntries_AreNamedAsWebView2NamesThem</c>, the one that
/// runs on Windows, failing now and then though it drives no browser.
/// </summary>
public class CefPolicyServerTests
{
    [Fact]
    public async Task ATakenPortIsPassedOver()
    {
        using var first = new CefPolicyTests.Server();
        int taken = new Uri(first.Root).Port;

        var ports = new Queue<int>([taken]);
        using var second = new CefPolicyTests.Server(() => ports.Count > 0 ? ports.Dequeue() : CefPolicyTests.Server.FreePort());

        Assert.Empty(ports);
        Assert.NotEqual(taken, new Uri(second.Root).Port);
        using var http = new HttpClient();
        Assert.Contains("<title>hello</title>", await http.GetStringAsync(second.Root + "hello"));
    }
}
