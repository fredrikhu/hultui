using HultUI.Core.Animation;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Core.Tests.Elements;

/// <summary>
/// Covers declarative interaction-state styling: overrides apply and revert, states
/// layer in the right order, transitions animate the change, and the base snapshot
/// survives reconciliation.
/// </summary>
public class InteractionStyleTests
{
    private static readonly Color Blue = new(0, 0, 255);
    private static readonly Color Red = new(255, 0, 0);
    private static readonly Color Green = new(0, 128, 0);

    [Fact]
    public void Hover_AppliesOverride_AndRevertsToBase()
    {
        var box = Box.New().Background(Blue).OnHover(b => b.Background(Red));

        box.SetInteractionHovered(true);
        Assert.Equal(Red, box.BackgroundColor);

        box.SetInteractionHovered(false);
        Assert.Equal(Blue, box.BackgroundColor);
    }

    [Fact]
    public void Pressed_AppliesOverride()
    {
        var box = Box.New().Background(Blue).OnPressed(b => b.Background(Red));

        box.SetInteractionPressed(true);
        Assert.Equal(Red, box.BackgroundColor);
        box.SetInteractionPressed(false);
        Assert.Equal(Blue, box.BackgroundColor);
    }

    [Fact]
    public void States_Layer_PressedOverHover()
    {
        var box = Box.New().Background(Blue)
            .OnHover(b => b.Background(Green))
            .OnPressed(b => b.Background(Red));

        box.SetInteractionHovered(true);
        Assert.Equal(Green, box.BackgroundColor);

        box.SetInteractionPressed(true);
        Assert.Equal(Red, box.BackgroundColor); // pressed wins

        box.SetInteractionPressed(false);
        Assert.Equal(Green, box.BackgroundColor); // back to hover

        box.SetInteractionHovered(false);
        Assert.Equal(Blue, box.BackgroundColor); // back to base
    }

    [Fact]
    public void Transition_AnimatesFromBaseToTarget()
    {
        var box = Box.New().Background(Blue)
            .OnHover(b => b.Background(Red))
            .Transition(TransitionProperty.Background, ms: 100f, Easing.Linear);

        box.SetInteractionHovered(true);
        // The transition holds the value at the base and animates toward the target,
        // so it hasn't jumped straight to red.
        Assert.Equal(Blue, box.BackgroundColor);

        AnimationManager.Tick(1f); // 1s >> 100ms — the tween completes
        Assert.Equal(Red, box.BackgroundColor);
    }

    [Fact]
    public void Reconciliation_ReappliesActiveStateOnRefreshedBase()
    {
        var live = Box.New().Background(Blue).OnHover(b => b.Background(Red));
        live.SetInteractionHovered(true);
        Assert.Equal(Red, live.BackgroundColor);

        // Rebuild with a different base color; patch carries the config over.
        var rebuilt = Box.New().Background(Green).OnHover(b => b.Background(Red));
        live.PatchFrom(rebuilt);
        live.RefreshInteractionAfterPatch();

        // Still hovered → override re-applied.
        Assert.Equal(Red, live.BackgroundColor);

        // Unhovering must fall back to the NEW base (green), not the old one.
        live.SetInteractionHovered(false);
        Assert.Equal(Green, live.BackgroundColor);
    }

    [Fact]
    public void NoOverrides_HoverIsRecorded_ButNothingIsRestyled()
    {
        var box = Box.New().Background(Blue);
        box.SetInteractionHovered(true); // no interaction style registered
        Assert.True(box.IsHovered);      // the fact is kept for every element (it once read false)
        Assert.Equal(Blue, box.BackgroundColor);

        box.BackgroundColor = Red;       // an app's own change survives the hover ending
        box.SetInteractionHovered(false);
        Assert.False(box.IsHovered);
        Assert.Equal(Red, box.BackgroundColor);
    }

    [Fact]
    public void Spring_StartsAtZeroEndsAtOne_AndOvershoots()
    {
        var spring = Easing.Spring(); // default under-damped

        Assert.Equal(0f, spring(0f), 3);
        Assert.Equal(1f, spring(1f), 3);

        bool overshot = false;
        for (float t = 0.05f; t < 1f; t += 0.02f)
            if (spring(t) > 1.0001f) overshot = true;
        Assert.True(overshot, "under-damped spring should overshoot 1 before settling");
    }
}
