using HultUI.Core.Accessibility;
using HultUI.Core.Animation;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using Xunit;
using static HultUI.Headless.HeadlessExpect;

namespace HultUI.Headless.Tests;

using Text = HultUI.Core.Builders.Text;

/// <summary>Playwright-style locators and assertions over the accessibility tree, on virtual time.</summary>
public class LocatorTests
{
    private static HeadlessSession Mount(params Element[] children)
    {
        var session = HeadlessSession.Create(500, 400);
        session.Window.SetRoot(Flex.Column(gap: 8).Padding(10).Children(children));
        session.PumpFrame();
        return session;
    }

    private static Element Row(string label, Action onDelete) =>
        Flex.Row(gap: 8).AccessibleRole(AccessibilityRole.Group)
            .Children(Text.New(label), Button.New("Delete").OnClick(_ => onDelete()));

    [Fact]
    public void GetByRole_MatchesNameBySubstring_OrExactly_AndClicks()
    {
        int clicks = 0;
        using var session = Mount(Button.New("Save draft").OnClick(_ => clicks++));

        session.GetByRole(AccessibilityRole.Button, "save").Click();
        Assert.Equal(1, clicks);
        Assert.Equal(0, session.GetByRole(AccessibilityRole.Button, "save", exact: true).Count);
        Assert.Equal(1, session.GetByRole(AccessibilityRole.Button, "Save draft", exact: true).Count);
    }

    [Fact]
    public void GetByText_AndAssertionsOnText()
    {
        using var session = Mount(Text.New("Hello,   locators!"));

        var hello = session.GetByText("hello, locators");
        Expect(hello).ToBeVisible();
        Expect(hello).ToHaveText("Hello, locators!");
        Expect(hello).ToContainText("locators");
        Expect(session.GetByText("Hello, locators!", exact: true)).ToHaveCount(0);   // exact keeps the spaces
    }

    [Fact]
    public void GetByLabel_FindsTheFieldAfterItsLabel_AndByAccessibleName()
    {
        using var session = Mount(
            Flex.Row(gap: 8).Children(Text.New("Email"), TextInput.New("")),
            Flex.Row(gap: 8).Children(Text.New("Name"), TextInput.New("")),
            TextInput.New("").AccessibleName("Search"));

        session.GetByLabel("Email").Fill("a@b.c");
        session.GetByLabel("Search").Fill("query");
        Expect(session.GetByLabel("Email")).ToHaveValue("a@b.c");
        Expect(session.GetByLabel("Name")).ToHaveValue("");
        Expect(session.GetByLabel("Search")).ToHaveValue("query");

        session.GetByLabel("Email").Fill("x");   // replaces, not appends
        Expect(session.GetByLabel("Email")).ToHaveValue("x");
    }

    [Fact]
    public void GetByTestId_Press_TypesIntoTheField()
    {
        var input = TextInput.New("abc");
        input.AutomationId = "search-box";
        using var session = Mount(input);

        var box = session.GetByTestId("search-box");
        box.Click();
        box.Press(KeyCode.End);
        box.Press(KeyCode.Backspace);
        Expect(box).ToHaveValue("ab");
    }

    [Fact]
    public void Strict_AnActionOnSeveralMatchesFails_NamingThem()
    {
        var deleted = new List<string>();
        using var session = Mount(Row("Alpha", () => deleted.Add("Alpha")), Row("Beta", () => deleted.Add("Beta")));

        var ex = Assert.Throws<InvalidOperationException>(() => session.GetByRole(AccessibilityRole.Button, "Delete").Click());
        Assert.Contains("Strict mode violation", ex.Message);
        Assert.Contains("matched 2 elements", ex.Message);
        Assert.Contains("0: Button \"Delete\" bounds=", ex.Message);
        Assert.Contains("1: Button \"Delete\" bounds=", ex.Message);
        Assert.Empty(deleted);

        Assert.Throws<InvalidOperationException>(() => Expect(session.GetByRole(AccessibilityRole.Button)).ToBeVisible());
    }

