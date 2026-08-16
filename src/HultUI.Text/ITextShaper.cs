namespace HultUI.Text;

public interface ITextShaper
{
    /// <summary>
    /// Shapes one directional run. <paramref name="rtl"/> sets the shaping
    /// direction explicitly — the pipeline resolves direction per UAX #9 and never
    /// lets the shaper guess, because a guess is per-buffer and mixed-direction
    /// text needs it per-run (docs/plans/14-bidi.md).
    /// </summary>
    ShapedRun Shape(string text, IResolvedFont font, float fontSize, bool rtl = false);
}
