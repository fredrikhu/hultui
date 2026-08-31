using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// The bright line along the bottom of a text field, and the fill that changes underneath it.
///
/// <para>Measured off the WinUI Gallery, because a text box turned out to be the one control
/// whose border is not the two-tone every other control has. At rest a dark WinUI text box is
/// <c>#303030</c> on three sides and <c>#9A9A9A</c> along the bottom —
/// <c>ControlStrongFillColorDefault</c>, a different token entirely. On focus the bottom becomes
/// a 2px accent, the other three sides do not move at all, and the fill goes <i>darker</i>. The
/// obvious implementation — light the whole ring on focus — is wrong on all three counts.</para>
/// </summary>
public class TextFieldUnderlineTests
{
    private static readonly Color Rest = Color.FromHex("#9A9A9A");
    private static readonly Color Focus = Color.FromHex("#60CDFF");
    private static readonly Color Sides = Color.FromHex("#303030");
    private static readonly Color Fill = Color.FromHex("#2D2D2D");
    private static readonly Color FocusFill = Color.FromHex("#1F1F1F");

    private static Theme Fluent => Theme.Dark.With(new TextInputTheme
    {
        Background = Fill,
        BorderColor = Sides,
        FocusBorderColor = Sides,          // the sides stay exactly as they were
        BorderWidth = new Thickness(1),
        UnderlineColor = Rest,
        UnderlineThickness = 1f,
        FocusUnderlineColor = Focus,
        FocusUnderlineThickness = 2f,
        FocusBackground = FocusFill,
    });

    private static (HeadlessSession Session, TextInputElement Input) Start(Theme? theme = null)
    {
        var session = HeadlessSession.Create(300, 120,
            options: new HeadlessOptions { RecordDrawCalls = true });
        session.Application.Theme = theme ?? Fluent;

        var input = TextInput.New("").Width(200f);
        session.Application.SetRoot(Box.New().Padding(20f).Children(input));
        session.PumpFrames(2);
        return (session, input);
    }

    private static string Frame(HeadlessSession session)
    {
        session.Window.RenderLog.Clear();
        session.PumpFrame(session.DefaultFrameDelta, forceRender: true);
        return session.Window.RenderLog.ToString();
    }

    [Fact]
    public void AtRestTheBottomEdgeIsItsOwnColour()
    {
        var (session, _input) = Start();
        using var _ = session;

        var frame = Frame(session);

        Assert.Contains("#9A9A9A", frame);          // the underline
        Assert.Contains("#303030", frame);          // the other three sides, unchanged
        Assert.DoesNotContain("#60CDFF", frame);    // no accent until focus
    }

    [Fact]
    public void FocusMovesTheUnderlineAndTheFillAndNothingElse()
    {
        var (session, input) = Start();
        using var _ = session;

        session.Window.Click(input);
        session.PumpFrames(2);
        var frame = Frame(session);

        Assert.Contains("#60CDFF", frame);          // accent underline
        Assert.Contains("#1F1F1F", frame);          // ControlFillColorInputActive
        Assert.Contains("#303030", frame);          // the sides did not light up
        Assert.DoesNotContain("#9A9A9A", frame);    // the resting underline is gone
        Assert.DoesNotContain("#2D2D2D", frame);    // and so is the resting fill
    }

    /// <summary>Two pixels focused, one at rest — the thickness carries as much of the signal as
    /// the colour, and a focus state that only recolours reads as weaker than the real one.</summary>
    [Fact]
    public void TheUnderlineThickens()
    {
        var (session, input) = Start();
        using var _ = session;

        Assert.Contains("width=1", UnderlineOp(Frame(session)));

        session.Window.Click(input);
        session.PumpFrames(2);

        Assert.Contains("width=2", UnderlineOp(Frame(session)));
    }

    /// <summary>
    /// The guard for every app that has never heard of this: a theme that says nothing about an
    /// underline gets none, and its fill does not change under focus either. Asserted on the
    /// element and on the frame, because both halves are opt-in and either one defaulting the
    /// wrong way would repaint every text box in every app.
    /// </summary>
    [Fact]
    public void AThemeThatSaysNothingChangesNothing()
    {
        var (session, input) = Start(Theme.Dark);
        using var _ = session;

        Assert.Null(input.UnderlineColor);
        Assert.Null(input.FocusUnderlineColor);
        Assert.Null(input.FocusBackground);

        var resting = Hex(input.BackgroundColor);
        Assert.Contains(resting, Frame(session));

        session.Window.Click(input);
        session.PumpFrames(2);

        // Still the same fill: focus recolours the border, as it always did, and nothing else.
        Assert.Contains(resting, Frame(session));
    }

    private static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    /// <summary>A chromeless input has handed its box to the caller, and the underline is part
    /// of the box — drawing one would put a line through somebody else's control.</summary>
    [Fact]
    public void AChromelessInputDrawsNoUnderline()
    {
        using var session = HeadlessSession.Create(300, 120,
            options: new HeadlessOptions { RecordDrawCalls = true });
        session.Application.Theme = Fluent;

        var input = TextInput.New("").Chromeless().Width(200f);
        session.Application.SetRoot(Box.New().Padding(20f).Children(input));
        session.PumpFrames(2);

        Assert.DoesNotContain("#9A9A9A", Frame(session));
    }

    /// <summary>A text area is the same control with more rows, and reads the same theme — it
    /// would be a strange app whose single-line fields had an underline and whose multi-line
    /// ones did not.</summary>
    [Fact]
    public void ATextAreaGetsTheSameUnderline()
    {
        using var session = HeadlessSession.Create(300, 160,
            options: new HeadlessOptions { RecordDrawCalls = true });
        session.Application.Theme = Fluent;

        var area = TextArea.New("hello").Width(200f).Height(80f);
        session.Application.SetRoot(Box.New().Padding(20f).Children(area));
        session.PumpFrames(2);

        Assert.Contains("#9A9A9A", Frame(session));
    }

    private static string UnderlineOp(string frame) =>
        frame.Split('\n').FirstOrDefault(l => l.Contains("#9A9A9A") || l.Contains("#60CDFF")) ?? "";
}
