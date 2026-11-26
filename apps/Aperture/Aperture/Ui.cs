using Aperture.Theme;
using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using HultUI.Icons;

namespace Aperture;

/// <summary>
/// Small, stateless builders shared across the Aperture views — icons, mono figures,
/// chips, buttons, the progress sliver on a poster. Keeps the view code declarative and
/// on-theme.
/// </summary>
public static class Ui
{
    /// <summary>
    /// Converts a nominal icon size into the box a vector icon needs to look the same.
    /// Same measured ratio the other apps use: Phosphor's paths ink ~72% of their box
    /// where the design's nominal sizes assume ~64%.
    /// </summary>
    public static float IconBox(float nominal) => MathF.Round(nominal * 0.88f * 2f) / 2f;

    /// <summary>Device pixels per layout unit, kept in step with the window by
    /// <see cref="ApertureApp"/>. Icons rasterize at this scale so the renderer never
    /// resamples them.</summary>
    public static float DisplayScale
    {
        get => _displayScale;
        set
        {
            if (MathF.Abs(_displayScale - value) < 0.001f) return;
            _displayScale = value;
            Phosphor.InvalidateCache();   // every cached raster was built for the old scale
        }
    }
    private static float _displayScale = 1f;

    public static Element Icon(string name, float size, Color color) =>
        Phosphor.Draw(name, IconBox(size), color, DisplayScale);

    public static TextElement Mono(string text, float size, Color color) =>
        Text.New(text).FontFamily(Fonts.Mono).FontSize(size).TextColor(color);

    /// <summary>A one-line label that ellipsizes. <c>FlexShrink(0)</c> is not enough on its
    /// own: in a tight row a chip is measured under the leftover width, the label wraps
    /// during that measurement, and a no-shrink chip then keeps the two-line size.</summary>
    public static TextElement Line(string text, float size, Color color) =>
        Text.New(text).FontSize(size).TextColor(color).MaxLines(1).TextOverflow(TextOverflow.Ellipsis);

    /// <summary>Declarative hover tint (Interaction system): re-applied after any rebuild,
    /// so it survives mid-hover reconciliation.</summary>
    public static T HoverBg<T>(this T el, Color hover) where T : BoxElement =>
        el.OnHover(b => b.BackgroundColor = hover);

    /// <summary>A metadata chip: year, rating certificate, runtime, "4K". Outlined, never
    /// filled — a row of filled chips under a poster competes with the artwork.</summary>
    public static BoxElement Chip(string text, Color? color = null)
    {
        var fg = color ?? Halide.TextMuted;
        return Box.New()
            .Padding(new Thickness(2, 6, 2, 6))
            .CornerRadius(Halide.Radius1)
            .BorderColor(Halide.Alpha(fg, 0.45)).BorderWidth(1f)
            .FlexShrink(0)
            .Children(Line(text, 11f, fg));
    }

    /// <summary>
    /// The watched sliver: a 3dip accent bar across the bottom of a poster or still,
    /// as wide as the fraction watched. The one place the accent covers area, and it is
    /// three pixels tall.
    /// </summary>
    public static Element ProgressSliver(double percent, float width)
    {
        var track = Box.New().Width(width).Height(3f).Background(Halide.Alpha(Halide.Text, 0.20));
        float filled = (float)Math.Clamp(percent / 100.0, 0, 1) * width;
        if (filled > 0) track.AddChild(Box.New().Width(filled).Height(3f).Background(Halide.Accent));
        return track;
    }

    /// <summary>A button in Halide's families, with Aperture's content: a label, an
    /// optional leading glyph, and the automation id every control here carries.</summary>
    public static ButtonElement Button(ButtonVariant variant, string label, Action? onClick,
        string? automationId = null, string? glyph = null, ButtonStyle? style = null)
    {
        var b = HultUI.Core.Builders.Button.New(variant).Gap(8).Style(style);
        b.AccessibleName = label;
        if (glyph != null)
            b.AddChild(Icon(glyph, 15f, variant == ButtonVariant.Primary ? new Color(35, 23, 4) : Halide.Text));
        b.AddChild(Text.New(label));
        if (automationId != null) b.AutomationId(automationId);
        if (onClick != null) b.OnClick(_ => onClick());
        return b;
    }

    /// <summary>A bare icon button — the title bar's controls, a card's overflow.</summary>
    /// <summary>
    /// Whether keys are what the app can be driven by: not on Android (a phone, a TV's remote), where
    /// a shortcut named in a tooltip or a button's name ("Back (Esc)", the rail's digits) is a key
    /// nobody can press, and TalkBack read it out as part of the name.
    /// </summary>
    public static bool HasKeyboard => !OperatingSystem.IsAndroid();

    /// <summary>A control's words without the key in brackets at their end where there is no keyboard
    /// (<see cref="HasKeyboard"/>): "Back (Esc)" is "Back" on a phone. By this app's habit, a hint in
    /// brackets at the end is the key's.</summary>
    public static string WithoutKeyHint(string label)
    {
        if (HasKeyboard || !label.EndsWith(')')) return label;
        int at = label.LastIndexOf(" (", StringComparison.Ordinal);
        return at > 0 ? label[..at] : label;
    }

