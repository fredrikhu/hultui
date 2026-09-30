using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using HultUI.Headless;
using HultUI.WebView.Testing;
using Xunit;

namespace HultUI.WebView.Tests;

/// <summary>
/// The page's own menus, dialogs and downloads: what the application sees of them, what the
/// element draws for them with HultUI, and that every one of them is answered — an engine
/// waits for each answer, and a page's script is stopped until its dialog has one.
/// </summary>
public class WebViewPageUiTests
{
    private static readonly Uri A = new("https://a.test/page");

    private sealed class Page : IDisposable
    {
        public required HeadlessSession Session { get; init; }
        public required FakeWebEngine Engine { get; init; }
        public required WebViewController Web { get; init; }
        public required WebViewElement Element { get; init; }
        public FakeWebEngineView View => Engine.Views[0];
        public HeadlessWindow Window => Session.Window;

        /// <summary>Every name in the tree; a menu entry's shortcut, which its name carries
        /// after a comma ("Cut, Ctrl+X"), is left off.</summary>
        public List<string> Names() => Walk(Window.AccessibilityTree).Select(n => Label(n.Name)).ToList();

        public AccessibilityNode Node(string name) => Walk(Window.AccessibilityTree).First(n => Label(n.Name) == name);

        private static string Label(string? name) => name is null ? "" : name.Split(", ")[0];

        public void ClickNode(string name)
        {
            var r = Node(name).BoundingRect;
            Window.Click(r.X + r.Width / 2f, r.Y + r.Height / 2f);
            Session.PumpFrames(2);
        }

        public void Dispose()
        {
            Web.Dispose();
            Session.Dispose();
        }
    }

    /// <summary>A 40-unit toolbar over the page, which fills the rest of a 400×300 window.</summary>
    private static Page Show(float scale = 1f, WebViewOptions? options = null, bool showElement = true)
    {
        var session = HeadlessSession.Create(400, 300, scale);
        var engine = new FakeWebEngine();
        engine.Pages[A] = new FakePage("A page", Color.White);
        var web = new WebViewController(engine, options);
        var element = WebViewElement.New(web).FlexGrow(1);
        session.Application.SetRoot(Flex.Column().Children(
            Box.New().Height(40f).FlexShrink(0),
            showElement ? element : Box.New().FlexGrow(1)));
        session.PumpFrames(2);
        web.Navigate(A);
        session.PumpFrame();
        return new Page { Session = session, Engine = engine, Web = web, Element = element };
    }

    private static IEnumerable<AccessibilityNode> Walk(AccessibilityNode? node)
    {
        if (node is null) yield break;
        yield return node;
        foreach (var child in node.Children)
            foreach (var d in Walk(child))
                yield return d;
    }

    // ──── Context menu ────

    [Fact]
    public void ARightClickInThePage_OpensAHultUIMenu_WithTheEditingEntriesAndNoneOfTheBrowsers()
    {
        using var page = Show();

        page.View.RaiseContextMenu(20, 20);
        page.Session.PumpFrame();

        var names = page.Names();
        Assert.Contains("Cut", names);
        Assert.Contains("Copy", names);
        Assert.Contains("Paste", names);
        foreach (var browsers in new[] { "Back", "Reload", "Save as", "Print", "Inspect" })
            Assert.DoesNotContain(browsers, names);
    }

    [Fact]
    public void TheStandardMenu_KeepsInspect_WhenDevToolsAreAllowed_AndNeverStartsOrEndsWithALine()
    {
        var request = new WebContextMenuRequest(0, 0, new WebContextMenuTarget(), FakeWebEngineView.StandardEngineMenu, _ => { }, devTools: true);

        Assert.Equal(["cut", "copy", "paste", "separator", "inspectElement"], request.Items.Select(i => i.Name));
        Assert.Equal(11, request.EngineItems.Count); // everything the engine offered is still there to pick from
    }

    [Fact]
    public void TheMenuOpens_WhereThePageAskedForIt()
    {
        // Device pixels from the page's top-left, at scale 2: (100, 60) is 50 units in and 30
        // down from the page, which starts 40 units below the window's top.
        using var page = Show(scale: 2f);

        page.View.RaiseContextMenu(100, 60);
        page.Session.PumpFrame();

        var cut = page.Node("Cut").BoundingRect;
        Assert.InRange(cut.X, 50f, 60f);
        Assert.InRange(cut.Y, 70f, 82f);
    }

    [Fact]
    public void ChoosingAnEntry_TellsTheEngineWhich_AndClickingAway_TellsItNone()
    {
        using var page = Show();

        page.View.RaiseContextMenu(20, 20);
        page.Session.PumpFrame();
        page.ClickNode("Copy");
        Assert.Equal("copy", Assert.Single(page.View.ContextMenuAnswers)!.Name);

        page.View.RaiseContextMenu(20, 20);
        page.Session.PumpFrame();
        page.Window.Click(390, 290); // away from the menu
        page.Session.PumpFrames(2);
        Assert.Equal(2, page.View.ContextMenuAnswers.Count);
        Assert.Null(page.View.ContextMenuAnswers[1]);
        Assert.DoesNotContain("Copy", page.Names());
    }

