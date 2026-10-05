using HultUI.Core.Accessibility;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;

namespace HultUI.Core.Builders;

/// <summary>Which side of its anchor a <see cref="Popover"/> opens on.</summary>
public enum PopoverPlacement : byte
{
    Below,
    Above,
    Right,
    Left,
}

/// <summary>How a <see cref="Popover"/> lines up with its anchor along that side: by the
/// anchor's start edge (left, or top), its centre, or its end edge.</summary>
public enum PopoverAlignment : byte
{
    Start,
    Center,
    End,
}

/// <summary>
/// A card of any content that opens next to the element it belongs to — a filter panel under
/// a toolbar button, a list of recent folders under a caret — and closes on a press outside it
/// or Esc. Not modal: the rest of the window stays live.
/// </summary>
/// <remarks>
/// <para><b>Use.</b> Hold one per anchor, and toggle it from the anchor's click:
/// <c>button.OnClick(e => _filters.Toggle(e.CurrentTarget))</c>. The content function is asked
/// when it opens and again on every <see cref="Refresh"/> (and whenever reactive state it reads
/// changes), and the result is patched into the open card, so what is typed in it survives.</para>
///
/// <para><b>Placement.</b> On <see cref="Placement"/>'s side, lined up by
/// <see cref="Alignment"/>, a theme gap away. It flips to the other side when it does not fit
/// and fits better there, slides along to stay in the window, and is placed again every frame,
/// so it follows its anchor when the page scrolls or the window changes size. If the anchor
/// leaves the window — a rebuild that dropped it — the popover closes.</para>
///
/// <para><b>Closing.</b> A press anywhere outside closes it, and goes on to whatever was
/// pressed; a press on its own anchor only closes it, so the toggle does not reopen it. Esc
/// closes it. So does <see cref="Close"/>, which a row in it calls once it has done its work.
/// <see cref="Closed"/> runs however it closed.</para>
///
/// <para><b>Keyboard.</b> Opening moves the keyboard into it — the first control, or the card
/// itself when it has none — and Tab then stays inside. Closing with the keyboard inside hands it
/// back to what had it, normally the anchor. <see cref="ArrowKeys"/> lets ↑ ↓ walk the controls
/// too, for a list of rows.</para>
///
/// <para><b>Accessibility.</b> The card is a dialog named by <see cref="AccessibleName"/>, or
/// else by its anchor. The anchor reports expanded while it is open; a build that makes the
/// anchor again should say so too, with <c>.AccessibleExpanded(popover.IsOpen)</c>, or the
/// rebuild resets it.</para>
/// </remarks>
public sealed class Popover
{
    private readonly Func<Element> _content;
    private View? _view;
    private ComponentHost? _host;
    private IOverlayHost? _overlayHost;
    private OverlayHandle _handle;
    private Element? _anchor;
    private Point _lastPosition;
    private bool _closing;

    public Popover(Func<Element> content)
    {
        _content = content ?? throw new ArgumentNullException(nameof(content));
    }

    /// <summary>The side to open on, when it fits there. Below by default.</summary>
    public PopoverPlacement Placement { get; set; } = PopoverPlacement.Below;
    /// <summary>How to line up with the anchor along that side. Start by default.</summary>
    public PopoverAlignment Alignment { get; set; } = PopoverAlignment.Start;
    /// <summary>What a screen reader calls the card; null takes the anchor's name.</summary>
    public string? AccessibleName { get; set; }
    /// <summary>The card's automation id.</summary>
    public string AutomationId { get; set; } = "popover";
    /// <summary>Whether ↑ and ↓ move the keyboard between the controls in it, as Tab does.</summary>
    public bool ArrowKeys { get; set; }

    /// <summary>Runs when it closes, however it closed.</summary>
    public Action? Closed { get; set; }

    public bool IsOpen => _handle.IsValid;
    /// <summary>The element it is open against, while it is open.</summary>
    public Element? Anchor => _anchor;
    /// <summary>The side it went on last: <see cref="Placement"/>, or the other one if it flipped.</summary>
    public PopoverPlacement ActualPlacement { get; private set; }
    /// <summary>The card, while it is open.</summary>
    public Element? Card => _host?.Children.Count > 0 ? _host.Children[0] : null;

