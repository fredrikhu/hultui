using Aperture.Theme;

namespace Aperture;

/// <summary>
/// What a build takes from the window's size. Nearly everything on screen follows the window
/// through layout, so a window dragged through its widths has nothing to build for most of them;
/// each step used to build the whole window, about 70 KB, to find that out.
///
/// <para>Each build notes what it read off the size: every card width it asked
/// <see cref="VisibleCards"/> about (the walk's columns, a shelf's pages), and a hero's box and
/// the artwork step that sizes its picture. A resize works the same out at the new size and builds
/// only when an answer changed. A new reader of the window's size belongs here, or a window that
/// stops at that size shows the last build's answer; WindowFitTests walks the views through the
/// widths against a full build.</para>
///
/// <para>The library's height needs no build: the rows a taller window brings in are made as they
/// come into view, and each asks for the next page (<see cref="GridRowFor"/>). What a build did
/// for a shorter one, a cursor kept on screen, is done without it.</para>
/// </summary>
public sealed partial class ApertureApp
{
    private float _fitWidth, _fitHeight;
    private bool _fitFullscreen;

    /// <summary>The card widths the last build asked <see cref="VisibleCards"/> about.</summary>
    private readonly List<float> _fitCardWidths = [];

    /// <summary>Whether the last build drew a hero, and the shape of its picture.</summary>
    private bool _fitHero;
    private double? _fitHeroAspect;

    /// <summary>The library grid's row height, when the last build built one.</summary>
    private float _fitGridStride;

    private void StartWindowFit()
    {
        _fitWidth = _window.LayoutWidth;
        _fitHeight = _window.LayoutHeight;
        _fitFullscreen = _window.IsFullscreen;
        _fitCardWidths.Clear();
        _fitHero = false;
        _fitHeroAspect = null;
        _fitGridStride = 0f;
    }

    /// <summary>Whether a build at the window's size now would read anything else off it than the
    /// last build did.</summary>
    private bool WindowFitChanged()
    {
        float width = _window.LayoutWidth, height = _window.LayoutHeight;
        if (_window.IsFullscreen != _fitFullscreen) return true;
        foreach (float cardWidth in _fitCardWidths)
            if (CardsAcross(cardWidth, width) != CardsAcross(cardWidth, _fitWidth)) return true;
        return _fitHero && (HeroHeightAt(height) != HeroHeightAt(_fitHeight)
            || HeroAsk(_fitHeroAspect, width, height) != HeroAsk(_fitHeroAspect, _fitWidth, _fitHeight));
    }

    /// <summary>What a build of the library does for a window that changed under the cursor.</summary>
    private void FollowWindowWithoutBuild()
    {
        if (_fitGridStride > 0f) RevealGridCursor(_cursorPerRow, _fitGridStride);
    }

    private static int CardsAcross(float cardWidth, float windowWidth)
    {
        float content = windowWidth - Halide.RailWidth - Halide.S5 * 2;
        return Math.Max(1, (int)((content + Halide.PosterGap) / (cardWidth + Halide.PosterGap)));
    }
}