    public static BoxElement IconButton(string glyph, string tooltip, Action onClick, float size = 16f,
        Color? color = null, string? automationId = null)
    {
        tooltip = WithoutKeyHint(tooltip);
        var b = Flex.Row(gap: 0).Width(32).Height(32)
            .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
            .CornerRadius(Halide.Radius1)
            .Cursor(CursorType.Pointer)
            .Tooltip(tooltip)
            .AccessibleRole(AccessibilityRole.Button).AccessibleName(tooltip)
            .Pressable();
        if (automationId != null) b.AutomationId(automationId);
        b.AddChild(Icon(glyph, size, color ?? Halide.TextDim));
        b.OnClick(_ => onClick());
        return b.HoverBg(Halide.Raised);
    }

    /// <summary>
    /// Makes a box that answers a click answer the keyboard as a button does: a Tab stop that
    /// wears the focus ring, which Enter and Space press, and whose menu, when it has one, the
    /// menu key or Shift+F10 opens. Without it a box with a click handler is the mouse's alone —
    /// only the framework's <see cref="ButtonElement"/> presses on a key — and the rail, the
    /// filter chips and an item's toggles could not be reached from the keyboard at all.
    /// </summary>
    /// <remarks>The press is a click sent to the box itself, placed under its bottom-left corner,
    /// so a chip whose click opens a menu at the click opens it under the chip.</remarks>
    public static T Pressable<T>(this T el) where T : BoxElement
    {
        el.IsFocusable = true;
        el.ShowsFocusVisual = true;
        el.On(PressOnKey);
        return el;
    }

    /// <summary>One handler for every pressable box, reading the box it is on from the event: a
    /// handler that closed over the box would press the build's copy, not the one on screen.</summary>
    private static readonly Action<KeyDownEvent> PressOnKey = OnPressKey;

    private static void OnPressKey(KeyDownEvent e)
    {
        // Its own keys: one pressed on a button inside it is that button's.
        if (e.Handled || !ReferenceEquals(e.Target, e.CurrentTarget) || e.CurrentTarget is not BoxElement box) return;
        var held = e.Modifiers & (KeyModifiers.Ctrl | KeyModifiers.Alt | KeyModifiers.Super | KeyModifiers.Shift);
        var bounds = box.WindowBorderBox;
        var under = new Point(bounds.X, bounds.Y + bounds.Height);
        if (held == 0 && e.Key is KeyCode.Enter or KeyCode.NumpadEnter or KeyCode.Space)
            EventDispatcher.DispatchDirect(new ClickEvent { Position = under }, box);
        else if (box.ContextMenuItems is not null && box.OverlayHost is HultUI.Window window
            && (held == 0 && e.Key == KeyCode.Menu || held == KeyModifiers.Shift && e.Key == KeyCode.F10))
            window.OpenContextMenu(box, under);
        else
            return;
        e.Handled = true;
        e.StopPropagation = true;
    }

    /// <summary>"APERTURE" at the letterform's tracking — the title bar and the front door.</summary>
    public static TextElement Wordmark(float size, Color color) =>
        Text.New("APERTURE").FontSize(size).FontWeight(FontWeight.Medium)
            .TextColor(color).LetterSpacing(Halide.WordmarkTracking(size));

    /// <summary>A section heading over a row of cards: "Continue watching", "Latest films".</summary>
    public static Element SectionHeading(string title, string? trailing = null)
    {
        var row = Flex.Row(gap: Halide.S3).AlignItems(AlignItems.Center).FlexShrink(0);
        row.AddChild(Text.New(title).FontSize(15f).FontWeight(FontWeight.SemiBold).TextColor(Halide.Text));
        if (trailing != null)
        {
            row.AddChild(Box.New().FlexGrow(1));
            row.AddChild(Line(trailing, 12f, Halide.TextMuted));
        }
        return row;
    }

    /// <summary>1 h 47 m — the runtime format used everywhere. Minutes alone under an hour.</summary>
    public static string Runtime(TimeSpan span) =>
        span.TotalHours >= 1
            ? $"{(int)span.TotalHours} h {span.Minutes} m"
            : $"{Math.Max(1, (int)span.TotalMinutes)} m";

    /// <summary>A position on the clock: 1:04:12, or 4:12 under an hour. What a chapter
    /// list shows, where the point is where it is rather than how long it lasts.</summary>
    public static string Clock(TimeSpan at) =>
        at.TotalHours >= 1 ? $"{(int)at.TotalHours}:{at.Minutes:00}:{at.Seconds:00}"
                           : $"{at.Minutes}:{at.Seconds:00}";

    /// <summary>"just now", "5 min ago", "3 h ago", "yesterday", "4 days ago", then the date —
    /// when a request was made, which matters in days rather than seconds.</summary>
    public static string Ago(DateTimeOffset at, DateTimeOffset? now = null)
    {
        var span = (now ?? DateTimeOffset.UtcNow) - at;
        if (span < TimeSpan.FromMinutes(1)) return "just now";
        if (span < TimeSpan.FromHours(1)) return $"{(int)span.TotalMinutes} min ago";
        if (span < TimeSpan.FromDays(1)) return $"{(int)span.TotalHours} h ago";
        if (span < TimeSpan.FromDays(2)) return "yesterday";
        if (span < TimeSpan.FromDays(30)) return $"{(int)span.TotalDays} days ago";
        return at.ToLocalTime().ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>"38 m left" — what a half-watched card says instead of a percentage.</summary>
    public static string Remaining(TimeSpan total, TimeSpan position)
    {
        var left = total - position;
        return left <= TimeSpan.Zero ? "Finished" : $"{Runtime(left)} left";
    }
}