    /// <summary>Opens against <paramref name="anchor"/>, which must be on screen. Already open
    /// against another anchor, it moves.</summary>
    public void Open(Element anchor)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        if (IsOpen)
        {
            if (ReferenceEquals(anchor, _anchor)) return;
            Close();
        }
        if (anchor.OverlayHost is not { } overlayHost) return;

        _anchor = anchor;
        _overlayHost = overlayHost;
        _closing = false;
        _view = new View(this);
        _host = _view.Mount();
        _host.On<KeyDownEvent>(OnKeyDown);
        _handle = overlayHost.ShowAnchoredOverlay(_host, anchor, Place, dismissOnOutsideClick: true, onDismissed: OnDismissed);

        anchor.AccessibleExpanded = true;
        anchor.NotifyAccessibilityPropertyChanged(AccessibilityProperties.IsExpanded);

        // The keyboard goes in: to the first control, or to the card so Esc reaches it.
        if (FocusTraversal.FindNext(_host, null, wrap: false) is { } first)
            first.Focus();
        else if (Card is { } card)
        {
            card.IsFocusable = true;
            card.Focus();
        }
    }

    /// <summary>Opens against <paramref name="anchor"/>, or closes when it is already open
    /// against it.</summary>
    public void Toggle(Element anchor)
    {
        if (IsOpen && ReferenceEquals(anchor, _anchor)) Close();
        else Open(anchor);
    }

    public void Close()
    {
        if (!IsOpen) return;
        var handle = _handle;
        _handle = default;
        _overlayHost?.CloseOverlay(handle);
        Finish();
    }

    /// <summary>Asks the content function again and patches the result into the open card.</summary>
    public void Refresh() => _view?.Refresh();

    private void OnDismissed()
    {
        // The host has already taken the overlay away.
        _handle = default;
        Finish();
    }

    private void Finish()
    {
        if (_anchor is { } anchor)
        {
            anchor.AccessibleExpanded = false;
            anchor.NotifyAccessibilityPropertyChanged(AccessibilityProperties.IsExpanded);
        }
        _anchor = null;
        _overlayHost = null;
        _view = null;
        _host = null;
        Closed?.Invoke();
    }

    private void OnKeyDown(KeyDownEvent e)
    {
        if (e.Key == KeyCode.Escape && (e.Modifiers & ChordModifiers) == 0)
        {
            Close();
            e.StopPropagation = true;
            return;
        }
        if (ArrowKeys && _host is { } host && (e.Modifiers & ChordModifiers) == 0
            && e.Key is KeyCode.Down or KeyCode.Up)
        {
            var current = host.FocusManager?.FocusedElement;
            var next = e.Key == KeyCode.Down
                ? FocusTraversal.FindNext(host, current)
                : FocusTraversal.FindPrevious(host, current);
            next?.Focus();
            e.StopPropagation = true;
        }
    }

    private const KeyModifiers ChordModifiers = KeyModifiers.Shift | KeyModifiers.Ctrl | KeyModifiers.Alt | KeyModifiers.Super;

    /// <summary>Asked by the host every frame the popover is open.</summary>
    private Point Place(Size size, Size window)
    {
        if (_anchor is not { IsHosted: true } anchor)
        {
            // The anchor went with a rebuild. Closing here would change the window's overlays
            // while it is drawing them, so it waits for the frame to finish.
            if (!_closing && _overlayHost is { } host)
            {
                _closing = true;
                host.RequestPostFrameCallback(Close);
            }
            return _lastPosition;
        }
        var card = Card as CardElement;
        var (at, side) = Position(anchor.WindowBorderBox, size, window, Placement, Alignment,
            card?.Gap ?? 6f, card?.WindowMargin ?? 8f);
        ActualPlacement = side;
        _lastPosition = at;
        return at;
    }

    /// <summary>
    /// Where a card of <paramref name="size"/> goes against <paramref name="anchor"/> in a window of
    /// <paramref name="window"/>, and on which side. It takes the preferred side, or the opposite
    /// one when the card does not fit and the opposite has more room; slides along the anchor to
    /// stay <paramref name="margin"/> inside the window; and, fitting on neither side, overlaps
    /// the anchor rather than leave the window. Whole pixels, so its text is not blurred.
    /// </summary>
    internal static (Point At, PopoverPlacement Side) Position(Rect anchor, Size size, Size window,
        PopoverPlacement preferred, PopoverAlignment alignment, float gap, float margin)
    {
        float Room(PopoverPlacement side) => side switch
        {
            PopoverPlacement.Below => window.Height - margin - (anchor.Bottom + gap),
            PopoverPlacement.Above => anchor.Y - gap - margin,
            PopoverPlacement.Right => window.Width - margin - (anchor.Right + gap),
            _ => anchor.X - gap - margin,
        };

        var side = preferred;
        bool vertical = side is PopoverPlacement.Below or PopoverPlacement.Above;
        float need = vertical ? size.Height : size.Width;
        var opposite = side switch
        {
            PopoverPlacement.Below => PopoverPlacement.Above,
            PopoverPlacement.Above => PopoverPlacement.Below,
            PopoverPlacement.Right => PopoverPlacement.Left,
            _ => PopoverPlacement.Right,
        };
        if (Room(side) < need && Room(opposite) > Room(side))
            side = opposite;

        float x, y;
        if (vertical)
        {
            y = side == PopoverPlacement.Below ? anchor.Bottom + gap : anchor.Y - gap - size.Height;
            x = alignment switch
            {
                PopoverAlignment.Start => anchor.X,
                PopoverAlignment.Center => anchor.X + (anchor.Width - size.Width) / 2f,
                _ => anchor.Right - size.Width,
            };
        }
        else
        {
            x = side == PopoverPlacement.Right ? anchor.Right + gap : anchor.X - gap - size.Width;
            y = alignment switch
            {
                PopoverAlignment.Start => anchor.Y,
                PopoverAlignment.Center => anchor.Y + (anchor.Height - size.Height) / 2f,
                _ => anchor.Bottom - size.Height,
            };
        }

        x = Clamp(x, margin, window.Width - margin - size.Width);
        y = Clamp(y, margin, window.Height - margin - size.Height);
        return (new Point(MathF.Round(x), MathF.Round(y)), side);

        // Wider than the window less its margins: from the near margin, overflowing the far one.
        static float Clamp(float v, float lo, float hi) => hi < lo ? lo : Math.Clamp(v, lo, hi);
    }

    private string? Name()
    {
        if (AccessibleName is { } name) return name;
        if (_anchor is not { } anchor) return null;
        return anchor.AccessibleName ?? AccessibilityTreeBuilder.GetContentText(anchor);
    }

    /// <summary>Builds the card around the app's content, so a refresh or a reactive change
    /// patches it in place.</summary>
    private sealed class View(Popover popover) : Component
    {
        public void Refresh() => MarkDirty();

        public override Element Build()
        {
            var card = new CardElement
            {
                Role = AccessibilityRole.Dialog,
                AccessibleName = popover.Name(),
                AutomationId = popover.AutomationId,
            };
            card.AddChild(popover._content());
            return card;
        }
    }

    /// <summary>The card: the theme's fill, border, corners, padding and shadow, and the gap and
    /// margin the placement keeps.</summary>
    internal sealed class CardElement : BoxElement
    {
        public float Gap = 6f;
        public float WindowMargin = 8f;

        public CardElement()
        {
            Flex.Direction = FlexDirection.Column;
        }

        protected internal override void ApplyTheme(Theme theme)
        {
            var t = theme.Popover;
            BackgroundColor = t.Background ?? theme.Surface;
            BorderColor = t.Border ?? theme.Border;
            Box.BorderWidth = new Thickness(t.BorderWidth);
            Box.Padding = t.Padding;
            CornerRadius = new CornerRadius(t.CornerRadius);
            BoxShadows = t.Shadows is null ? null : [.. t.Shadows];
            Gap = t.Gap;
            WindowMargin = t.WindowMargin;
        }
    }
}
