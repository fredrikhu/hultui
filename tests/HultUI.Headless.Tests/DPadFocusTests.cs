using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using Xunit;
using TextB = HultUI.Core.Builders.Text;

namespace HultUI.Headless.Tests;

/// <summary>
/// <see cref="Window.ArrowKeysMoveFocus"/>: a TV remote's D-pad (docs/design/DPAD-FOCUS.md). On the
/// Shield an arrow moved nothing, nothing had the focus at start, and a rebuild that replaced the
/// focused button left nothing focused, so OK pressed nothing.
/// </summary>
public class DPadFocusTests
{
    private static ButtonElement Key(string label) =>
        Button.New(label).Width(Dimension.Px(80)).Height(Dimension.Px(32));

    /// <summary>Three rows of three buttons, A to I, 80 wide with 16 between, from x 20: A 20–100,
    /// B 116–196, C 212–292. The middle row is shifted right by 32: D 52–132, E 148–228, F 244–324.</summary>
    private static (Element Root, ButtonElement[] Keys) Grid()
    {
        var keys = "ABCDEFGHI".Select(c => Key(c.ToString())).ToArray();
        var root = Flex.Column(gap: 16).Padding(new Thickness(20));
        for (int row = 0; row < 3; row++)
        {
            var line = Flex.Row(gap: 16);
            if (row == 1) line.Padding(new Thickness(0, 0, 0, 32));   // left
            for (int col = 0; col < 3; col++) line.AddChild(keys[row * 3 + col]);
            root.AddChild(line);
        }
        return (root, keys);
    }

    private static HeadlessSession DPad(Element root, int width = 500, int height = 400)
    {
        var session = HeadlessSession.Create(width, height);
        session.Window.Window.ArrowKeysMoveFocus = true;
        session.Window.SetRoot(root);
        session.PumpFrame();
        return session;
    }

    private static Element? Focused(HeadlessSession session) => session.Window.Window.FocusedElement;

    [Fact]
    public void TheFirstFrame_FocusesTheFirstStop_WithNoKeyPressed()
    {
        var (root, keys) = Grid();
        using var session = DPad(root);
        Assert.Same(keys[0], Focused(session));
    }

    [Fact]
    public void Arrows_MoveTheFocus_ToTheNearestButtonThatWay()
    {
        var (root, keys) = Grid();
        using var session = DPad(root);

        session.Window.PressKey(KeyCode.Right);
        Assert.Same(keys[1], Focused(session));   // B
        session.Window.PressKey(KeyCode.Down);
        // Under B (116–196) the beam meets D (52–132) and E (148–228): E's centre is nearer.
        Assert.Same(keys[4], Focused(session));   // E
        session.Window.PressKey(KeyCode.Left);
        Assert.Same(keys[3], Focused(session));   // D
        // Nothing left of D in its row: A, up and to the left, is the only thing that way (Android's rule).
        session.Window.PressKey(KeyCode.Left);
        Assert.Same(keys[0], Focused(session));
        session.Window.PressKey(KeyCode.Left);
        Assert.Same(keys[0], Focused(session));   // nothing at all: no wrap
        session.Window.PressKey(KeyCode.Down);
        session.Window.PressKey(KeyCode.Down);
        Assert.Same(keys[6], Focused(session));   // A → D → G
        session.Window.PressKey(KeyCode.Right);
        session.Window.PressKey(KeyCode.Right);
        Assert.Same(keys[8], Focused(session));   // I
        session.Window.PressKey(KeyCode.Up);
        Assert.Same(keys[5], Focused(session));   // F (244–324) is in I's beam (212–292), E is not
    }

    [Fact]
    public void WithTheSwitchOff_ArrowsMoveNothing()
    {
        var (root, keys) = Grid();
        using var session = HeadlessSession.Create(500, 400);
        session.Window.SetRoot(root);
        session.PumpFrame();
        Assert.Null(Focused(session));            // and nothing is focused for it
        keys[0].Focus();
        session.Window.PressKey(KeyCode.Right);
        session.Window.PressKey(KeyCode.Down);
        Assert.Same(keys[0], Focused(session));
    }

    [Fact]
    public void AnArrowWithAModifier_IsNotTheDPads()
    {
        var (root, keys) = Grid();
        using var session = DPad(root);
        session.Window.PressKey(KeyCode.Right, KeyModifiers.Shift);
        session.Window.PressKey(KeyCode.Right, KeyModifiers.Ctrl);
        Assert.Same(keys[0], Focused(session));
    }

    [Fact]
    public void ATextInput_KeepsLeftAndRight_ForItsCaret_AndUpAndDownLeaveIt()
    {
        var field = TextInput.New("abc").Width(Dimension.Px(200));
        var below = Key("Below");
        var root = Flex.Column(gap: 16).Padding(new Thickness(20));
        root.AddChild(field);
        root.AddChild(below);
        using var session = DPad(root);
        Assert.Same(field, Focused(session));

        session.Window.PressKey(KeyCode.Left);
        session.Window.PressKey(KeyCode.Right);
        Assert.Same(field, Focused(session));
        session.Window.PressKey(KeyCode.Down);
        Assert.Same(below, Focused(session));
        session.Window.PressKey(KeyCode.Up);
        Assert.Same(field, Focused(session));
    }