    [Fact]
    public void Chaining_Nth_First_Filter()
    {
        var deleted = new List<string>();
        using var session = Mount(Row("Alpha", () => deleted.Add("Alpha")), Row("Beta", () => deleted.Add("Beta")),
            Row("Gamma", () => deleted.Add("Gamma")));

        var rows = session.GetByRole(AccessibilityRole.Group);
        Expect(rows).ToHaveCount(3);
        rows.Filter("beta").GetByRole(AccessibilityRole.Button, "Delete").Click();
        rows.First.GetByRole(AccessibilityRole.Button).Click();
        session.GetByRole(AccessibilityRole.Button, "Delete").Nth(2).Click();
        Assert.Equal(["Beta", "Alpha", "Gamma"], deleted);

        Expect(rows.Filter("Gamma").GetByText("Gamma")).ToBeVisible();
        Expect(rows.Filter("Delta")).ToHaveCount(0);
        Assert.Equal("role=Group >> has-text=\"Beta\" >> role=Button", rows.Filter("Beta").GetByRole(AccessibilityRole.Button).ToString());
    }

    [Fact]
    public void Locator_IsLazy_ResolvesAgainstTheCurrentTree()
    {
        var label = Text.New("before");
        using var session = Mount(label);
        var after = session.GetByText("after");   // made before the element exists
        Assert.Equal(0, after.Count);

        label.Content = "after";
        session.PumpFrame();
        Assert.Equal(1, after.Count);
    }

    [Fact]
    public void Expect_WaitsForATimer_OnVirtualTime()
    {
        var status = Text.New("Saving…");
        using var session = Mount(status);
        long start = session.Time.GetTimestamp();
        Animate.Value(0f, 1f, _ => { }).Delay(1.5f).Duration(0f).OnComplete(() => status.Content = "Saved").Start();

        Expect(session.GetByText("Saved")).ToBeVisible();
        Expect(session.GetByText("Saving")).ToBeHidden();
        var waited = TimeSpan.FromTicks(session.Time.GetTimestamp() - start);
        Assert.InRange(waited.TotalSeconds, 1.5, 1.6);
    }

    [Fact]
    public void Click_WaitsUntilAnAnimatedElementStopsMoving()
    {
        int clicks = 0;
        var button = Button.New("Moving").OnClick(_ => clicks++);
        using var session = Mount(button);
        long start = session.Time.GetTimestamp();
        float top = button.WindowBorderBox.Y;
        Animate.Margin(button, new Thickness(0), new Thickness(200, 0, 0, 0)).Duration(0.5f).Start();
        session.PumpFrame();

        session.GetByRole(AccessibilityRole.Button, "Moving").Click();

        Assert.Equal(1, clicks);
        Assert.True(TimeSpan.FromTicks(session.Time.GetTimestamp() - start).TotalSeconds >= 0.5);
        Assert.Equal(top + 200, session.GetByRole(AccessibilityRole.Button).Resolve().Bounds.Y, 0.01f);   // Thickness is top first
    }

    [Fact]
    public void Click_WaitsForADisabledButtonToBeEnabled_OrTimesOutSayingWhy()
    {
        int clicks = 0;
        var button = Button.New("Send").Enabled(false).OnClick(_ => clicks++);
        using var session = Mount(button);
        var send = session.GetByRole(AccessibilityRole.Button, "Send");
        Expect(send).ToBeDisabled();

        var ex = Assert.Throws<TimeoutException>(() => send.Click(TimeSpan.FromMilliseconds(200)));
        Assert.Contains("timed out after 0.2 s of virtual time", ex.Message);
        Assert.Contains("element is disabled: Button \"Send\"", ex.Message);

        Animate.Value(0f, 1f, _ => { }).Delay(0.3f).Duration(0f).OnComplete(() => button.IsEnabled = true).Start();
        send.Click();
        Assert.Equal(1, clicks);
        Expect(send).ToBeEnabled();
    }

