using HultUI.Core.Elements;
using HultUI.Core.Rendering;
using HultUI.Core.RichText;
using HultUI.Core.Styling;

namespace HultUI.Core.Builders;

/// <summary>A rich text editor (docs/plans/16-rich-text-editor.md); see <see cref="RichTextEditorElement"/>.</summary>
public static class RichTextEditor
{
    public static RichTextEditorElement New(RichDocument? document = null,
        [OriginFile] string? file = null, [OriginLine] int line = 0) =>
        RichTextEditorElement.New(document, file, line);
}

public static class RichTextEditorExtensions
{
    /// <summary>Takes each new document an edit makes; hand it back on the next build.</summary>
    public static RichTextEditorElement OnDocumentChanged(this RichTextEditorElement el, Action<RichDocument> handler)
    { el.DocumentChanged = handler; return el; }

    /// <summary>When the selection or the marks typing would take change — a toolbar's cue.</summary>
    public static RichTextEditorElement OnSelectionChanged(this RichTextEditorElement el, Action handler)
    { el.SelectionChanged = handler; return el; }

    public static RichTextEditorElement OnLinkActivated(this RichTextEditorElement el, Action<string> handler)
    { el.LinkActivated = handler; return el; }

    /// <summary>Ctrl+K: show a link editor.</summary>
    public static RichTextEditorElement OnLinkRequested(this RichTextEditorElement el, Action handler)
    { el.LinkRequested = handler; return el; }

    /// <summary>Where pictures' pixels come from: the app's loader, asked with each picture's
    /// source on every build. Null, or a source not loaded yet, draws a line naming the picture.</summary>
    public static RichTextEditorElement Images(this RichTextEditorElement el, Func<string, IImageSource?> resolve)
    { el.ImageResolver = resolve; return el; }

    /// <summary>The toolbar's Picture button and the context menu's Change picture: show a
    /// picker, then <see cref="RichEditState.InsertImage"/> or <see cref="RichEditState.SetImage"/>.</summary>
    public static RichTextEditorElement OnImageRequested(this RichTextEditorElement el, Action handler)
    { el.ImageRequested = handler; return el; }

    public static RichTextEditorElement Features(this RichTextEditorElement el, RichTextFeatures features)
    { el.Features = features; return el; }

    public static RichTextEditorElement ReadOnly(this RichTextEditorElement el, bool value = true)
    { el.ReadOnly = value; return el; }

    public static RichTextEditorElement PlaceholderText(this RichTextEditorElement el, string text)
    { el.Placeholder = text; return el; }

    /// <summary>As tall as the document between <paramref name="min"/> and <paramref name="max"/>
    /// — the editor's height, padding and border included; it scrolls past the maximum.</summary>
    public static RichTextEditorElement AutoHeight(this RichTextEditorElement el, float min, float max = float.PositiveInfinity)
    { el.AutoHeight = (min, max); return el; }

    /// <summary>A reusable look for the field, layered over the theme's; see <see cref="TextInputStyle"/>.</summary>
    public static RichTextEditorElement Style(this RichTextEditorElement el, TextInputStyle? style)
    { el.Style = style; return el; }
}
