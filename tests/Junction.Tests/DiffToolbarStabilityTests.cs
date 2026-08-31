using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Headless;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// The diff toolbar holds still while its buttons are toggled.
///
/// <para>Reported by eye and then measured: turning Wrap on slid the Split button 2px left, and
/// Unified moved with it without having changed state at all. The buttons size to their content
/// and only the active one carried a border, so activating one made it 2px wider and — the group
/// being right-aligned — pushed everything to its left along.</para>
///
/// <para>The tempting diagnosis is <c>content-box</c> sizing, and it is wrong. HultUI is
/// border-box already, but an element with no declared width is <c>content + padding + border</c>
/// under either box model, so there is nothing for a box model to reinterpret. The fix is an
/// outline: a ring that layout does not read.</para>
///
/// <para>Asserted as "the other two did not move" rather than "the widths are equal", because
/// equal widths would also hold if every button grew together — which is a different bug that
/// still moves the group's left edge.</para>
/// </summary>
[Collection("JunctionDiff")]
public class DiffToolbarStabilityTests
{
    [Fact]
    public void TogglingWrapDoesNotMoveTheButtonsBesideIt()
    {
        DiffScrollTests.Hosted((session, app) =>
        {
            Widen(session);
            var before = Boxes(session);

            session.Window.PressKey(KeyCode.W);      // wrap on
            session.PumpFrames(3);
            var after = Boxes(session);

            Assert.Equal(before["Unified"], after["Unified"]);
            Assert.Equal(before["Split"], after["Split"]);
        });
    }

    /// <summary>And the button that <i>did</i> change keeps its own box too — the ring is drawn
    /// outside it, so even the button carrying the state does not resize.</summary>
    [Fact]
    public void TheButtonThatChangedKeepsItsOwnBox()
    {
        DiffScrollTests.Hosted((session, app) =>
        {
            Widen(session);
            var before = Boxes(session)["Wrap"];

            session.Window.PressKey(KeyCode.W);
            session.PumpFrames(3);

            Assert.Equal(before, Boxes(session)["Wrap"]);
        });
    }

    /// <summary>Switching unified/split moves neither of the two buttons that flip, nor the one
    /// beside them — the same guarantee across the pair that swaps state together.</summary>
    [Fact]
    public void SwitchingUnifiedAndSplitMovesNothing()
    {
        DiffScrollTests.Hosted((session, app) =>
        {
            Widen(session);
            var before = Boxes(session);

            session.Window.PressKey(KeyCode.S);      // split on
            session.PumpFrames(3);
            var after = Boxes(session);

            Assert.Equal(before["Unified"], after["Unified"]);
            Assert.Equal(before["Split"], after["Split"]);
            Assert.Equal(before["Wrap"], after["Wrap"]);
        });
    }

    /// <summary>Junction drops these buttons to icon-only below 1140px of content width, and an
    /// icon-only button has no accessible name to find it by. The harness opens at 1400x900,
    /// which lands just under that once the sidebar is subtracted, so widen first.</summary>
    private static void Widen(HeadlessSession session)
    {
        session.Window.SetClientSize(1700, 900);
        session.PumpFrames(4);
    }

    /// <summary>Every mode button's painted box, by label. <see cref="LayoutProbe.Painted"/> is
    /// the border box — the thing that grew — rather than the content box, which would have kept
    /// its size while the element around it expanded.</summary>
    private static Dictionary<string, HultUI.Core.Primitives.Rect> Boxes(HeadlessSession session)
    {
        var found = new Dictionary<string, HultUI.Core.Primitives.Rect>();
        foreach (var label in new[] { "Unified", "Split", "Wrap" })
        {
            var el = session.Window.FindByRole(AccessibilityRole.Button, label)
                ?? throw new InvalidOperationException($"no \"{label}\" button on screen");
            found[label] = LayoutProbe.Painted(el.Element);
        }
        return found;
    }
}