    [Fact]
    public void Expect_TimesOut_WithExpectedFoundAndTheMatches()
    {
        using var session = Mount(Text.New("Count: 1"));
        session.LocatorTimeout = TimeSpan.FromSeconds(1);
        long start = session.Time.GetTimestamp();

        var ex = Assert.Throws<LocatorAssertionException>(() => Expect(session.GetByText("Count")).ToHaveText("Count: 2"));
        Assert.Contains("Expected locator text=\"Count\" to have text \"Count: 2\"", ex.Message);
        Assert.Contains("but found text \"Count: 1\" after 1 s of virtual time", ex.Message);
        Assert.Contains("0: Label \"Count: 1\" bounds=(", ex.Message);
        Assert.Equal(TimeSpan.FromSeconds(1).Ticks, session.Time.GetTimestamp() - start, TimeSpan.FromMilliseconds(20).Ticks);

        var missing = Assert.Throws<LocatorAssertionException>(
            () => Expect(session.GetByRole(AccessibilityRole.Button, "Go"), TimeSpan.FromMilliseconds(100)).ToBeVisible());
        Assert.Contains("to be visible, but found no element", missing.Message);
        Assert.Contains("No element matched.", missing.Message);
    }

    [Fact]
    public void Check_Uncheck_AndHover()
    {
        int entered = 0;
        var checkbox = Checkbox.New(false, "Enable feature");
        using var session = Mount(checkbox, Button.New("Hover me").OnPointerEnter(_ => entered++));
        var box = session.GetByRole(AccessibilityRole.Checkbox, "Enable feature");

        box.Check();
        Expect(box).ToBeChecked();
        box.Check();   // already checked: left alone
        Assert.True(checkbox.IsChecked);
        box.Uncheck();
        Expect(box).ToBeChecked(false);

        session.GetByRole(AccessibilityRole.Button, "Hover me").Hover();
        Assert.Equal(1, entered);
    }

    [Fact]
    public void SelectOption_PicksADropdownRow()
    {
        int changed = -1;
        var dropdown = Dropdown.New(["Alpha", "Beta", "Gamma"], 0).Changed(i => changed = i);
        using var session = Mount(dropdown);
        var combo = session.GetByRole(AccessibilityRole.ComboBox);

        combo.SelectOption("Gamma");
        Assert.Equal(2, changed);
        Expect(combo).ToHaveValue("Gamma");
        combo.SelectOption("Alpha");
        Expect(combo).ToHaveValue("Alpha");

        var ex = Assert.Throws<InvalidOperationException>(() => combo.SelectOption("Delta"));
        Assert.Contains("\"Alpha\", \"Beta\", \"Gamma\"", ex.Message);
    }

    [Fact]
    public async Task AsyncForms_RunInPlace()
    {
        int clicks = 0;
        using var session = Mount(Button.New("Go").OnClick(_ => clicks++), TextInput.New("").AutomationId("f"));
        await session.GetByRole(AccessibilityRole.Button, "Go").ClickAsync();
        await session.GetByTestId("f").FillAsync("typed");
        await Expect(session.GetByTestId("f")).ToHaveValueAsync("typed");
        Assert.Equal(1, clicks);
        await Assert.ThrowsAsync<LocatorAssertionException>(
            () => Expect(session.GetByText("never"), TimeSpan.FromMilliseconds(50)).ToBeVisibleAsync());
    }

