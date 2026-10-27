using System.Reflection;
using System.Text;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Platform;
using HultUI.Headless;
using Meridian.Core;
using Meridian.Tests.Fixtures;
using Meridian.Theme;
using Xunit;

namespace Meridian.Tests;

/// <summary>
/// The title bar, the rail, the request tree and the response panel are parts — child
/// components the app keeps while it rebuilds around them (MeridianApp.Chrome.cs). The risk is a
/// value they show changing while they are kept: a stale unsaved dot, an old environment, a row
/// still lit, a response tab that did not follow a click.
///
/// <para>The oracle is a full build. Every check here compares the parts on screen with the parts
/// a forced rebuild of all of them makes, after the edits and moves that keep them (which must
/// not build them) and after each value they show changes the way the app changes it.</para>
/// </summary>
public class PartTests : IClassFixture<TestHttpServer>, IDisposable
{
    private readonly TestHttpServer _server;
    private readonly string _root = Directory.CreateTempSubdirectory("meridian-parts-").FullName;

    public PartTests(TestHttpServer server)
    {
        _server = server;
        Write("auth/login.req", "POST {{base_url}}/echo\nname Login\nheader Accept: application/json\n");
        Write("get-ok.req", $"GET {_server.Origin}/ok\nname Get ok\nheader Accept: application/json\n");
        Write("two.req", $"GET {_server.Origin}/two\nname Two\n");
        Write("stall.req", $"GET {_server.Origin}/stall\nname Stall\n");
        Write("params.req", $"GET {_server.Origin}/ok?page=1&size=20\nname Paged\n");
        for (int i = 0; i < 4; i++)
            Write($"orders/o{i}.req", $"GET {_server.Origin}/ok\nname Order {i}\n");
        Write("envs/local.env", $"base_url={_server.Origin}\nhost=127.0.0.1\n");
        Write("envs/staging.env", "base_url=http://staging.invalid\nhost=staging.invalid\n");
        var start = DateTimeOffset.Now.AddMinutes(-90);
        for (int i = 0; i < 12; i++)
            HistoryLog.Append(_root, new HistoryEntry(start.AddMinutes(i), i % 3 == 0 ? "POST" : "GET",
                $"https://api.x/v1/items/{i}", i % 5 == 4 ? 404 : 200, 10 + i, null, null));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private void Write(string relative, string content)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    // ── The view-only paths ──────────────────────────────────────────────

    /// <summary>Every path that rebuilds the view and keeps the parts: typing in the URL, the
    /// headers, the params, the body and the assertions, adding a row, the sub-tabs, the URL box
    /// opening and closing, History's walk, search, row clicks and filters, and the environment
    /// table's values. Each rebuilds the window, builds no part, and leaves the parts exactly as a
    /// full build would make them.</summary>
    [Fact]
    public void ViewOnlyEdits_RebuildTheView_AndKeepThePartsAFullBuildWouldMake()
    {
        Hosted(fx =>
        {
            var s = fx.Session;
            void ViewOnly(string name, Action act)
            {
                AssertPartsAreAFullBuild(fx, "before " + name);
                int builds = fx.App.Builds, parts = fx.App.PartBuilds;
                act();
                s.PumpFrames(3);
                Assert.True(fx.App.Builds > builds, $"{name} did not rebuild the window");
                Assert.True(fx.App.PartBuilds == parts, $"{name} built {fx.App.PartBuilds - parts} parts");
                AssertPartsAreAFullBuild(fx, "after " + name);
            }

            // The URL. The first character makes the request unsaved, which the tree shows (see
            // TheFirstEdit…): typed before the view-only keys.
            Click(fx, "req-login");
            ViewOnly("the URL box opening", () => Click(fx, "url-display"));
            s.Window.TypeText("/");
            s.PumpFrames(3);
            ViewOnly("URL typing", () => s.Window.TypeText("v2"));
            ViewOnly("URL Backspace", () => s.Window.PressKey(KeyCode.Backspace));
            ViewOnly("the URL box closing", () => s.Window.Window.ClearFocus());

            // Headers, the sub-tabs, the body and the assertions of the same request.
            ViewOnly("header value typing", () => { Click(fx, "header-value-0"); s.Window.TypeText("+json"); });
            ViewOnly("header name typing", () => { Click(fx, "header-name-0"); s.Window.TypeText("X"); });
            ViewOnly("adding a header", () => Click(fx, "add-header"));
            ViewOnly("the Params sub-tab", () => Click(fx, "subtab-params"));
            ViewOnly("the Body sub-tab", () => Click(fx, "subtab-body"));
            ViewOnly("body typing", () => { Click(fx, "body-editor"); s.Window.TypeText("{}"); });
            ViewOnly("the Assertions sub-tab", () => Click(fx, "subtab-assertions"));
            ViewOnly("adding an assertion", () => Click(fx, "starter-status"));
            ViewOnly("assertion typing", () => { Click(fx, "assert-expected-0"); s.Window.TypeText("1"); });
            ViewOnly("the Headers sub-tab", () => Click(fx, "subtab-headers"));
            s.Window.Window.ClearFocus();
            s.PumpFrames(2);

            // Params, on a request of their own: its first edit makes it unsaved.
            Click(fx, "req-params");
            Click(fx, "subtab-params");
            Click(fx, "param-value-0");
            s.Window.TypeText("0");
            s.PumpFrames(3);
            ViewOnly("param value typing", () => s.Window.TypeText("5"));
            ViewOnly("param name typing", () => { Click(fx, "param-name-1"); s.Window.TypeText("s"); });
            s.Window.Window.ClearFocus();
            s.PumpFrames(2);

            // History.
            Switch(fx, KeyCode.Digit4);
            ViewOnly("History Down", () => s.Window.PressKey(KeyCode.Down));
            ViewOnly("History Down again", () => s.Window.PressKey(KeyCode.Down));
            ViewOnly("History Up", () => s.Window.PressKey(KeyCode.Up));
            ViewOnly("History Enter", () => s.Window.PressKey(KeyCode.Enter));
            ViewOnly("a History row clicked", () =>
                s.Window.Click(Descendants(Root(s)).First(e => e.AutomationId == "hist-row-3")));
            ViewOnly("a History filter", () => Click(fx, "filter-GET"));
            ViewOnly("the History filter off", () => Click(fx, "filter-GET"));
            ViewOnly("History search typing", () => { Click(fx, "history-search"); s.Window.TypeText("items/1"); });
            s.Window.Window.ClearFocus();
            s.PumpFrames(2);

            // The environment table.
            Switch(fx, KeyCode.Digit5);
            ViewOnly("environment value typing", () => { Click(fx, "cell-host-local"); s.Window.TypeText(".2"); });
        });
    }

    /// <summary>Typing in the URL makes no part per key, whatever the size of the collection — the
    /// tree's rows are the same elements key after key.</summary>
    [Fact]
    public void URLTyping_MakesNoPart_AndKeepsTheTreesRows()
    {
        for (int i = 0; i < 40; i++)
            Write($"bulk/b{i:00}.req", $"GET {_server.Origin}/ok\nname Bulk {i}\n");
        Hosted(fx =>
        {
            var s = fx.Session;
            fx.App.OpenRequest(Path.Combine(_root, "get-ok.req"));   // below the fold of the tree
            s.PumpFrames(3);
            Click(fx, "url-display");
            s.Window.TypeText("?");
            s.PumpFrames(3);
            var rows = Descendants(Part(fx, "tree")).ToList();
            Assert.True(rows.Count > 150, $"the tree has {rows.Count} elements");
            int builds = fx.App.Builds, parts = fx.App.PartBuilds;
            for (int i = 0; i < 20; i++)
            {
                if (i % 2 == 0) s.Window.TypeText("a"); else s.Window.PressKey(KeyCode.Backspace);
                s.PumpFrames(2);
            }
            Assert.Equal(20, fx.App.Builds - builds);
            Assert.Equal(0, fx.App.PartBuilds - parts);
            Assert.Equal(rows, Descendants(Part(fx, "tree")).ToList());
            AssertPartsAreAFullBuild(fx);
        });
    }

    // ── What the parts show ──────────────────────────────────────────────

    /// <summary>The tree's unsaved dot: the first edit puts it on the request's row, Ctrl+S takes
    /// it off. The row's method is the file's: a method picked in the editor reaches the tree
    /// when it is saved.</summary>
    [Fact]
    public void TheFirstEdit_PutsTheUnsavedDotOnTheTreeRow_AndSavingTakesItOff()
    {
        Hosted(fx =>
        {
            var s = fx.Session;
            Click(fx, "req-get-ok");
            Assert.False(HasDot(TreeRow(fx, "req-get-ok")));
            Click(fx, "url-display");
            int parts = fx.App.PartBuilds;
            s.Window.TypeText("x");
            s.PumpFrames(3);
            Assert.True(fx.App.PartBuilds > parts, "the edit that made the request unsaved built no part");
            Assert.True(HasDot(TreeRow(fx, "req-get-ok")), "no unsaved dot on the tree row");
            AssertPartsAreAFullBuild(fx);

            s.Window.TypeText("yz");
            s.PumpFrames(3);
            Assert.True(HasDot(TreeRow(fx, "req-get-ok")));
            s.Window.Window.ClearFocus();
            s.PumpFrames(2);
            AssertPartsAreAFullBuild(fx);

            s.Window.PressKey(KeyCode.S, KeyModifiers.Ctrl);
            s.PumpFrames(3);
            Assert.False(HasDot(TreeRow(fx, "req-get-ok")), "the dot stayed after the save");
            AssertPartsAreAFullBuild(fx);

            Click(fx, "method-chip");
            Click(fx, "popover-PUT");
            Assert.Equal("GET Get ok", TreeRow(fx, "req-get-ok").AccessibleName);
            Assert.True(HasDot(TreeRow(fx, "req-get-ok")));
            AssertPartsAreAFullBuild(fx);
            s.Window.PressKey(KeyCode.S, KeyModifiers.Ctrl);
            s.PumpFrames(3);
            Assert.Equal("PUT Get ok", TreeRow(fx, "req-get-ok").AccessibleName);
            Assert.False(HasDot(TreeRow(fx, "req-get-ok")));
            AssertPartsAreAFullBuild(fx);
        });
    }

    /// <summary>Which request is open: its row lit in the tree, through tree clicks, tab clicks and
    /// closing a tab; with no tab left, the response panel goes.</summary>
    [Fact]
    public void TheLitRow_FollowsTheOpenRequest()
    {
        Hosted(fx =>
        {
            var s = fx.Session;
            Assert.Null(TryPart(fx, "response-right"));
            Click(fx, "req-login");
            Assert.True(Lit(TreeRow(fx, "req-login")));
            Assert.NotNull(TryPart(fx, "response-right"));
            Click(fx, "req-two");
            Assert.True(Lit(TreeRow(fx, "req-two")));
            Assert.False(Lit(TreeRow(fx, "req-login")));
            AssertPartsAreAFullBuild(fx);

            Click(fx, "request-tab-0");
            Assert.True(Lit(TreeRow(fx, "req-login")));
            Assert.False(Lit(TreeRow(fx, "req-two")));
            AssertPartsAreAFullBuild(fx);

            s.Window.PressKey(KeyCode.W, KeyModifiers.Ctrl);
            s.PumpFrames(3);
            Assert.True(Lit(TreeRow(fx, "req-two")));
            s.Window.PressKey(KeyCode.W, KeyModifiers.Ctrl);
            s.PumpFrames(3);
            Assert.False(Lit(TreeRow(fx, "req-two")));
            Assert.Null(TryPart(fx, "response-right"));
            AssertPartsAreAFullBuild(fx);
        });
    }

    /// <summary>A response: it reaches the panel when the send ends, its body when the worker has
    /// formatted it, and the panel's own controls — the find bar, the tabs, pretty and raw —
    /// rebuild the panel alone, never the window.</summary>
    [Fact]
    public void AResponseReachesThePanel_AndItsControlsRebuildThePanelAlone()
    {
        Hosted(fx =>
        {
            var s = fx.Session;
            Click(fx, "req-two");
            Assert.True(HasText(Part(fx, "response-right"), "never sent"));
            Click(fx, "send");
            s.PumpUntil(() => s.Window.FindByAutomationId("response-body") is not null, Wait);
            s.PumpFrames(3);
            Assert.True(HasText(Part(fx, "response-right"), "✓ 200 OK"));
            AssertPartsAreAFullBuild(fx);

            // `then` checks what the step did before the oracle's forced build, whose OnAfterBuild
            // would service the find bar whether the panel's own builds do or not.
            void PanelOnly(string name, Action act, Action? then = null)
            {
                int builds = fx.App.Builds, parts = fx.App.PartBuilds;
                act();
                s.PumpFrames(3);
                Assert.True(fx.App.Builds == builds, $"{name} rebuilt the window");
                Assert.True(fx.App.PartBuilds > parts, $"{name} did not rebuild the panel");
                then?.Invoke();
                AssertPartsAreAFullBuild(fx, "after " + name);
            }

            s.Window.Window.ClearFocus();
            s.PumpFrames(2);
            PanelOnly("the find bar opening", () => s.Window.PressKey(KeyCode.F, KeyModifiers.Ctrl),
                () => Assert.Equal("find-input", s.Window.Window.FocusedElement?.AutomationId));
            PanelOnly("find typing", () => s.Window.TypeText("a"),
                () => Assert.True(HasTextStarting(Part(fx, "response-right"), "1 of "), "the count did not follow the query"));
            PanelOnly("the next match", () => s.Window.PressKey(KeyCode.Enter),
                () => Assert.True(HasTextStarting(Part(fx, "response-right"), "2 of "), "the count did not follow Enter"));
            PanelOnly("the find bar closing", () => s.Window.PressKey(KeyCode.Escape),
                () => Assert.Equal("response-body", s.Window.Window.FocusedElement?.AutomationId));
            PanelOnly("the Headers tab", () => Click(fx, "resptab-headers"));
            PanelOnly("the Timeline tab", () => Click(fx, "resptab-timeline"));
            PanelOnly("the Body tab", () => Click(fx, "resptab-body"));
            PanelOnly("raw", () => Click(fx, "body-raw"));
            PanelOnly("pretty", () => Click(fx, "body-pretty"));
        });
    }

    /// <summary>A send in flight: its elapsed readout keeps counting on screen while the URL is
    /// typed — the kept panel keeps the readout the tick rewrites.</summary>
    [Fact]
    public void ASendInFlight_KeepsItsReadoutCounting_WhileTheURLIsTyped()
    {
        Hosted(fx =>
        {
            var s = fx.Session;
            Click(fx, "req-stall");
            Click(fx, "send");
            s.PumpUntil(() => s.Window.FindByAutomationId("send-elapsed") is not null, Wait);
            var readout = (TextElement)s.Window.FindByAutomationId("send-elapsed")!.Element;
            Click(fx, "url-display");
            s.Window.TypeText("/x");
            s.PumpFrames(3);
            // A view-only build between every two ticks of the readout.
            var shown = new HashSet<string> { readout.Content };
            var until = DateTime.UtcNow + Wait;
            while (shown.Count < 4 && DateTime.UtcNow < until)
            {
                s.Window.TypeText("y");
                s.PumpFrames(2);
                Thread.Sleep(50);
                s.PumpFrame();
                shown.Add(readout.Content);
            }
            Assert.True(shown.Count >= 4, $"the readout showed {string.Join(", ", shown)} while the URL was typed");
            Assert.Same(readout, s.Window.FindByAutomationId("send-elapsed")!.Element);
            s.Window.Window.ClearFocus();
            s.PumpFrames(2);
            s.Window.PressKey(KeyCode.Escape);
            s.PumpUntil(() => fx.App.ActiveTab!.Sending == null, Wait);
            s.PumpFrames(3);
            Assert.Null(s.Window.FindByAutomationId("send-elapsed"));
            AssertPartsAreAFullBuild(fx);
        });
    }

    /// <summary>A popover — the method picker, the environment switcher, an assertion's operator —
    /// opens and closes with the window's view alone, and a pick builds the window once: what the
    /// pick changes, and the parts only where they show it (the tree's unsaved dot, the title
    /// bar's environment). An open and a pick built the window three times and the parts twice
    /// (plan 17 round 6).</summary>
    [Fact]
    public void APopoverOpensAndClosesWithTheView_AndAPickBuildsOnce()
    {
        Hosted(fx =>
        {
            var s = fx.Session;
            void Step(string name, Action act, bool parts)
            {
                int builds = fx.App.Builds, partBuilds = fx.App.PartBuilds;
                act();
                Assert.True(fx.App.Builds - builds == 1, $"{name} built the window {fx.App.Builds - builds} times");
                if (parts) Assert.True(fx.App.PartBuilds > partBuilds, $"{name} built no part");
                else Assert.True(fx.App.PartBuilds == partBuilds, $"{name} built {fx.App.PartBuilds - partBuilds} parts");
                AssertPartsAreAFullBuild(fx, "after " + name);
            }

            Click(fx, "req-get-ok");
            Step("the method picker opening", () => Click(fx, "method-chip"), parts: false);
            Step("a method picked on a saved request", () => Click(fx, "popover-PUT"), parts: true);
            Assert.Equal("PUT", fx.App.ActiveTab!.Doc.Method);
            Assert.True(HasDot(TreeRow(fx, "req-get-ok")));
            Step("the method picker opening again", () => Click(fx, "method-chip"), parts: false);
            Step("a method picked on an unsaved request", () => Click(fx, "popover-POST"), parts: false);
            Assert.Equal("POST", fx.App.ActiveTab!.Doc.Method);
            Assert.Equal("Method: POST", s.Window.FindByAutomationId("method-chip")!.Element.AccessibleName);
            Step("the method picker opening once more", () => Click(fx, "method-chip"), parts: false);
            Step("a click beside it", () => { s.Window.Click(700, 860); s.PumpFrames(3); }, parts: false);
            Assert.Null(s.Window.FindByAutomationId("popover-GET"));
            Assert.Equal("POST", fx.App.ActiveTab!.Doc.Method);

            Step("the environment switcher opening", () => { s.Window.PressKey(KeyCode.E, KeyModifiers.Ctrl); s.PumpFrames(3); }, parts: false);
            Step("an environment picked", () => Click(fx, "popover-staging"), parts: true);
            Assert.Equal("Environment: staging", Part(fx, "titlebar").Descendant("env-name").AccessibleName);

            Click(fx, "subtab-assertions");
            Click(fx, "starter-status");
            Step("an assertion's operator popover opening", () => Click(fx, "assert-op-0"), parts: false);
            Step("an operator picked", () => Click(fx, "popover-contains"), parts: false);
            Assert.Equal("status contains 200", fx.App.ActiveTab!.Doc.Asserts.Single().Args);
        });
    }

    /// <summary>The environment: the title bar's chip follows a switch.</summary>
    [Fact]
    public void TheEnvironmentChip_FollowsASwitch()
    {
        Hosted(fx =>
        {
            var s = fx.Session;
            Assert.Equal("Environment: local", Part(fx, "titlebar").Descendant("env-name").AccessibleName);
            s.Window.PressKey(KeyCode.E, KeyModifiers.Ctrl);
            s.PumpFrames(3);
            Click(fx, "popover-staging");
            Assert.Equal("Environment: staging", Part(fx, "titlebar").Descendant("env-name").AccessibleName);
            Assert.True(HasText(Part(fx, "titlebar"), "env: staging"));
            Switch(fx, KeyCode.Digit4);
            s.Window.PressKey(KeyCode.Down);
            s.PumpFrames(3);
            Assert.True(HasText(Part(fx, "titlebar"), "env: staging"));
            AssertPartsAreAFullBuild(fx);
        });
    }

    /// <summary>The rail lights the view on screen; it reads the View signal itself, so a view
    /// switch needs no MarkDirty.</summary>
    [Fact]
    public void TheRail_LightsTheView()
    {
        Hosted(fx =>
        {
            Assert.True(Lit(Part(fx, "rail").Descendant("rail-requests")));
            Switch(fx, KeyCode.Digit4);
            Assert.True(Lit(Part(fx, "rail").Descendant("rail-history")));
            Assert.False(Lit(Part(fx, "rail").Descendant("rail-requests")));
            fx.Session.Window.PressKey(KeyCode.Down);
            fx.Session.PumpFrames(3);
            Assert.True(Lit(Part(fx, "rail").Descendant("rail-history")));
            AssertPartsAreAFullBuild(fx);
            Switch(fx, KeyCode.Digit1);
            Assert.True(Lit(Part(fx, "rail").Descendant("rail-requests")));
            AssertPartsAreAFullBuild(fx);
        });
    }

    /// <summary>A request added on disk reaches the tree through the watcher's rescan.</summary>
    [Fact]
    public void ARequestAddedOnDisk_ReachesTheTree()
    {
        Hosted(fx =>
        {
            Assert.Null(Part(fx, "tree").Descendant("req-late", required: false));
            Write("orders/late.req", $"GET {_server.Origin}/ok\nname Late\n");
            fx.Session.PumpUntil(() => Part(fx, "tree").Descendant("req-late", required: false) is not null, Wait);
            AssertPartsAreAFullBuild(fx);
        });
    }

    /// <summary>A display scale change retires the icon rasters cut for the old scale, so the
    /// parts are built again at the new one.</summary>
    [Fact]
    public void ADisplayScaleChange_BuildsThePartsAgain()
    {
        Hosted(fx =>
        {
            try
            {
                Click(fx, "req-two");
                int parts = fx.App.PartBuilds;
                fx.Session.Window.SetScale(1.5f);
                fx.Session.PumpFrames(3);
                Assert.True(fx.App.PartBuilds >= parts + 4, $"{fx.App.PartBuilds - parts} part builds after the scale change");
                AssertPartsAreAFullBuild(fx);
            }
            finally { Ui.DisplayScale = 1f; }
        });
    }

    /// <summary>A part's host takes its root's place in the layout: the title bar 40 high across
    /// the window, the rail 48 and the tree 264 wide down the body, the response pane 560 wide at
    /// the right or 380 high at the bottom — and, in a window too short for both, the pane gives
    /// way down to its floor while the editor keeps its own. Each root fills its host.</summary>
    [Theory]
    [InlineData(1440, 900)]
    [InlineData(1000, 600)]
    public void ThePartsLieWhereTheirRootsDid(int width, int height)
    {
        Hosted(fx =>
        {
            var s = fx.Session;
            Click(fx, "req-two");
            s.Window.SetClientSize(width, height);
            s.PumpFrames(4);
            float w = width, h = height, body = h - 40f;
            AssertBox(fx, "titlebar", 0, 0, w, 40);
            AssertBox(fx, "rail", 0, 40, Azimuth.RailWidth, body);
            AssertBox(fx, "tree", Azimuth.RailWidth, 40, Azimuth.TreeWidth, body);
            if (width >= MeridianApp.ForceBottomWindowWidth)
            {
                AssertBox(fx, "response-right", w - Azimuth.ResponseRight, 40, Azimuth.ResponseRight, body);
                s.Window.PressKey(KeyCode.Backslash, KeyModifiers.Ctrl);
                s.PumpFrames(4);
            }
            float pane = Math.Max(MeridianApp.BottomPaneMinHeight, Math.Min(Azimuth.ResponseBottom, body - MeridianApp.EditorMinHeight));
            float left = Azimuth.RailWidth + Azimuth.TreeWidth;
            AssertBox(fx, "response-bottom", left, h - pane, w - left, pane);
        });
    }

    // ── The oracle ───────────────────────────────────────────────────────

    private static readonly string[] PartKeys = ["titlebar", "rail", "tree", "response-right", "response-bottom"];

    /// <summary>The parts on screen equal the parts a forced build of every one of them makes.</summary>
    private static void AssertPartsAreAFullBuild(Fixture fx, string? when = null)
    {
        var kept = PartSigs(fx);
        int parts = fx.App.PartBuilds, onScreen = PartKeys.Count(k => TryPart(fx, k) is not null);
        FullRebuild(fx.App);
        fx.Session.PumpFrames(2);
        Assert.True(fx.App.PartBuilds >= parts + onScreen, $"the forced rebuild built {fx.App.PartBuilds - parts} of {onScreen} parts");
        var full = PartSigs(fx);
        for (int i = 0; i < PartKeys.Length; i++)
        {
            if (kept[i] == full[i]) continue;
            var xl = kept[i].Split('\n'); var yl = full[i].Split('\n');
            for (int l = 0; l < Math.Max(xl.Length, yl.Length); l++)
                if (l >= xl.Length || l >= yl.Length || xl[l] != yl[l])
                    throw new Xunit.Sdk.XunitException(
                        $"the kept {PartKeys[i]} is not what a full build makes{(when is null ? "" : " (" + when + ")")}, line {l}:\n"
                        + $"  kept: {(l < xl.Length ? xl[l] : "<none>")}\n  full: {(l < yl.Length ? yl[l] : "<none>")}");
        }
    }

    private static string[] PartSigs(Fixture fx) =>
        PartKeys.Select(k => TryPart(fx, k) is { } part ? Sig(part) : "<none>").ToArray();

    /// <summary>What a subtree shows: element kinds, keys, text, field and body text, the body's
    /// spans, colours, tooltips, names, opacity and enabled state, in tree order.</summary>
    private static string Sig(Element e)
    {
        var sb = new StringBuilder();
        void Walk(Element el, int depth)
        {
            sb.Append(' ', depth).Append(el.GetType().Name);
            if (el is not ComponentHost && el.Key is { } k) sb.Append(" key=").Append(k);
            if (el is TextElement t) sb.Append(" text=").Append(t.Content);
            if (el is TextInputElement input) sb.Append(" input=").Append(input.Text);
            if (el is TextAreaElement area)
            {
                var spans = new HashCode();
                foreach (var span in area.Spans ?? []) spans.Add(span);
                sb.Append(" area=").Append(area.Text.Length).Append('/').Append(area.Text.GetHashCode())
                    .Append(" spans=").Append(area.Spans?.Count ?? -1).Append('/').Append(spans.ToHashCode());
            }
            if (el is BoxElement b) sb.Append(" bg=").Append(b.BackgroundColor).Append(" border=").Append(b.BorderColor);
            if (el.TooltipText is { } tip) sb.Append(" tip=").Append(tip);
            if (el.AccessibleName is { } n) sb.Append(" name=").Append(n);
            if (el.AutomationId is { } id) sb.Append(" id=").Append(id);
            if (el.Inherited.TextColor is { } c) sb.Append(" fg=").Append(c);
            if (el is ButtonElement btn) sb.Append(" enabled=").Append(btn.IsEnabled);
            sb.Append(" op=").Append(el.Opacity.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
            sb.Append('\n');
            foreach (var child in el.Children) Walk(child, depth + 1);
        }
        Walk(e, 0);
        return sb.ToString();
    }

    private static void FullRebuild(MeridianApp app) =>
        typeof(MeridianApp).GetMethod("MarkDirty", BindingFlags.NonPublic | BindingFlags.Instance, Type.EmptyTypes)!
            .Invoke(app, null);

    // ── Harness ──────────────────────────────────────────────────────────

    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(20);

    private sealed record Fixture(HeadlessSession Session, MeridianApp App);

    private void Hosted(Action<Fixture> body)
    {
        using var session = HeadlessSession.Create(1440, 900);
        MeridianApp? app = null;
        try
        {
            app = new MeridianApp(session.Application, session.Window.Window);
            session.Application.SetRoot(app.Mount());
            app.OpenWorkspace(_root);
            session.PumpFrames(3);
            body(new Fixture(session, app));
        }
        finally { app?.Dispose(); }
    }

    private static void Click(Fixture fx, string automationId)
    {
        var el = fx.Session.Window.FindByAutomationId(automationId);
        Assert.True(el != null, $"#{automationId} not found");
        el!.Click();
        fx.Session.PumpFrames(3);
    }

    private static void Switch(Fixture fx, KeyCode view)
    {
        fx.Session.Window.Window.ClearFocus();
        fx.Session.PumpFrames(2);
        fx.Session.Window.PressKey(view);
        fx.Session.PumpFrames(4);
    }

    private static void AssertBox(Fixture fx, string key, float x, float y, float width, float height)
    {
        var part = Part(fx, key);
        var root = part.Layout.AbsoluteBorderBox;
        var host = part.Parent!.Layout.AbsoluteBorderBox;
        Assert.Equal((x, y, width, height), (root.X, root.Y, root.Width, root.Height));
        Assert.Equal((root.X, root.Y, root.Width, root.Height), (host.X, host.Y, host.Width, host.Height));
    }

    private static Element Root(HeadlessSession session) =>
        (Element)typeof(HultUI.Window).GetField("_root", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(session.Window.Window)!;

    private static Element? TryPart(Fixture fx, string key) =>
        Descendants(Root(fx.Session)).FirstOrDefault(e => e.Key == key && e.Parent is ComponentHost);

    private static Element Part(Fixture fx, string key) =>
        TryPart(fx, key) ?? throw new Xunit.Sdk.XunitException($"no part \"{key}\" on screen");

    private static BoxElement TreeRow(Fixture fx, string automationId) =>
        (BoxElement)Part(fx, "tree").Descendant(automationId);

    /// <summary>The tree's unsaved dot: the row's last child, a filled accent box with nothing in it.</summary>
    private static bool HasDot(BoxElement row) =>
        row.Children[^1] is BoxElement { Children.Count: 0 } dot && dot.BackgroundColor == Azimuth.Accent;

    private static bool Lit(Element e) => e is BoxElement b && b.BorderColor == Azimuth.Accent;

    private static bool HasText(Element root, string text) =>
        Descendants(root).Any(e => e is TextElement t && t.Content == text);

    private static bool HasTextStarting(Element root, string text) =>
        Descendants(root).Any(e => e is TextElement t && t.Content.StartsWith(text, StringComparison.Ordinal));

    internal static IEnumerable<Element> Descendants(Element element)
    {
        yield return element;
        foreach (var child in element.Children)
            foreach (var d in Descendants(child))
                yield return d;
    }
}

internal static class PartTestElementExtensions
{
    public static Element Descendant(this Element root, string automationId) =>
        root.Descendant(automationId, required: true)!;

    public static Element? Descendant(this Element root, string automationId, bool required)
    {
        var found = PartTests.Descendants(root).FirstOrDefault(e => e.AutomationId == automationId);
        if (found is null && required) throw new Xunit.Sdk.XunitException($"no #{automationId} under {root.Key}");
        return found;
    }
}
