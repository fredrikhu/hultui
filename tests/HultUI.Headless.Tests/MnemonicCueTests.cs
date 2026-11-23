using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// A button's and a check box's mnemonic is underlined only while Alt is held, as a menu bar's
/// titles are: not at rest, not after Alt is let go, not after a click that came while it was held,
/// not after the window lost the foreground with it held, and not for AltGr, which types. Each
/// change is drawn in the frame after the key: the window asks for it.
/// </summary>
public class MnemonicCueTests
{
    [Fact]
    public void TheUnderlineShows_OnlyWhileAltIsHeld()
    {
        using var session = HeadlessSession.Create(400, 200, options: new HeadlessOptions { RecordDrawCalls = true });
        var button = Button.New("&Apply");
        button.UseMnemonic = true;
        var checkbox = Checkbox.New(false, "&Wrap lines");
        checkbox.UseMnemonic = true;
        session.Window.SetRoot(Flex.Column(gap: 8).Padding(20f).Children(button, checkbox));
        session.PumpFrame();
        var w = session.Window;

        Assert.Equal((2, 0), Labels(session, () => w.Window.Root!.MarkPaintDirty()));
        Assert.Equal((2, 2), Labels(session, () => w.KeyDown(KeyCode.AltLeft, KeyModifiers.Alt, modifierKeys: ModifierKeys.AltLeft)));
        Assert.Equal((2, 0), Labels(session, () => w.KeyUp(KeyCode.AltLeft)));

        Assert.Equal((2, 2), Labels(session, () => w.KeyDown(KeyCode.AltLeft, KeyModifiers.Alt, modifierKeys: ModifierKeys.AltLeft)));
        Assert.Equal((2, 0), Labels(session, () => w.Click(checkbox, modifiers: KeyModifiers.Alt)));
        w.KeyUp(KeyCode.AltLeft);

        Assert.Equal((2, 0), Labels(session, () =>
        {
            w.KeyDown(KeyCode.AltRight, modifierKeys: ModifierKeys.AltGr);
            w.Window.Root!.MarkPaintDirty();
        }));
        w.KeyUp(KeyCode.AltRight);

        Assert.Equal((2, 2), Labels(session, () => w.KeyDown(KeyCode.AltLeft, KeyModifiers.Alt, modifierKeys: ModifierKeys.AltLeft)));
        Assert.Equal((2, 0), Labels(session, w.Deactivate));   // Alt+Tab: the release goes to another window
    }

    /// <summary>The next frame after <paramref name="act"/>: how many of the labels "Apply" and
    /// "Wrap lines" it drew, and how many of those with the underline span.</summary>
    private static (int Drawn, int Underlined) Labels(HeadlessSession session, Action act)
    {
        session.Window.RenderLog.Clear();
        act();
        session.PumpFrame();
        var labels = session.Window.RenderLog.Named("DrawText")
            .Where(o => o.Args.StartsWith("\"Apply\"", StringComparison.Ordinal) || o.Args.StartsWith("\"Wrap lines\"", StringComparison.Ordinal))
            .ToList();
        return (labels.Count, labels.Count(o => o.Args.Contains("spans=[0+1:", StringComparison.Ordinal)));
    }

    private sealed class Form : Component
    {
        public string Label = "&Apply";
        public bool UseMnemonic = true;
        public readonly HultUI.Core.Reactivity.Ref<ButtonElement> Button = new();

        public override Element Build()
        {
            var button = Core.Builders.Button.New(Label);
            button.UseMnemonic = UseMnemonic;
            button.WithRef(Button);
            return Flex.Column().Children(button);
        }

        public void Rebuild() => MarkDirty();
    }

    /// <summary>A rebuild patches the kept button and its label: a new label, and the flag turned off
    /// and on again, each show on the button that was kept.</summary>
    [Fact]
    public void ARebuild_CarriesTheLabelAndTheFlag_OntoTheKeptButton()
    {
        using var session = HeadlessSession.Create(300, 200);
        var form = new Form();
        session.Window.SetRoot(form.Mount());
        session.PumpFrames(2);
        var kept = form.Button.Current!;
        var label = (TextElement)kept.Children[0];

        form.Label = "&Retry";
        form.Rebuild();
        session.PumpFrame();
        Assert.Same(kept, form.Button.Current);
        Assert.Equal(('r', "Retry", 0), (kept.Mnemonic, label.Content, label.MnemonicIndex));

        form.UseMnemonic = false;
        form.Rebuild();
        session.PumpFrame();
        Assert.Equal(('\0', "&Retry", -1), (kept.Mnemonic, label.Content, label.MnemonicIndex));

        form.UseMnemonic = true;
        form.Rebuild();
        session.PumpFrame();
        Assert.Equal(('r', "Retry", 0), (kept.Mnemonic, label.Content, label.MnemonicIndex));
        Assert.Equal("&Retry", kept.Label);
    }
}