    [Fact]
    public void Locators_BelongToTheirSession()
    {
        int aClicks = 0, bClicks = 0;
        var aLabel = Text.New("A: 0");
        using var a = HeadlessSession.Create(300, 200);
        using var b = HeadlessSession.Create(300, 200);
        a.Window.SetRoot(Flex.Column().Children(Button.New("Go").OnClick(_ => aLabel.Content = $"A: {++aClicks}"), aLabel));
        b.Window.SetRoot(Flex.Column().Children(Button.New("Go").OnClick(_ => bClicks++), Text.New("B")));
        a.PumpFrame();
        b.PumpFrame();

        var goA = a.GetByRole(AccessibilityRole.Button, "Go");
        Assert.Same(a, goA.Session);
        long bTime = b.Time.GetTimestamp();

        goA.Click();
        Expect(a.GetByText("A: 1")).ToBeVisible();
        Expect(b.GetByText("A:")).ToHaveCount(0);
        Assert.Equal((1, 0), (aClicks, bClicks));
        Assert.Equal(bTime, b.Time.GetTimestamp());   // waiting on A pumps A's clock only

        b.GetByRole(AccessibilityRole.Button, "Go").Click();
        Assert.Equal((1, 1), (aClicks, bClicks));

        var second = a.CreateWindow("Second", 200, 100);
        second.SetRoot(Text.New("Only here"));
        a.PumpFrame();
        Expect(second.GetByText("Only here", exact: true)).ToBeVisible();
        Expect(a.GetByText("Only here")).ToHaveCount(0);
    }

    [Fact]
    public void VirtualListItems_AreFound_AndClicked_ScrolledIntoViewFirst()
    {
        string? clicked = null;
        var items = Enumerable.Range(0, 40).Select(i => $"Row {i}").ToList();
        var list = VirtualListElement<string>.New().ItemHeight(24)
            .RenderWith(t => Button.New(t).OnClick(_ => clicked = t))
            .Items(items);
        list.Height(Dimension.Px(120));
        using var session = Mount(list);

        // Item nodes have no element of their own: the locator pairs them with the list.
        Expect(session.GetByText("Row 2", exact: true)).ToHaveCount(1);
        Expect(session.GetByRole(AccessibilityRole.ListItem)).ToHaveCount(40);
        Assert.Same(list, session.GetByText("Row 2", exact: true).Resolve().Element);

        session.GetByText("Row 2", exact: true).Click();
        Assert.Equal("Row 2", clicked);

        session.GetByText("Row 30", exact: true).Click();   // far below the list's viewport
        Assert.Equal("Row 30", clicked);
        Assert.True(list.ScrollOffsetY > 0);
    }

    [Fact]
    public void Click_ScrollsAPartlyClippedElementIntoView_RatherThanHittingWhatIsUnderItsCentre()
    {
        int target = 0, other = 0;
        var scroll = ScrollView.Vertical().Height(100).Children(
            Flex.Column().Children(
                Box.New().Height(Dimension.Px(90)),
                Button.New("Target").Height(Dimension.Px(40)).OnClick(_ => target++)));
        using var session = Mount(scroll, Button.New("Other").OnClick(_ => other++));

        session.GetByRole(AccessibilityRole.Button, "Target", exact: true).Click();
        Assert.Equal((1, 0), (target, other));
        Assert.True(scroll.ScrollOffsetY > 0);
    }

    [Fact]
    public void IsEntered_AndIsDrawingFrame_TellWhereTheTestIs()
    {
        using var a = HeadlessSession.Create(100, 100);
        using var b = HeadlessSession.Create(100, 100);
        bool? drawingInside = null;
        a.Window.SetRoot(Canvas.New(_ => drawingInside = a.Window.IsDrawingFrame).Width(Dimension.Px(10)).Height(Dimension.Px(10)));
        a.PumpFrame(TimeSpan.FromMilliseconds(16), forceRender: true);
        Assert.True(drawingInside);
        Assert.False(a.Window.IsDrawingFrame);

        b.Enter();
        Assert.True(b.IsEntered);
        Assert.False(a.IsEntered);
        a.Enter();
        Assert.True(a.IsEntered);
        Assert.False(b.IsEntered);
    }
}