    [Fact]
    public void AFieldTheDPadReaches_WaitsForOK_BeforeItsKeyboard_AndOKIsNotItsEnter()
    {
        var field = TextInput.New("").Width(Dimension.Px(200));
        int enters = 0;
        var root = Flex.Column(gap: 16).Padding(new Thickness(20));
        root.On<KeyDownEvent>(e => { if (e.Key == KeyCode.Enter) enters++; });
        root.AddChild(field);
        root.AddChild(Key("Below"));
        using var session = DPad(root);
        Assert.Same(field, Focused(session));
        // On a TV the keyboard took the D-pad as soon as a field had the focus.
        Assert.False(session.Window.TextInputHost.IsTextInputActive);

        session.Window.PressKey(KeyCode.Enter);
        Assert.True(session.Window.TextInputHost.IsTextInputActive);
        Assert.Equal(0, enters);                  // the keyboard came up; the form was not submitted

        session.Window.PressKey(KeyCode.Enter);   // the keyboard's Done
        Assert.Equal(1, enters);

        session.Window.PressKey(KeyCode.Down);
        Assert.False(session.Window.TextInputHost.IsTextInputActive);
    }

    [Fact]
    public void ASlider_StepsOnLeftAndRight_AndLetsUpAndDownGo()
    {
        var slider = Slider.New(30, 0, 100).Width(Dimension.Px(240));
        var below = Key("Below");
        var root = Flex.Column(gap: 16).Padding(new Thickness(20));
        root.AddChild(slider);
        root.AddChild(below);
        using var session = DPad(root);
        Assert.Same(slider, Focused(session));

        session.Window.PressKey(KeyCode.Right);
        Assert.Same(slider, Focused(session));    // it used to step and let the focus leave
        double stepped = slider.Value;
        Assert.True(stepped > 30);

        session.Window.PressKey(KeyCode.Down);
        Assert.Same(below, Focused(session));
        Assert.Equal(stepped, slider.Value);      // and Down no longer stepped it down
    }

    [Fact]
    public void DownPastTheBottom_ScrollsTheNextButtonIntoView()
    {
        var column = Flex.Column(gap: 8).Padding(new Thickness(8));
        var keys = Enumerable.Range(0, 20).Select(i => Key("Row " + i)).ToArray();
        foreach (var k in keys) column.AddChild(k);
        var scroll = ScrollView.Vertical().Height(Dimension.Px(200));
        scroll.AddChild(column);
        using var session = DPad(scroll, 300, 300);

        for (int i = 0; i < 10; i++) session.Window.PressKey(KeyCode.Down);
        session.PumpFrame();
        Assert.Same(keys[10], Focused(session));
        var box = keys[10].WindowBorderBox;
        var view = scroll.WindowContentBox;
        Assert.True(box.Y >= view.Y && box.Bottom <= view.Bottom,
            $"row 10 at {box.Y}–{box.Bottom}, the view {view.Y}–{view.Bottom}, scrolled {scroll.ScrollOffsetY}");
    }

    [Fact]
    public void AFocusedButtonARebuildReplaces_HandsTheFocusToWhatIsNearestWhereItWas()
    {
        // The sign-in page on the Shield: an error banner inserted above the buttons, which the rebuild
        // made again, left nothing focused.
        var field = TextInput.New("").Width(Dimension.Px(200));
        var form = Flex.Column(gap: 16).Padding(new Thickness(20));
        form.AddChild(field);
        var code = Key("Code");
        form.AddChild(code);
        using var session = DPad(form);
        session.Window.PressKey(KeyCode.Down);
        Assert.Same(code, Focused(session));

        form.RemoveChild(code);
        Assert.Null(Focused(session));
        form.AddChild(TextB.New("Enter your server's address"));
        var again = Key("Code");
        form.AddChild(again);
        session.PumpFrame();

        Assert.Same(again, Focused(session));     // not the field, the first stop
    }

    [Fact]
    public void ADialog_KeepsTheDPadInsideIt()
    {
        var (root, keys) = Grid();
        using var session = DPad(root);
        var ok = Key("OK");
        var cancel = Key("Cancel");
        var panel = Flex.Row(gap: 16).Padding(new Thickness(20)).Background(Color.FromHex("#282828"));
        panel.AddChild(ok);
        panel.AddChild(cancel);
        session.Window.Window.ShowOverlay(panel, 100, 100, modal: true);
        session.PumpFrame();
        Assert.Same(ok, Focused(session));

        session.Window.PressKey(KeyCode.Right);
        Assert.Same(cancel, Focused(session));
        session.Window.PressKey(KeyCode.Down);
        session.Window.PressKey(KeyCode.Up);
        session.Window.PressKey(KeyCode.Right);
        Assert.Same(cancel, Focused(session));    // nothing of the page behind it
    }
}
