using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Xunit;

namespace HultUI.WebView.Engines.Tests;

/// <summary>Skips unless CEF, a Wayland compositor and sway's IPC are present: the developer
/// tools are a native window, and sway's tree is what shows it.</summary>
public sealed class SwayCefFactAttribute : FactAttribute
{
    public SwayCefFactAttribute()
    {
        if (new WaylandCefFactAttribute().Skip is { } skip) Skip = skip;
        else if (CefDevToolsTests.SwaySocket() is null) Skip = "No sway IPC socket (SWAYSOCK).";
    }
}

/// <summary>
/// Developer tools on Linux (decided 2026-10-03): CEF's own, in a native window of their own,
/// opened by the call, by F12 or Ctrl+Shift+I, and by the context menu's "Inspect"; closed with
/// the page. Each runs in a process of its own on Wayland (this one's CEF is headless and
/// draws no windows), and sway's tree is asked what windows that process has.
/// </summary>
public class CefDevToolsTests
{
    internal static string? SwaySocket() =>
        Environment.GetEnvironmentVariable("SWAYSOCK") is { Length: > 0 } s ? s
        : Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is { Length: > 0 } run && Directory.Exists(run)
            ? Directory.GetFiles(run, "sway-ipc.*.sock").FirstOrDefault()
            : null;

    /// <summary>The titles of the windows sway shows for <paramref name="pid"/>.</summary>
    private static List<string> WindowsOf(int pid)
    {
        var start = new ProcessStartInfo("swaymsg", "-t get_tree -r") { RedirectStandardOutput = true };
        start.Environment["SWAYSOCK"] = SwaySocket();
        using var p = Process.Start(start)!;
        var json = p.StandardOutput.ReadToEnd();
        p.WaitForExit(5000);
        var found = new List<string>();
        void Walk(JsonElement n)
        {
            if (n.TryGetProperty("pid", out var owner) && owner.ValueKind == JsonValueKind.Number && owner.GetInt32() == pid
                && n.TryGetProperty("name", out var name) && name.GetString() is { } title)
                found.Add(title);
            foreach (var list in new[] { "nodes", "floating_nodes" })
                if (n.TryGetProperty(list, out var children))
                    foreach (var child in children.EnumerateArray()) Walk(child);
        }
        Walk(JsonDocument.Parse(json).RootElement);
        return found;
    }

    /// <summary>Runs the probe; the windows it has while it holds, and after its page went.</summary>
    private static (string Output, List<string> While, List<string> After) Open(string how)
    {
        var tfm = new DirectoryInfo(AppContext.BaseDirectory);
        var config = tfm.Parent!;
        var tests = config.Parent!.Parent!.Parent!;
        var dll = Path.Combine(tests.FullName, "HultUI.WebView.Engines.Tests.CefProcess", "bin", config.Name, tfm.Name,
            "HultUI.WebView.Engines.Tests.CefProcess.dll");
        var start = new ProcessStartInfo("dotnet", [dll, "--devtools-window", "wayland", how, "4000"])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = Process.Start(start)!;
        var output = new StringBuilder();
        _ = p.StandardError.ReadToEndAsync();
        List<string> during = [], after = [];
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed.TotalSeconds < 60 && p.StandardOutput.ReadLine() is { } line)
        {
            output.AppendLine(line);
            if (line == "holding") during = WindowsOf(p.Id);
            if (line == "disposed") after = WindowsOf(p.Id);
        }
        if (!p.WaitForExit(30_000)) p.Kill(entireProcessTree: true);
        return (output.ToString(), during, after);
    }

    private static void AssertOpened((string Output, List<string> While, List<string> After) run)
    {
        Assert.Contains("ready: True", run.Output);
        Assert.Contains("devtools open: True", run.Output);
        Assert.Contains(run.While, t => t.StartsWith("DevTools"));
        Assert.DoesNotContain(run.After, t => t.StartsWith("DevTools"));   // the tools went with the page
    }

    [SwayCefFact]
    public void TheCall_OpensANativeDevToolsWindow_ThatGoesWithThePage() => AssertOpened(Open("call"));

    [SwayCefFact]
    public void F12_OpensThem() => AssertOpened(Open("f12"));

    [SwayCefFact]
    public void CtrlShiftI_OpensThem() => AssertOpened(Open("ctrl-shift-i"));

    [SwayCefFact]
    public void Inspect_OnTheContextMenu_OpensThem()
    {
        var run = Open("inspect");
        Assert.Contains("menu asked: True", run.Output);
        Assert.Contains("inspectElement", run.Output);
        AssertOpened(run);
    }

    [SwayCefFact]
    public void F12_WithTheOptionOff_OpensNothing()
    {
        var run = Open("f12-off");
        Assert.Contains("ready: True", run.Output);
        Assert.Contains("devtools open: False", run.Output);
        Assert.DoesNotContain(run.While, t => t.StartsWith("DevTools"));
    }

    [Theory]
    [InlineData(HultUI.Core.Events.KeyCode.F12, HultUI.Core.Events.KeyModifiers.None, true)]
    [InlineData(HultUI.Core.Events.KeyCode.I, HultUI.Core.Events.KeyModifiers.Ctrl | HultUI.Core.Events.KeyModifiers.Shift, true)]
    [InlineData(HultUI.Core.Events.KeyCode.F12, HultUI.Core.Events.KeyModifiers.Shift, false)]
    [InlineData(HultUI.Core.Events.KeyCode.I, HultUI.Core.Events.KeyModifiers.Ctrl, false)]
    [InlineData(HultUI.Core.Events.KeyCode.I, HultUI.Core.Events.KeyModifiers.Ctrl | HultUI.Core.Events.KeyModifiers.Shift | HultUI.Core.Events.KeyModifiers.Alt, false)]
    public void TheDevToolsKeys_AreF12AndCtrlShiftI(HultUI.Core.Events.KeyCode key, HultUI.Core.Events.KeyModifiers modifiers, bool opens)
        => Assert.Equal(opens, HultUI.WebView.Cef.CefView.IsDevToolsKey(key, modifiers));
}