    [Fact]
    public void Escape_ClosesTheMenu_AndTheKeyboardGoesBackToThePage()
    {
        using var page = Show();
        page.Element.Focus();
        page.Session.PumpFrame();
        Assert.True(page.View.HasFocus);

        page.View.RaiseContextMenu(20, 20);
        page.Session.PumpFrame();
        // The menu's arrows, Enter and Escape are HultUI's while it is open.
        Assert.False(page.View.HasFocus);

        page.Window.PressKey(KeyCode.Escape);
        page.Session.PumpFrames(2);

        Assert.Null(Assert.Single(page.View.ContextMenuAnswers));
        Assert.True(page.View.HasFocus, "the page did not get the keyboard back");
        Assert.True(page.Element.IsFocused);
    }

    [Fact]
    public void TheArrowKeysAndEnter_ChooseFromTheMenu()
    {
        using var page = Show();
        page.Element.Focus();
        page.Session.PumpFrame();

        page.View.RaiseContextMenu(20, 20);
        page.Session.PumpFrame();
        page.Window.PressKey(KeyCode.Down);
        page.Window.PressKey(KeyCode.Down);
        page.Window.PressKey(KeyCode.Enter);
        page.Session.PumpFrames(2);

        Assert.Equal("copy", Assert.Single(page.View.ContextMenuAnswers)!.Name);
    }

    [Fact]
    public void TheApp_CanAddItsOwnEntries_WhichRunItsCode_AndTellTheEngineNothing()
    {
        using var page = Show();
        Uri? opened = null;
        page.Web.ContextMenuRequested += r =>
        {
            r.Items.Clear();
            if (r.Target.LinkUri is { } link)
                r.Items.Add(new WebContextMenuItem("Open in browser", () => opened = link));
        };

        page.View.RaiseContextMenu(20, 20, target: new WebContextMenuTarget { LinkUri = new Uri("https://b.test/") });
        page.Session.PumpFrame();
        page.ClickNode("Open in browser");

        Assert.Equal(new Uri("https://b.test/"), opened);
        Assert.Null(Assert.Single(page.View.ContextMenuAnswers));
    }

    [Fact]
    public void AnAppThatTakesTheMenuOver_GetsNothingDrawn_AndAnswersWhenItLikes()
    {
        using var page = Show();
        WebContextMenuRequest? taken = null;
        page.Web.ContextMenuRequested += r => { r.Handled = true; taken = r; };

        page.View.RaiseContextMenu(20, 20);
        page.Session.PumpFrame();
        Assert.DoesNotContain("Copy", page.Names());
        Assert.Empty(page.View.ContextMenuAnswers);

        taken!.Select(taken.Items.First(i => i.Name == "paste"));
        Assert.Equal("paste", Assert.Single(page.View.ContextMenuAnswers)!.Name);
    }

    [Fact]
    public void AMenuWithNothingLeftInIt_IsAnsweredAtOnce()
    {
        using var page = Show();
        page.Web.ContextMenuRequested += r => r.Items.Clear();
        page.View.RaiseContextMenu(20, 20);
        Assert.Null(Assert.Single(page.View.ContextMenuAnswers));
    }

    [Fact]
    public void AMenuWithNoElementToShowIt_IsAnsweredAtOnce()
    {
        using var hidden = Show(showElement: false);
        hidden.View.RaiseContextMenu(20, 20);
        Assert.Null(Assert.Single(hidden.View.ContextMenuAnswers));
    }

    [Fact]
    public void AThrowingHandler_StillLeavesTheMenuAnswered()
    {
        using var page = Show(showElement: false);
        page.Web.ContextMenuRequested += _ => throw new InvalidOperationException("app bug");

        Assert.Throws<InvalidOperationException>(() => page.View.RaiseContextMenu(20, 20));

        Assert.Null(Assert.Single(page.View.ContextMenuAnswers));
    }

    // ──── The page's dialogs ────

    [Fact]
    public void AnAlert_IsAHultUIDialog_ThatSaysWhoseItIs()
    {
        using var page = Show();

        page.View.RaiseScriptDialog(WebScriptDialogKind.Alert, "Saved.");
        page.Session.PumpFrames(2);

        Assert.Contains("a.test says", page.Names());
        Assert.Contains("Saved.", page.Names());
        Assert.DoesNotContain("Cancel", page.Names());
        Assert.Empty(page.View.ScriptDialogAnswers); // the page waits

        page.ClickNode("OK");
        Assert.Equal((true, (string?)null), Assert.Single(page.View.ScriptDialogAnswers));
        Assert.DoesNotContain("Saved.", page.Names());
    }

    [Fact]
    public void AConfirm_AnswersWithTheButtonChosen()
    {
        using var page = Show();

        page.View.RaiseScriptDialog(WebScriptDialogKind.Confirm, "Delete it?");
        page.Session.PumpFrames(2);
        page.ClickNode("Cancel");

        Assert.Equal((false, (string?)null), Assert.Single(page.View.ScriptDialogAnswers));
    }

