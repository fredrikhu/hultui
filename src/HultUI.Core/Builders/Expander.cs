using HultUI.Core.Elements;

namespace HultUI.Core.Builders;

public static class Expander
{
    /// <summary>
    /// A titled header that shows and hides <paramref name="content"/> below it. Starts
    /// closed and keeps its own state; see <see cref="ExpanderElement.Expanded"/> to hand the
    /// state to the app.
    /// </summary>
    public static ExpanderElement New(string title, Element? content = null,
        [OriginFile] string? file = null, [OriginLine] int line = 0)
    {
        var expander = new ExpanderElement { Title = title }.WithOrigin(file, line);
        expander.SetContent(content);
        return expander;
    }

    /// <summary>
    /// As <see cref="New(string, Element?, string?, int)"/>, with a body built only while it is
    /// open — for content that costs something to build and is usually closed.
    /// </summary>
    public static ExpanderElement New(string title, Func<Element> content,
        [OriginFile] string? file = null, [OriginLine] int line = 0)
    {
        var expander = new ExpanderElement { Title = title }.WithOrigin(file, line);
        expander.SetContent(content);
        return expander;
    }
}
