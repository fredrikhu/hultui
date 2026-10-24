using HultUI.Core.Accessibility;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Platform;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.RichText;
using HultUI.Core.Styling;

namespace HultUI.Core.Elements;

/// <summary>
/// A rich text editor: a <see cref="RichDocument"/> of paragraphs, headings, lists, quotes, code,
/// pictures and tables, edited in place (docs/plans/16-rich-text-editor.md). With
/// <see cref="ReadOnly"/> it is the viewer — selectable, copyable, its links clickable. Pictures'
/// pixels come from the app (<see cref="ImageResolver"/>).
/// </summary>
/// <remarks>
/// <para>The editing rules live in <see cref="State"/>, which this element drives from the
/// keyboard, the pointer and the input method and draws. A toolbar calls the same commands on
/// <see cref="State"/> and reads <see cref="RichEditState.SelectionMarks"/> to show what is on.</para>
/// <para>The document is a controlled value: hand one in, take each new one from
/// <see cref="DocumentChanged"/>, and hand that back on the next build. A document handed in that
/// is neither the last one handed in nor the one being edited replaces it, and forgets the undo
/// history — it is someone else's text now.</para>
/// <para>Each block is laid out once per width as a <see cref="TextParagraph"/> and kept until
/// the block changes; blocks are immutable, so "changed" is a reference compare. Only the blocks
/// on screen are drawn.</para>
/// </remarks>
public partial class RichTextEditorElement : BoxElement, IScrollbarDraggable, IAutoScrollable, ITextServicesClient
{
    private readonly RichEditState _state;
    private RichDocument _lastIncoming;
    private bool _loading;
    private bool _focused;
    private CompositionState _composition;

    public RichTextEditorElement() : this(RichDocument.Empty) { }

    public RichTextEditorElement(RichDocument document)
    {
        _state = new RichEditState(document);
        _lastIncoming = document;
        _state.DocumentChanged += OnStateDocumentChanged;
        _state.SelectionChanged += OnStateSelectionChanged;

        IsFocusable = true;
        Role = AccessibilityRole.TextInput;
        Box.Padding = new Thickness(6, 10, 6, 10);
        Inherited.Cursor = CursorType.Text;

        OnOwn<FocusEvent>(_ => { _focused = true; MarkPaintDirty(); });
        OnOwn<BlurEvent>(_ =>
        {
            _focused = false;
            if (_composition.IsActive)
            {
                _composition.Clear();
                TextInputHost?.ClearComposition();
            }
            MarkPaintDirty();
        });
        OnOwn<TextInputEvent>(OnTextInput);
        OnOwn<TextEditingEvent>(OnTextEditing);
        OnOwn<KeyDownEvent>(OnKeyDown);
        OnOwn<PointerDownEvent>(OnPointerDown);
        OnOwn<PointerMoveEvent>(OnPointerMove);
        OnOwn<PointerUpEvent>(OnPointerUp);
        OnOwn<ScrollEvent>(OnScroll);

        ContextMenuItems = BuildContextMenuItems;
    }

    public static RichTextEditorElement New(RichDocument? document = null,
        [OriginFile] string? file = null, [OriginLine] int line = 0) =>
        new RichTextEditorElement(document ?? RichDocument.Empty).WithOrigin(file, line);

    /// <summary>The editing engine: the document, the selection and every command.</summary>
    public RichEditState State => _state;

    /// <summary>The document. Setting one that differs from what is being edited replaces it and
    /// forgets the undo history.</summary>
    public RichDocument Document
    {
        get => _state.Document;
        set
        {
            _lastIncoming = value;
            if (ReferenceEquals(value, _state.Document)) return;
            _loading = true;
            try { _state.Load(value); }
            finally { _loading = false; }
        }
    }

    /// <summary>Runs with each new document an edit makes — not with one handed in.</summary>
    public Action<RichDocument>? DocumentChanged { get; set; }

    /// <summary>Runs when the selection or the marks typing would take change: when a toolbar
    /// should look again.</summary>
    public Action? SelectionChanged { get; set; }

    /// <summary>Runs with a link's target when it is followed: a click in a read-only editor,
    /// Ctrl+click in an editable one, or the context menu's Open link.</summary>
    public Action<string>? LinkActivated { get; set; }

    /// <summary>What a person may make here; see <see cref="RichTextFeatures"/>.</summary>
    public RichTextFeatures Features
    {
        get => _state.Features;
        set
        {
            if (_state.Features == value) return;
            _state.Features = value;
            MarkPaintDirty();
        }
    }

    /// <summary>The viewer: the document can be selected and copied, links open on a plain
    /// click, nothing can change it.</summary>
    public bool ReadOnly
    {
        get => _readOnly;
        set
        {
            if (_readOnly == value) return;
            _readOnly = value;
            MarkPaintDirty();
            // The text a reader reads says whether it may be changed (UIA's IsReadOnly text
            // attribute): its version holds it, and the window puts the new version on the applied
            // tree, as for a caret move, telling nothing. This marked paint only, so a reader was
            // answered the old state until something else rebuilt the tree.
            NotifyAccessibilityPropertyChanged(AccessibilityProperties.TextSelection);
        }
    }
    private bool _readOnly;

    public string Placeholder { get; set; } = "";

    /// <summary>
    /// When set, the editor is as tall as its document within these bounds and scrolls only past
    /// the maximum — a form field that grows as it is filled. Unset, it takes whatever height
    /// layout gives it and scrolls inside that.
    /// </summary>
    public (float Min, float Max)? AutoHeight
    {
        get => _autoHeight;
        set
        {
            if (_autoHeight == value) return;
            _autoHeight = value;
            MarkLayoutDirty();
        }
    }
    private (float Min, float Max)? _autoHeight;

