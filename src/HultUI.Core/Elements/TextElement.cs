using HultUI.Core.Accessibility;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;

namespace HultUI.Core.Elements;

public class TextElement : Element
{
    private string _content = "";

    public string Content
    {
        get => _content;
        set
        {
            if (_content == value) return;
            _content = value;
            MarkLayoutDirty();
            MarkPaintDirty();
            // Text content is this element's accessible name (and, when it labels
            // a button, the button's name-from-content), so a change is an
            // a11y-visible property change.
            NotifyAccessibilityPropertyChanged(AccessibilityProperties.Name);
        }
    }

    public TextOverflow Overflow { get; set; } = TextOverflow.Clip;
    public int? MaxLines { get; set; }

    public static TextElement New(string content) => new() { _content = content };

    internal override void PatchFrom(Element source)
    {
        base.PatchFrom(source);
        if (source is TextElement text)
        {
            Content = text.Content;
            Overflow = text.Overflow;
            MaxLines = text.MaxLines;
        }
    }

    internal override Size MeasureContent(float availableWidth, float availableHeight, IMeasureContext ctx) =>
        ctx.MeasureText(Content, ResolvedInherited, availableWidth, MaxLines);

    public override void Render(IRenderContext ctx)
    {
        if (string.IsNullOrEmpty(Content)) return;
        ctx.DrawText(Content, Layout.AbsoluteContentBox, ResolvedInherited, Overflow, MaxLines);
    }
}
