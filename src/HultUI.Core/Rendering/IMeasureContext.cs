using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;

namespace HultUI.Core.Rendering;

public interface IMeasureContext
{
    /// <summary>
    /// Measures <paramref name="text"/> as it would be laid out in a box
    /// <paramref name="maxWidth"/> wide.
    ///
    /// <para><paramref name="overflow"/> must match what the caller will draw with:
    /// the modes disagree about what happens to text that does not fit, so measuring
    /// under one and drawing under the other reports a size the element never takes.
    /// It defaults to <see cref="TextOverflow.Clip"/> because that is what
    /// <c>DrawText</c> defaults to, and because at an unbounded width — how most
    /// callers here measure — nothing is truncated and the mode cannot matter.</para>
    /// </summary>
    Size MeasureText(string text, ResolvedInheritedStyle style, float maxWidth, int? maxLines,
        TextOverflow overflow = TextOverflow.Clip);
}
