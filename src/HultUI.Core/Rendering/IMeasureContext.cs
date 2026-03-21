using HultUI.Core.Primitives;
using HultUI.Core.Styling;

namespace HultUI.Core.Rendering;

public interface IMeasureContext
{
    Size MeasureText(string text, ResolvedInheritedStyle style, float maxWidth, int? maxLines);
}