    [Fact]
    public void APrompt_StartsWithThePagesText_AndAnswersWithWhatWasTyped()
    {
        using var page = Show();

        page.View.RaiseScriptDialog(WebScriptDialogKind.Prompt, "Your name?", "Ann");
        page.Session.PumpFrames(2);
        // The text field has the keyboard as soon as the dialog shows.
        page.Window.PressKey(KeyCode.End);
        page.Window.TypeText("ika");
        page.Window.PressKey(KeyCode.Enter);
        page.Session.PumpFrames(2);

        Assert.Equal((true, "Annika"), Assert.Single(page.View.ScriptDialogAnswers));
    }

    [Fact]
    public void Escape_Cancels_AndTheKeyboardGoesBackToThePage()
    {
        using var page = Show();
        page.Element.Focus();
        page.Session.PumpFrame();

        page.View.RaiseScriptDialog(WebScriptDialogKind.Confirm, "Delete it?");
        page.Session.PumpFrames(2);
        Assert.False(page.View.HasFocus);
        page.Window.PressKey(KeyCode.Escape);
        page.Session.PumpFrames(2);

        Assert.Equal((false, (string?)null), Assert.Single(page.View.ScriptDialogAnswers));
        Assert.True(page.View.HasFocus, "the page did not get the keyboard back");
    }

    [Fact]
    public void LeavingAPage_AsksWithLeaveAndStay()
    {
        using var page = Show();

        page.View.RaiseScriptDialog(WebScriptDialogKind.BeforeUnload, "");
        page.Session.PumpFrames(2);

        Assert.Contains("Leave this page?", page.Names());
        page.ClickNode("Stay");
        Assert.Equal((false, (string?)null), Assert.Single(page.View.ScriptDialogAnswers));
    }

    [Fact]
    public void TheApp_CanAnswerADialogItself_AndThenNothingIsShown()
    {
        using var page = Show();
        page.Web.ScriptDialogRequested += d => d.Accept("from the app");

        page.View.RaiseScriptDialog(WebScriptDialogKind.Prompt, "Your name?");
        page.Session.PumpFrames(2);

        Assert.Equal((true, "from the app"), Assert.Single(page.View.ScriptDialogAnswers));
        Assert.DoesNotContain("Your name?", page.Names());
    }

    [Fact]
    public void ADialogWithNoElementToShowIt_IsCancelled_SoThePageDoesNotHang()
    {
        using var page = Show(showElement: false);

        page.View.RaiseScriptDialog(WebScriptDialogKind.Confirm, "Delete it?");

        Assert.Equal((false, (string?)null), Assert.Single(page.View.ScriptDialogAnswers));
    }

    // ──── Downloads ────

    [Fact]
    public void ADownloadNobodyAnswers_IsCancelled()
    {
        using var page = Show();

        var request = page.View.RaiseDownload(new Uri("https://a.test/report.pdf"), "report.pdf");

        Assert.Null(Assert.Single(page.View.DownloadAnswers));
        Assert.Equal(WebDownloadState.Cancelled, request.Download.State);
    }

    [Fact]
    public void ADownloadSavedByTheApp_ReportsItsProgress()
    {
        using var page = Show();
        var path = Path.Combine(Path.GetTempPath(), "report.pdf");
        var seen = new List<(WebDownloadState, long)>();
        page.Web.DownloadRequested += d => d.SaveTo(path).Changed += x => seen.Add((x.State, x.BytesReceived));

        var request = page.View.RaiseDownload(new Uri("https://a.test/report.pdf"), "report.pdf", totalBytes: 100);
        request.Download.Report(40, 100);
        request.Download.Report(100, 100);
        request.Download.Finish(WebDownloadState.Completed);

        Assert.Equal(path, Assert.Single(page.View.DownloadAnswers));
        Assert.Equal([(WebDownloadState.InProgress, 40L), (WebDownloadState.InProgress, 100L), (WebDownloadState.Completed, 100L)], seen);
        Assert.Equal(path, request.Download.Path);
    }

    [Fact]
    public void ADeferredDownload_WaitsForTheAnswer()
    {
        using var page = Show();
        WebDownloadRequest? waiting = null;
        page.Web.DownloadRequested += d => { d.Defer(); waiting = d; };

        page.View.RaiseDownload(new Uri("https://a.test/report.pdf"), "report.pdf");
        Assert.Empty(page.View.DownloadAnswers);

        waiting!.SaveTo(Path.Combine(Path.GetTempPath(), "chosen.pdf"));
        Assert.EndsWith("chosen.pdf", Assert.Single(page.View.DownloadAnswers));
        waiting.Cancel(); // too late: does nothing
        Assert.Single(page.View.DownloadAnswers);
    }

    [Fact]
    public void CancellingARunningDownload_StopsItInTheEngine()
    {
        using var page = Show();
        page.Web.DownloadRequested += d => d.SaveTo(Path.Combine(Path.GetTempPath(), "big.iso"));

        var request = page.View.RaiseDownload(new Uri("https://a.test/big.iso"), "big.iso");
        request.Download.Cancel();

        Assert.Equal(WebDownloadState.Cancelled, request.Download.State);
    }
}