    public IClipboard? Clipboard { get; set; }
    public ITextInputHost? TextInputHost { get; set; }

    /// <summary>The field's look, layered over the theme's — the same <see cref="TextInputStyle"/>
    /// a text area takes. Compared by reference, so hand the same instance each build.</summary>
    public TextInputStyle? Style
    {
        get => _style;
        set
        {
            if (ReferenceEquals(_style, value)) return;
            _style = value;
            MarkStyleDirty();
        }
    }
    private TextInputStyle? _style;

    // Colours resolved from the text field theme, as TextArea resolves them.
    public Color PlaceholderColor { get; set; } = new(150, 150, 150, 255);
    public Color SelectionColor { get; set; } = new(66, 135, 245, 80);
    public Color CursorColor { get; set; } = new(0, 0, 0, 255);
    public Color FocusBorderColor { get; set; } = new(66, 135, 245, 255);
    public Color? UnderlineColor { get; set; }
    public Color? FocusUnderlineColor { get; set; }
    public float UnderlineThickness { get; set; } = 1f;
    public float FocusUnderlineThickness { get; set; } = 2f;
    public Color? FocusBackground { get; set; }
    public float CursorWidth { get; set; } = 1.5f;
    public float ScrollSpeed { get; set; } = 40f;

    /// <summary>Whether the input method is composing: its text is drawn at the caret but is not
    /// in the document until committed.</summary>
    public bool IsComposing => _composition.IsActive;
    public string CompositionText => _composition.Text ?? "";

    protected internal override void ApplyTheme(Theme theme)
    {
        var ti = theme.TextInput;
        var s = _style;
        CornerRadius = s?.CornerRadius ?? ti.CornerRadius;
        Box.Padding = s?.Padding ?? new Thickness(
            Math.Max(ti.Padding.Top, 6), ti.Padding.Right, Math.Max(ti.Padding.Bottom, 6), ti.Padding.Left);
        Box.BorderWidth = s?.BorderWidth ?? ti.BorderWidth;
        Inherited.FontSize ??= s?.FontSize ?? ti.FontSize;
        BackgroundColor = s?.Background ?? ti.Background ?? theme.Background;
        BorderColor = s?.Border ?? ti.BorderColor ?? theme.Border;
        FocusBorderColor = s?.FocusBorder ?? ti.FocusBorderColor ?? theme.Primary;
        UnderlineColor = s?.Underline ?? ti.UnderlineColor;
        FocusUnderlineColor = s?.FocusUnderline ?? ti.FocusUnderlineColor;
        UnderlineThickness = s?.UnderlineThickness ?? ti.UnderlineThickness;
        FocusUnderlineThickness = s?.FocusUnderlineThickness ?? ti.FocusUnderlineThickness;
        FocusBackground = s?.FocusBackground ?? ti.FocusBackground;
        PlaceholderColor = s?.Placeholder ?? ti.PlaceholderColor ?? theme.Secondary;
        SelectionColor = s?.Selection ?? ti.SelectionColor ?? new Color(theme.Primary.R, theme.Primary.G, theme.Primary.B, 80);
        CursorColor = s?.Cursor ?? ti.CursorColor ?? theme.TextColor;
        ApplyRichTheme(theme);
    }

    // ── Engine events ──

    private void OnStateDocumentChanged()
    {
        _needsScrollToCaret = true;
        _preferredX = -1;
        _upstream = false;
        if (_autoHeight is not null) MarkLayoutDirty();
        MarkPaintDirty();
        NotifyAccessibilityPropertyChanged(AccessibilityProperties.Value);
        BindSearch();
        if (!_loading) DocumentChanged?.Invoke(_state.Document);
    }

    private void OnStateSelectionChanged()
    {
        _needsScrollToCaret = true;
        MarkPaintDirty();
        // A reader follows the caret: the window puts the text's new version on the applied tree,
        // and the bridge tells it moved. No tree is built for it (plan 17, round 4).
        if (AccessibilityClients.Active) NotifyAccessibilityPropertyChanged(AccessibilityProperties.TextSelection);
        SelectionChanged?.Invoke();
    }

    // ── Accessibility ──

    internal override string? AccessibleValue => _state.Document.ToPlainText();

    // ── Reconciliation ──

    internal override void PatchFrom(Element source)
    {
        base.PatchFrom(source);
        if (source is not RichTextEditorElement e) return;

        Placeholder = e.Placeholder;
        Style = e.Style;
        Features = e.Features;
        ReadOnly = e.ReadOnly;
        AutoHeight = e.AutoHeight;
        DocumentChanged = e.DocumentChanged;
        SelectionChanged = e.SelectionChanged;
        LinkActivated = e.LinkActivated;
        LinkRequested = e.LinkRequested;
        ImageRequested = e.ImageRequested;
        // Set every build, changed or not: a picture that has arrived since is drawn now.
        ImageResolver = e.ImageResolver;
        Search = e.Search;
        RefreshSearchTints();
        MiddleClickAutoScroll = e.MiddleClickAutoScroll;
        CursorWidth = e.CursorWidth;
        ScrollSpeed = e.ScrollSpeed;

        // A rebuild hands back the document it was given. Only a different one — not the last
        // one handed in, and not the one being edited — replaces what is here.
        var incoming = e._lastIncoming;
        if (!ReferenceEquals(incoming, _lastIncoming))
            Document = incoming;
    }
}

/// <summary>
/// An element that edits text and so needs the window's clipboard and input method: the window
/// hands both over as it takes focus.
/// </summary>
internal interface ITextServicesClient
{
    IClipboard? Clipboard { get; set; }
    ITextInputHost? TextInputHost { get; set; }
}
