using HultUI.Core.Primitives;

namespace HultUI;

/// <summary>
/// Frame-damage instrumentation: what a damage-rect renderer would have repainted,
/// measured on the renderer we actually have. Exists to answer "are damage rects
/// worth building" with data (capability roadmap / external-review item 10) — the
/// windows that render today repaint fully, and the case for partial repaints rests
/// entirely on how often a rendered frame's changed region is small.
///
/// <para><b>Best-case framing.</b> Dirty flags propagate to the root, so "paint-dirty"
/// cannot distinguish an element that will repaint from one merely containing such an
/// element. The probe unions the <i>deepest</i> paint-dirty elements — the tightest
/// damage a rect-tracking renderer could hope to compute. Real damage would only be
/// larger (old bounds of moved elements, shadow/blur bleed, an ancestor that was
/// itself marked above a marked descendant). Numbers from this probe therefore
/// overstate, never understate, what damage rects could save.</para>
///
/// <para><b>Read <see cref="DamageFrame.UnionFraction"/> first.</b> The union of the
/// deepest dirty elements spans the container they sit in, so the single-rect
/// measure stays honest when a whole subtree is dirty. The summed per-rect area
/// does not — a repainting container contributes only its leaves, so
/// <see cref="DamageFrame.RectFraction"/> can be far below what even a perfect
/// multi-rect renderer would repaint. It is a floor, not an estimate.</para>
/// </summary>
public static class DamageProbe
{
    /// <summary>Per rendered frame, when attached. UI thread. Attaching costs every
    /// window one delegate null-check per frame; leave null outside probing runs.</summary>
    public static Action<DamageFrame>? OnFrame { get; set; }

    /// <summary>One rendered frame's best-case damage.</summary>
    /// <param name="WindowArea">Layout-unit area of the window.</param>
    /// <param name="UnionArea">Area of one bounding rect over every damage rect —
    /// what a single-rect damage implementation would repaint.</param>
    /// <param name="RectArea">Summed area of the individual damage rects, overlap
    /// uncorrected — approximates what a multi-rect implementation would repaint.</param>
    /// <param name="RectCount">How many deepest paint-dirty elements contributed.</param>
    /// <param name="LayoutDirty">The frame also moved things. Real damage would need
    /// the old bounds too, which nothing records — treat these frames' numbers as
    /// especially optimistic.</param>
    /// <param name="StyleDirty">The frame re-resolved styles.</param>
    public readonly record struct DamageFrame(
        float WindowArea,
        float UnionArea,
        float RectArea,
        int RectCount,
        bool LayoutDirty,
        bool StyleDirty)
    {
        /// <summary>Single-rect damage as a fraction of the window (0..1).</summary>
        public float UnionFraction => WindowArea <= 0 ? 0 : Math.Min(1f, UnionArea / WindowArea);

        /// <summary>Multi-rect damage as a fraction of the window (0..1).</summary>
        public float RectFraction => WindowArea <= 0 ? 0 : Math.Min(1f, RectArea / WindowArea);
    }
}
