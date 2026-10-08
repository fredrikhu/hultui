using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using HultUI.Headless;
using Xunit;

namespace Meridian.Tests;

/// <summary>
/// The editor beside the response pane at the narrowest window that keeps the pane on the
/// right (1100 px leaves the editor 228 of them). Its tab titles and sub-tab labels used to
/// wrap — the open tabs' titles several lines deep, over the URL bar — and their words held
/// the editor wide enough to push the response pane off the window.
/// </summary>
public class NarrowEditorLayoutTests : IDisposable
{
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

    private void Hosted(int width, int height, Action<HeadlessSession> body)
    {
        using var session = HeadlessSession.Create(width, height);
        MeridianApp? app = null;
        try
        {
            app = new MeridianApp(session.Application, session.Window.Window);
            session.Application.SetRoot(app.Mount());
            app.OpenWorkspace(_root);
            session.PumpFrame();
            body(session);
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

    [Fact]
    public void Tab_titles_and_sub_tabs_stay_on_one_line_and_the_response_pane_stays_in_the_window()
    {
        Hosted(1100, 760, session =>
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

            foreach (var sub in new[] { "params", "headers", "body", "auth", "assertions", "chain" })
                AssertOneLine(Label(Get(session, "subtab-" + sub).Element));

            AssertEditorLeavesThePaneAlone(session, 1100);
        });
    }

    [Fact]
    public void Five_open_tabs_share_the_strip_and_stay_out_of_the_response_pane()
    {
        Hosted(1100, 760, session =>
        {
            foreach (var id in new[] { "req-login", "req-search", "req-get-charge", "req-list-refunds", "req-update-customer" })
                Click(session, id);
            // Back to a GET: the method chip and Send are the URL bar's floor, and "PATCH ▾"
            // alone takes that 5px past the 228 the pane leaves the editor at 1100.
            Click(session, "req-get-charge");
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

            AssertEditorLeavesThePaneAlone(session, 1100);
        });
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
