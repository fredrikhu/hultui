using HultUI.Core.Builders;
using HultUI.Core.Elements;
using System.Linq;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// The scrollbar thumb is a pill.
///
/// <para>Every current platform rounds it, and a square-ended bar is one of the few details
/// that dates a window on its own — this was found by putting the framework's scrollbar beside
/// the WinUI Gallery's, where the resting thumb is a 2px pill with no track at all.</para>
/// </summary>
public class ScrollbarShapeTests
{
    private static string Draw(Action<ScrollViewElement>? configure = null)
    {
        using var session = HeadlessSession.Create(200, 150,
            options: new HeadlessOptions { RecordDrawCalls = true });

        var scroll = ScrollView.New().Height(150f).Children(Box.New().Height(900f).FlexShrink(0));
        configure?.Invoke(scroll);
        session.Application.SetRoot(scroll);
        session.PumpFrames(2);

        session.Window.RenderLog.Clear();
        session.PumpFrame(session.DefaultFrameDelta, forceRender: true);

        // Only the scrollbar's own draws. Asserting against the whole log would be asserting
        // against every other rounded thing on the frame as well.
        var lines = session.Window.RenderLog.Named("DrawRoundedRect").Select(o => o.ToString());
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>Half the default 6px width, so the ends are semicircles rather than a radius
    /// that happens to look rounded at one size and blunt at another.</summary>
    [Fact]
    public void TheThumbIsRoundedToHalfItsWidthByDefault()
    {
        var log = Draw();

        Assert.Contains("DrawRoundedRect", log);
        Assert.Contains("r=3", log);
    }

    [Fact]
    public void TheRadiusFollowsTheWidth()
    {
        var log = Draw(s => s.ScrollbarWidth = 10f);

        Assert.Contains("r=5", log);
    }

    /// <summary>Square ends are still available for anything that wants them — the default is a
    /// default, not a decision taken away from the caller.</summary>
    [Fact]
    public void ZeroGivesSquareEnds()
    {
        var log = Draw(s => s.ScrollbarRadius = 0f);

        Assert.DoesNotContain("r=", log);
    }

    /// <summary>The radius survives reconciliation. A property <c>PatchFrom</c> forgets reverts
    /// on the first rebuild, and a scrollbar that squares off when an unrelated signal fires is
    /// the kind of thing nobody manages to reproduce.</summary>
    [Fact]
    public void TheRadiusFollowsARebuildThatChangesIt()
    {
        using var session = HeadlessSession.Create(200, 150);

        var live = ScrollView.New().Height(150f);
        live.ScrollbarRadius = 0f;
        session.Application.SetRoot(Box.New().Children(live));
        session.PumpFrames(2);

        var incoming = ScrollView.New().Height(150f);
        incoming.ScrollbarRadius = 4f;
        live.PatchFrom(incoming);

        Assert.Equal(4f, live.ScrollbarRadius);
    }
}
