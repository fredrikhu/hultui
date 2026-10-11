using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;

namespace HultUI.Core.Elements;

/// <summary>What a toast is reporting; draws a coloured bar at the pill's leading edge.</summary>
public enum ToastSeverity : byte
{
    /// <summary>A plain notice, no bar.</summary>
    None,
    Success,
    Warning,
    Error,
}

/// <summary>
/// The pill a transient notice is shown in: a message, an optional severity bar, and
/// an optional action verb at the end. Built by <see cref="Builders.ToastHost"/>; the
/// host owns the timing and the fade, the element owns the look.
/// </summary>
/// <remarks>
/// <para>A box with real text children rather than a self-drawn pill, so the message is
/// a <see cref="TextElement"/> like any other — findable by text, selectable, and
/// inspectable. The element manages those children itself; a rebuild patches the
/// message and verb onto them rather than reconciling them.</para>
///
/// <para>With an action the whole pill is the button — the verb is drawn at the end,
/// but a target the size of the pill is easier to hit in the second or two the toast is
/// up, and it gives assistive technology one invokable node named by the verb, with the
/// message as its description. Without one it is an alert. Either way the message is
/// announced (<see cref="Element.Announce"/>) as the toast appears.</para>
/// </remarks>
public class ToastElement : BoxElement
{
    private readonly BoxElement _accent = new();
    private readonly TextElement _text = TextElement.New("");
    private readonly TextElement _action = TextElement.New("");
    private string _message = "";
    private string? _actionLabel;
    private ToastSeverity _severity;

    private Color _actionColor = new(79, 70, 229);
    private Color _success = new(34, 197, 94);
    private Color _warning = new(245, 158, 11);
    private Color _error = new(220, 38, 38);
    private float _accentWidth = 3f;
    private float _accentGap = 10f;
    private float _actionGap = 12f;

    public string Message
    {
        get => _message;
        set { if (_message == value) return; _message = value; _text.Content = value; Describe(); }
    }

    /// <summary>The verb drawn at the end, if the toast offers one.</summary>
    public string? ActionLabel
    {
        get => _actionLabel;
        set { if (_actionLabel == value) return; _actionLabel = value; _action.Content = value ?? ""; Describe(); Arrange(); }
    }

    /// <summary>Runs when the pill is clicked, if there is an action.</summary>
    public Action? OnAction { get; set; }

    public ToastSeverity Severity
    {
        get => _severity;
        set { if (_severity == value) return; _severity = value; Arrange(); }
    }

    internal override bool ManagesOwnChildren => true;

    public ToastElement()
    {
        Flex.Direction = FlexDirection.Row;
        Flex.AlignItems = AlignItems.Center;
        _accent.Flex.AlignSelf = AlignSelf.Stretch;
        _accent.Flex.FlexShrink = 0;
        _text.MaxLines = 1;
        _action.MaxLines = 1;
        Describe();
        Arrange();
        OnOwn<ClickEvent>(evt =>
        {
            if (OnAction is null) return;
            evt.StopPropagation = true;
            OnAction();
        });
    }

    public static ToastElement New(string message, ToastSeverity severity = ToastSeverity.None,
        [OriginFile] string? file = null, [OriginLine] int line = 0) =>
        new ToastElement { Message = message, Severity = severity }.WithOrigin(file, line);

    private void Describe()
    {
        bool actionable = _actionLabel is not null;
        Role = actionable ? AccessibilityRole.Button : AccessibilityRole.Alert;
        AccessibleName = actionable ? _actionLabel : _message;
        AccessibleDescription = actionable ? _message : null;
        Inherited.Cursor = actionable ? CursorType.Pointer : CursorType.Default;
    }

    private Color? AccentColor => _severity switch
    {
        ToastSeverity.Success => _success,
        ToastSeverity.Warning => _warning,
        ToastSeverity.Error => _error,
        _ => null,
    };

    // Which children are in the row, in order: [bar] message [verb]. Spacing is on the
    // children themselves, since the gap differs on the two sides of the message.
    private void Arrange()
    {
        StyleParts();
        var children = new List<Element>(3);
        if (AccentColor is not null) children.Add(_accent);
        children.Add(_text);
        if (_actionLabel is not null) children.Add(_action);
        ReplaceChildren([.. children]);
    }

    // The bar's and the verb's look; which of them are in the row is Arrange's.
    private void StyleParts()
    {
        if (AccentColor is { } accent)
        {
            _accent.Box.Width = Dimension.Px(_accentWidth);
            _accent.CornerRadius(_accentWidth / 2);
            _accent.Background(accent);
            _accent.Box.Margin = new Thickness(0, _accentGap, 0, 0);
        }
        if (_actionLabel is not null)
        {
            _action.Box.Margin = new Thickness(0, 0, 0, _actionGap);
            _action.Inherited.TextColor = _actionColor;
            _action.Inherited.FontWeight = FontWeight.SemiBold;
        }
    }

    protected internal override void ApplyTheme(Theme theme)
    {
        var t = theme.Toast;
        var border = new Thickness(t.BorderWidth);
        // What the row is laid out with: its border and padding, the bar's width and gap, the
        // verb's gap (the text sizes go through the style pass). A theme that keeps them (a
        // light/dark switch) lays nothing out; and which parts are in the row is not the theme's
        // to change, so the row is not put together again either, which also told the
        // accessibility tree that its children had changed.
        bool resized = BoxRead.BorderWidth != border || BoxRead.Padding != t.Padding
            || t.AccentWidth != _accentWidth || t.AccentGap != _accentGap || t.ActionGap != _actionGap;
        this.Background(t.Background ?? theme.Surface);
        this.BorderColor(t.Border ?? theme.Border);
        Box.BorderWidth = border;
        this.CornerRadius(t.CornerRadius);
        Box.Padding = t.Padding;
        _text.Inherited.FontSize = t.FontSize;
        _text.Inherited.TextColor = t.TextColor ?? theme.TextColor;
        _action.Inherited.FontSize = t.FontSize;
        _actionColor = t.ActionColor ?? theme.Primary;
        _success = t.Success;
        _warning = t.Warning;
        _error = t.Error ?? theme.Danger;
        _accentWidth = t.AccentWidth;
        _accentGap = t.AccentGap;
        _actionGap = t.ActionGap;
        StyleParts();
        // The children resolve after this element in the same style pass, but only if
        // they are flagged; their inherited style was just written.
        _accent.MarkStyleDirty();
        _text.MarkStyleDirty();
        _action.MarkStyleDirty();
        // The bar's width and the gaps are read from the parts' styles as the row lays them out.
        if (resized) MarkLayoutDirty();
    }

    internal override void PatchFrom(Element source)
    {
        base.PatchFrom(source);
        if (source is not ToastElement t) return;
        bool replaced = _message != t._message;
        OnAction = t.OnAction;
        Message = t._message;
        ActionLabel = t._actionLabel;
        Severity = t._severity;
        if (replaced && IsHosted) AnnounceMessage();
    }

    protected override void OnMounted()
    {
        base.OnMounted();
        AnnounceMessage();
    }

    /// <summary>A toast appears where the user is not looking, and a reader would not find it in
    /// the second or two it is up: it is spoken, an error at once, anything else when the reader
    /// is done with what it is saying.</summary>
    private void AnnounceMessage()
    {
        if (_message.Length > 0)
            Announce(_message, _severity == ToastSeverity.Error ? AnnouncementPoliteness.Assertive : AnnouncementPoliteness.Polite);
    }
}
