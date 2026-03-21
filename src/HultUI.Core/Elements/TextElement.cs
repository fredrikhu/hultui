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
        }
    }

    public TextOverflow Overflow { get; set; } = TextOverflow.Clip;
    public int? MaxLines { get; set; }

    public static TextElement New(string content) => new() { _content = content };

    internal override Size MeasureContent(float availableWidth, float availableHeight, IMeasureContext ctx) =>
        ctx.MeasureText(Content, ResolvedInherited, availableWidth, MaxLines);

    public override void Render(IRenderContext ctx)
    {
        if (string.IsNullOrEmpty(Content)) return;
        ctx.DrawText(Content, Layout.AbsoluteContentBox, ResolvedInherited, Overflow, MaxLines);
    }
}
