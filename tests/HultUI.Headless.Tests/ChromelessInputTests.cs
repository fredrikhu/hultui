using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// An input that hands its box to the caller, for the case where it is one part of a control
/// someone else composed — a search field with a magnifier beside the text, a cell editor.
///
/// <para>Without it there is no way to get one. <c>ApplyTheme</c> runs on every style pass, so
/// a background set on the instance survives until the next pass and then reverts, which looks
/// like the setter not working and is really the theme winning a race nobody knew was running.
/// These tests pump several frames on purpose: one frame would pass either way.</para>
/// </summary>
public class ChromelessInputTests
{
    private static Theme Loud => Theme.Light.With(new TextInputTheme
    {
        Background = Color.FromHex("#112233"),
        BorderColor = Color.FromHex("#445566"),
        BorderWidth = new Thickness(4),
        CornerRadius = new CornerRadius(9f),
        Padding = new Thickness(7),
    });

    [Fact]
    public void AChromelessInputKeepsTheBoxTheCallerGaveIt()
    {
        using var session = HeadlessSession.Create(300, 120);
        session.Application.Theme = Loud;

        var input = TextInput.New("")
            .Chromeless()
            .Background(Color.Red)
            .BorderColor(Color.Green)
            .BorderWidth(2f)
            .CornerRadius(1f)
            .Padding(Thickness.Zero);

        session.Application.SetRoot(Box.New().Padding(10f).Children(input));
        session.PumpFrames(5);

        Assert.Equal(Color.Red, input.BackgroundColor);
        Assert.Equal(Color.Green, input.BorderColor);
        Assert.Equal(new Thickness(2f), input.Box.BorderWidth);
        Assert.Equal(new CornerRadius(1f), input.CornerRadius);
        Assert.Equal(Thickness.Zero, input.Box.Padding);
    }

    /// <summary>The control case, and the reason the flag has to exist: an ordinary input takes
    /// the theme's box back on the next style pass, whatever the caller set.</summary>
    [Fact]
    public void AnOrdinaryInputIsRepaintedByTheTheme()
    {
        using var session = HeadlessSession.Create(300, 120);
        session.Application.Theme = Loud;

        var input = TextInput.New("").Background(Color.Red).BorderColor(Color.Green);

        session.Application.SetRoot(Box.New().Padding(10f).Children(input));
        session.PumpFrames(5);

        Assert.Equal(Color.FromHex("#112233"), input.BackgroundColor);
        Assert.Equal(Color.FromHex("#445566"), input.BorderColor);
        Assert.Equal(new Thickness(4), input.Box.BorderWidth);
    }

    /// <summary>Chrome is the only thing given up. Placeholder, caret and selection colours are
    /// about the text, not the box, so they keep coming from the theme — an app composing a
    /// search field should not have to restate the palette to get a caret the right colour.</summary>
    [Fact]
    public void TheTextColoursStillComeFromTheTheme()
    {
        using var session = HeadlessSession.Create(300, 120);
        session.Application.Theme = Theme.Light.With(new TextInputTheme
        {
            PlaceholderColor = Color.FromHex("#AABBCC"),
            CursorColor = Color.FromHex("#DDEEFF"),
        });

        var input = TextInput.New("").Chromeless();
        session.Application.SetRoot(Box.New().Padding(10f).Children(input));
        session.PumpFrames(5);

        Assert.Equal(Color.FromHex("#AABBCC"), input.PlaceholderColor);
        Assert.Equal(Color.FromHex("#DDEEFF"), input.CursorColor);
    }

    /// <summary>
    /// The flag follows a rebuild that changes its mind.
    ///
    /// <para>Reconciliation patches the live element from a freshly built one, so anything
    /// <c>PatchFrom</c> forgets keeps the value it was born with for the life of the element.
    /// Asserting that a chromeless input <i>stays</i> chromeless would prove nothing — it would
    /// pass with the copy deleted, because the live element was already chromeless. Turning it
    /// back on is the assertion with something to say.</para>
    /// </summary>
    [Fact]
    public void TheFlagFollowsARebuildThatChangesIt()
    {
        using var session = HeadlessSession.Create(300, 120);
        session.Application.Theme = Loud;

        var host = new SwitchableInput();
        var root = host.Mount();
        session.Application.SetRoot(root);
        session.PumpFrames(3);

        var live = FirstInput(root);
        Assert.False(live.DrawsChrome);

        host.Chromeless = false;
        host.Rebuild();
        session.PumpFrames(3);

        // Patched rather than replaced — otherwise this would be asserting on a discarded
        // element and would pass whatever PatchFrom does.
        Assert.Same(live, FirstInput(root));
        Assert.True(live.DrawsChrome);
        Assert.Equal(Color.FromHex("#112233"), live.BackgroundColor);
    }

    /// <summary>A real component, because only reconciliation runs <c>PatchFrom</c> —
    /// <c>ReplaceChildren</c> on a mounted box swaps the child outright and leaves the old one
    /// detached, which is a very convincing way to write a test that proves nothing.</summary>
    private sealed class SwitchableInput : Component
    {
        public bool Chromeless = true;

        public override Element Build()
        {
            var input = TextInput.New("");
            if (Chromeless) input.Chromeless().Background(Color.Red);
            return Box.New().Padding(10f).Children(input);
        }

        public void Rebuild() => MarkDirty();
    }

    private static TextInputElement FirstInput(Element root)
    {
        if (root is TextInputElement match) return match;
        foreach (var child in root.Children)
            if (FirstInput(child) is { } found)
                return found;
        return null!;
    }
}
