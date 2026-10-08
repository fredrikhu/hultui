using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using HultUI.Headless;
using Xunit;

namespace Meridian.Tests;

/// <summary>
/// The editor beside the response pane at the narrowest window that keeps the pane on the
/// right (1320 px since 2026-10-08, leaving the editor 448 of them; 1100 and 228 before).
/// Its tab titles and sub-tab labels used to wrap — the open tabs' titles several lines
/// deep, over the URL bar — and their words held the editor wide enough to push the
/// response pane off the window. Below the breakpoint the pane sits under the editor.
/// </summary>
public class NarrowEditorLayoutTests : IDisposable
{
    /// <summary>The narrowest window with the response pane beside the editor: rail 48 +
    /// tree 264 + editor 448 + pane 560.</summary>
    private const int SideBySide = 1320;

    private const string SignIn = "Sign in with client credentials and remember the access token";
    private const string Search = "Search succeeded charges over 10 SEK for the customer";

    private readonly string _root = Directory.CreateTempSubdirectory("meridian-narrow-").FullName;

    public NarrowEditorLayoutTests()
    {
        Write("auth/login.req",
            $"POST {{{{base_url}}}}/v1/oauth/token/client-credentials\nname {SignIn}\nheader Content-Type: application/json\n"
            + "extract body $.access_token -> token\n");
        Write("charges/search.req",
            "GET {{base_url}}/v1/charges/search/{{customer_id}}?query=status:'succeeded' AND amount>1000&limit=50\n"
            + $"name {Search}\nheader Authorization: Bearer {{{{token}}}}\nassert status equals 200\n");
        Write("charges/get-charge.req",
            "GET {{base_url}}/v1/charges/{{charge_id}}\nname Get one charge with its refunds, customer and invoice subscription expanded\n");
        Write("charges/list-refunds.req",
            "GET {{base_url}}/v1/charges/{{charge_id}}/refunds?limit=100\nname List every refund of the charge, a hundred at a time, newest first\n");
        Write("customers/update-customer.req",
            "PATCH {{base_url}}/v1/customers/{{customer_id}}\nname Update the customer's billing address and preferred invoice language\n");
        Write("envs/local.env", "base_url=http://localhost:8080\n");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void Write(string relative, string content)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private void Hosted(int width, int height, Action<HeadlessSession> body) =>
        Hosted(width, height, (session, _) => body(session));

    private void Hosted(int width, int height, Action<HeadlessSession, MeridianApp> body)
    {
        using var session = HeadlessSession.Create(width, height);
        MeridianApp? app = null;
        try
        {
            app = new MeridianApp(session.Application, session.Window.Window);
            session.Application.SetRoot(app.Mount());
            app.OpenWorkspace(_root);
            session.PumpFrame();
            body(session, app);
        }
        finally
        {
            app?.Dispose();
        }
    }

    private static HeadlessElement Get(HeadlessSession session, string automationId) =>
        session.Window.FindByAutomationId(automationId)
        ?? throw new Xunit.Sdk.XunitException($"#{automationId} not found");

    private static void Click(HeadlessSession session, string automationId)
    {
        Get(session, automationId).Click();
        session.PumpFrames(2);
    }

    private static TextElement Label(Element e) =>
        e as TextElement ?? e.Children.Select(Label).First(t => t is not null);

    private static IEnumerable<TextElement> Labels(Element e)
    {
        if (e is TextElement t) yield return t;
        foreach (var c in e.Children)
            foreach (var l in Labels(c))
                yield return l;
    }

    private static Element Root(Element e)
    {
        while (e.Parent is { } parent) e = parent;
        return e;
    }

    /// <summary>Where an element can draw: its window box, cut by every ancestor that clips.</summary>
    private static Rect Visible(Element e)
    {
        Rect r = e.WindowBorderBox;
        for (var a = e.Parent; a is not null; a = a.Parent)
        {
            if (a.Box.OverflowX == HultUI.Core.Layout.Overflow.Visible) continue;
            Rect c = a.WindowBorderBox;
            float x0 = Math.Max(r.X, c.X), x1 = Math.Min(r.X + r.Width, c.X + c.Width);
            r = new Rect(x0, r.Y, Math.Max(0f, x1 - x0), r.Height);
        }
        return r;
    }

    /// <summary>One line of a label: its box is shorter than two lines' worth of its font.</summary>
    private static void AssertOneLine(TextElement label)
    {
        float lines = label.Layout.Height / (label.ResolvedInherited.FontSize * 1.3f);
        Assert.True(lines < 1.5f,
            $"\"{label.Content}\" takes {lines:0.0} lines ({label.Layout.Width:0.#} x {label.Layout.Height:0.#})");
    }

    private static readonly string[] SubTabs = ["params", "headers", "body", "auth", "assertions", "chain"];

    [Fact]
    public void Tab_titles_and_sub_tabs_stay_on_one_line_and_the_response_pane_stays_in_the_window()
    {
        Hosted(SideBySide, 760, session =>
        {
            Click(session, "req-login");
            Click(session, "req-search");
            session.PumpFrames(2);

            Rect url = Get(session, "url-display").Element.WindowBorderBox;
            foreach (var id in new[] { "request-tab-0", "request-tab-1" })
            {
                var tab = Get(session, id).Element;
                var title = Labels(tab).Single(l => l.Content is SignIn or Search);
                AssertOneLine(title);
                Rect box = title.WindowBorderBox;
                Assert.True(box.Y + box.Height <= url.Y,
                    $"{id}'s title reaches y={box.Y + box.Height:0.#}, into the URL bar at y={url.Y:0.#}");
            }

            foreach (var sub in SubTabs)
                AssertOneLine(Label(Get(session, "subtab-" + sub).Element));
            // What the breakpoint is for: at its narrowest the editor still shows all six
            // sub-tabs of an ordinary request (counts and all) without scrolling them.
            AssertSubTabsAllInView(session);

            AssertEditorLeavesThePaneAlone(session, SideBySide);
        });
    }

    [Fact]
    public void Five_open_tabs_share_the_strip_and_stay_out_of_the_response_pane()
    {
        Hosted(SideBySide, 760, session =>
        {
            // The last one open is the PATCH: "PATCH ▾" and Send are the widest common floor
            // the URL bar has, and the editor holds them with room for the URL.
            foreach (var id in new[] { "req-login", "req-search", "req-get-charge", "req-list-refunds", "req-update-customer" })
                Click(session, id);
            session.PumpFrames(2);

            Rect url = Get(session, "url-display").Element.WindowBorderBox;
            for (int i = 0; i < 5; i++)
                foreach (var label in Labels(Get(session, "request-tab-" + i).Element))
                {
                    AssertOneLine(label);
                    Rect box = label.WindowBorderBox;
                    Assert.True(box.Y + box.Height <= url.Y,
                        $"tab {i}'s \"{label.Content}\" reaches y={box.Y + box.Height:0.#}, into the URL bar at y={url.Y:0.#}");
                }

            AssertEditorLeavesThePaneAlone(session, SideBySide);
            Assert.True(url.Width >= 180f, $"the URL gets {url.Width:0.#}px beside \"PATCH ▾\" and Send");
        });
    }

    /// <summary>
    /// The breakpoint, pinned: the pane sits beside the editor from 1320 px (the editor keeps
    /// 448) and under it below that, re-evaluated as the window is resized across it. It was
    /// 1100 until 2026-10-08, which left the editor 228 px — the method chip and Send with
    /// almost no URL between them.
    /// </summary>
    [Fact]
    public void The_pane_sits_beside_the_editor_from_1320_px_and_under_it_below()
    {
        Hosted(SideBySide, 760, (session, app) =>
        {
            Click(session, "req-search");

            AssertSide(session, app, SideBySide);
            Rect editor = Editor(session, "response-right").WindowBorderBox;
            Assert.True(editor.Width >= 448f - 0.5f, $"the editor beside the pane is {editor.Width:0.#}px, short of 448");

            Resize(session, SideBySide - 1, 760);
            AssertBottom(session, app, SideBySide - 1);

            Resize(session, SideBySide, 760);
            AssertSide(session, app, SideBySide);
        });
    }

    /// <summary>
    /// Ctrl+\ is the user's choice and outlives the breakpoint: flipped to the bottom in a
    /// wide window it stays there through a narrow spell and back; in a narrow window the
    /// flip is refused (the pane cannot go right), so it does not quietly undo the choice.
    /// </summary>
    [Fact]
    public void The_users_flip_outlives_a_pass_through_the_breakpoint()
    {
        Hosted(1400, 760, (session, app) =>
        {
            Click(session, "req-search");
            AssertSide(session, app, 1400);

            Flip(session);
            AssertBottom(session, app, 1400);

            Resize(session, 1200, 760);
            AssertBottom(session, app, 1200);
            Flip(session);   // refused: too narrow for the right
            AssertBottom(session, app, 1200);

            Resize(session, 1400, 760);
            AssertBottom(session, app, 1400);
            Flip(session);
            AssertSide(session, app, 1400);

            // And without a flip, the narrow spell is undone by widening again.
            Resize(session, 1200, 760);
            AssertBottom(session, app, 1200);
            Resize(session, 1400, 760);
            AssertSide(session, app, 1400);
        });
    }

    /// <summary>
    /// The windows the move sends to the bottom layout, 1100–1319 px at 760 tall: the pane
    /// keeps its 380 px, and the editor keeps its whole chrome (tabs, URL bar, sub-tabs,
    /// footer) with room for at least five form rows in the sub-editor between them. Being
    /// full width, it shows all six sub-tabs and a long stretch of URL.
    /// </summary>
    [Theory]
    [InlineData(1100)]
    [InlineData(1200)]
    [InlineData(SideBySide - 1)]
    public void Under_the_breakpoint_the_editor_and_the_pane_share_the_height(int width)
    {
        Hosted(width, 760, (session, app) =>
        {
            Click(session, "req-search");
            AssertBottom(session, app, width);

            Rect pane = Get(session, "response-bottom").Element.WindowBorderBox;
            Assert.True(Math.Abs(pane.Height - 380f) < 0.5f, $"the pane is {pane.Height:0.#}px tall, not 380");
            Assert.True(pane.Y + pane.Height <= 760.5f, $"the pane ends at y={pane.Y + pane.Height:0.#}, past the window");

            Rect subTabs = Get(session, "subtabs").Element.WindowBorderBox;
            var footer = Labels(Editor(session, "response-bottom")).Single(l => l.Content == "charges/search.req");
            Rect foot = footer.WindowBorderBox;
            Assert.True(foot.Y + foot.Height <= pane.Y + 0.5f,
                $"the editor's footer reaches y={foot.Y + foot.Height:0.#}, into the pane at y={pane.Y:0.#}");
            float body = foot.Y - (subTabs.Y + subTabs.Height);
            Assert.True(body >= 5 * 32f, $"the sub-editor gets {body:0.#}px between the sub-tabs and the footer");

            AssertSubTabsAllInView(session);
            Rect url = Get(session, "url-display").Element.WindowBorderBox;
            Assert.True(url.Width >= 500f, $"the URL gets {url.Width:0.#}px in a {width}px window");
        });
    }

    private static void Resize(HeadlessSession session, int width, int height)
    {
        session.Window.SetClientSize(width, height);
        session.PumpFrames(3);
    }

    private static void Flip(HeadlessSession session)
    {
        session.Window.PressKey(HultUI.Core.Events.KeyCode.Backslash, HultUI.Core.Events.KeyModifiers.Ctrl);
        session.PumpFrames(2);
    }

    private static void AssertSide(HeadlessSession session, MeridianApp app, int width)
    {
        Assert.False(app.ResponseAtBottom, $"the pane is at the bottom at {width}px");
        Assert.NotNull(session.Window.FindByAutomationId("response-right"));
        Assert.Null(session.Window.FindByAutomationId("response-bottom"));
        AssertEditorLeavesThePaneAlone(session, width);
    }

    private static void AssertBottom(HeadlessSession session, MeridianApp app, int width)
    {
        Assert.True(app.ResponseAtBottom, $"the pane is beside the editor at {width}px");
        Assert.NotNull(session.Window.FindByAutomationId("response-bottom"));
        Assert.Null(session.Window.FindByAutomationId("response-right"));
    }

    /// <summary>The editor column: the URL bar's ancestor that sits beside the pane.</summary>
    private static Element Editor(HeadlessSession session, string paneId)
    {
        var editor = Get(session, "url-display").Element;
        while (editor.Parent is { } p && !p.Children.Any(c => c.AutomationId == paneId)) editor = p;
        return editor;
    }

    /// <summary>All six sub-tabs show whole: none is cut by the strip, which scrolls sideways
    /// when they do not fit.</summary>
    private static void AssertSubTabsAllInView(HeadlessSession session)
    {
        foreach (var sub in SubTabs)
        {
            var tab = Get(session, "subtab-" + sub).Element;
            Rect whole = tab.WindowBorderBox, shown = Visible(tab);
            Assert.True(Math.Abs(shown.Width - whole.Width) < 0.5f,
                $"the {sub} sub-tab shows {shown.Width:0.#} of its {whole.Width:0.#}px: the strip is too narrow for the six");
        }
    }

    /// <summary>
    /// The editor gives the pane the room the design leaves it: the pane, and the button that
    /// moves it, are inside the window, and nothing of the editor's draws over the pane — what
    /// does not fit is cut off (the tabs, the URL's chips) or scrolled (the sub-tabs) at the
    /// editor's edge.
    /// </summary>
    private static void AssertEditorLeavesThePaneAlone(HeadlessSession session, int width)
    {
        Rect pane = Get(session, "response-right").Element.WindowBorderBox;
        Assert.True(pane.X + pane.Width <= width + 0.5f, $"the response pane ends at x={pane.X + pane.Width:0.#}, past {width}");
        Rect flip = Get(session, "flip-pane").Element.WindowBorderBox;
        Assert.True(flip.X + flip.Width <= width + 0.5f, $"the flip button ends at x={flip.X + flip.Width:0.#}, past {width}");

        var editor = Get(session, "url-display").Element;
        while (editor.Parent is { } p && !p.Children.Any(c => c.AutomationId == "response-right")) editor = p;
        foreach (var label in Labels(editor))
        {
            Rect v = Visible(label);
            Assert.True(v.Width <= 0.5f || v.X + v.Width <= pane.X + 0.5f,
                $"\"{label.Content}\" draws to x={v.X + v.Width:0.#}, over the response pane at x={pane.X:0.#}");
        }
    }

    [Fact]
    public void Chain_rows_stay_one_line_high()
    {
        Hosted(1400, 760, session =>
        {
            Click(session, "req-login");
            Click(session, "req-search");
            Click(session, "subtab-chain");

            // The search request uses {{token}}, which sign-in produces but has not this
            // session: the row names the producer and the verdict, the longest notes there are.
            var notes = Labels(Root(Get(session, "url-display").Element))
                .Where(l => l.Content.StartsWith("← ", StringComparison.Ordinal) || l.Content.StartsWith("! ", StringComparison.Ordinal))
                .ToList();
            Assert.NotEmpty(notes);
            foreach (var note in notes) AssertOneLine(note);
        });
    }
}
