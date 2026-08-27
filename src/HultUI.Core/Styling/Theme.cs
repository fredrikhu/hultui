using HultUI.Core.Primitives;

namespace HultUI.Core.Styling;

/// <summary>
/// Root-level style defaults. Provides inherited text properties and semantic colors.
/// Swappable at runtime to toggle light/dark mode.
/// </summary>
public sealed class Theme
{
    // Inherited text defaults
    public string FontFamily { get; init; } = "sans-serif";
    public float FontSize { get; init; } = 16f;
    public FontWeight FontWeight { get; init; } = FontWeight.Regular;
    public Color TextColor { get; init; } = new(0, 0, 0);
    public TextDirection TextDirection { get; init; } = TextDirection.Ltr;
    public CursorType Cursor { get; init; } = CursorType.Default;

    // ──── Component styles ────
    //
    // These are facades over the same extension store a third-party control uses, so the
    // framework's own controls are not a privileged case with a mechanism nobody else can
    // reach. The property form stays because it reads better in an initializer and because
    // `new Theme { Button = ... }` is already written in three apps.

    public ButtonTheme Button { get => Get<ButtonTheme>(); init => Set(value); }
    public TextInputTheme TextInput { get => Get<TextInputTheme>(); init => Set(value); }
    public CheckboxTheme Checkbox { get => Get<CheckboxTheme>(); init => Set(value); }
    public RadioButtonTheme RadioButton { get => Get<RadioButtonTheme>(); init => Set(value); }
    public TooltipTheme Tooltip { get => Get<TooltipTheme>(); init => Set(value); }
    public ContextMenuTheme ContextMenu { get => Get<ContextMenuTheme>(); init => Set(value); }
    public DropdownTheme Dropdown { get => Get<DropdownTheme>(); init => Set(value); }
    public CalendarTheme Calendar { get => Get<CalendarTheme>(); init => Set(value); }
    public TimeFieldTheme TimeField { get => Get<TimeFieldTheme>(); init => Set(value); }

    // Semantic colors
    public Color Background { get; init; } = Color.White;
    public Color Surface { get; init; } = new(248, 250, 252);       // slate-50
    public Color SurfaceVariant { get; init; } = new(241, 245, 249); // slate-100
    public Color Primary { get; init; } = new(79, 70, 229);          // indigo-600
    public Color PrimaryText { get; init; } = Color.White;
    public Color Secondary { get; init; } = new(100, 116, 139);      // slate-500
    public Color SecondaryText { get; init; } = Color.White;
    public Color Border { get; init; } = new(226, 232, 240);         // slate-200
    public Color Danger { get; init; } = new(220, 38, 38);           // red-600
    public Color DangerText { get; init; } = Color.White;
    public Color FocusRing { get; init; } = new(59, 130, 246, 180);  // blue-500 with alpha

    // ──── Extension store ────
    //
    // Two dictionaries rather than one: what the app said, and what CreateDefault produced.
    // Keeping them apart is what lets With() carry the overrides to a new theme while
    // dropping stale defaults, and what makes "did the app set this?" answerable.

    private readonly Dictionary<Type, object> _overrides;
    private Dictionary<Type, object>? _defaults;

    public Theme()
    {
        _overrides = [];
    }

    /// <summary>Copies everything but the materialized defaults, which are cheap to rebuild
    /// and would otherwise carry an answer computed for a different set of overrides.</summary>
    private Theme(Theme source)
    {
        _overrides = new Dictionary<Type, object>(source._overrides);

        FontFamily = source.FontFamily;
        FontSize = source.FontSize;
        FontWeight = source.FontWeight;
        TextColor = source.TextColor;
        TextDirection = source.TextDirection;
        Cursor = source.Cursor;

        Background = source.Background;
        Surface = source.Surface;
        SurfaceVariant = source.SurfaceVariant;
        Primary = source.Primary;
        PrimaryText = source.PrimaryText;
        Secondary = source.Secondary;
        SecondaryText = source.SecondaryText;
        Border = source.Border;
        Danger = source.Danger;
        DangerText = source.DangerText;
        FocusRing = source.FocusRing;
    }

    /// <summary>
    /// This theme's values for <typeparamref name="T"/>: what the app set, or what
    /// <see cref="IThemeExtension{TSelf}.CreateDefault"/> produced, materialized once.
    ///
    /// <para>Called from <c>ApplyTheme</c>, so it runs for every themed element on every
    /// style pass — it is a dictionary lookup and stays one.</para>
    /// </summary>
    public T Get<T>() where T : class, IThemeExtension<T>
    {
        if (_overrides.TryGetValue(typeof(T), out var overridden))
            return (T)overridden;

        _defaults ??= [];
        if (_defaults.TryGetValue(typeof(T), out var cached))
            return (T)cached;

        // Materializing a default does not change what this theme means, so a shared
        // static (Theme.Light) can be filled in lazily without becoming observably mutable.
        var created = T.CreateDefault(this);
        _defaults[typeof(T)] = created;
        return created;
    }

    /// <summary>
    /// A copy of this theme with <typeparamref name="T"/> replaced. Used by apps at startup
    /// to impose their palette on a control they did not write:
    /// <c>app.Theme = Theme.Dark.With(new GaugeTheme { Fill = BrandRed });</c>
    ///
    /// <para>Returns a new theme rather than mutating. <see cref="Light"/> and
    /// <see cref="Dark"/> are shared process-wide, so mutating in place would leak one app's
    /// brand colour into every other consumer of that theme in the process — including a
    /// second window and the DevTools inspector.</para>
    /// </summary>
    public Theme With<T>(T extension) where T : class, IThemeExtension<T>
    {
        ArgumentNullException.ThrowIfNull(extension);
        var copy = new Theme(this);
        copy._overrides[typeof(T)] = extension;
        return copy;
    }

    /// <summary>Whether the app set this extension explicitly, rather than taking the
    /// default. Exposed for the inspector, which is more useful when it can say which.</summary>
    public bool IsOverridden<T>() where T : class, IThemeExtension<T> =>
        _overrides.ContainsKey(typeof(T));

    private void Set<T>(T value) where T : class, IThemeExtension<T> =>
        _overrides[typeof(T)] = value ?? throw new ArgumentNullException(nameof(value));

    /// <summary>
    /// Converts the theme's text properties to a ResolvedInheritedStyle
    /// for use as the root defaults in style resolution.
    /// </summary>
    public ResolvedInheritedStyle ToResolvedStyle() => new(
        FontFamily: FontFamily,
        FontSize: FontSize,
        FontWeight: FontWeight,
        TextColor: TextColor,
        TextDirection: TextDirection,
        Cursor: Cursor);

    public static Theme Light { get; } = new();

    public static Theme Dark { get; } = new()
    {
        TextColor = new Color(226, 232, 240),           // slate-200
        Background = new Color(15, 23, 42),              // slate-900
        Surface = new Color(30, 41, 59),                 // slate-800
        SurfaceVariant = new Color(51, 65, 85),          // slate-700
        Primary = new Color(99, 102, 241),               // indigo-500
        PrimaryText = Color.White,
        Secondary = new Color(148, 163, 184),            // slate-400
        SecondaryText = new Color(15, 23, 42),           // slate-900
        Border = new Color(51, 65, 85),                  // slate-700
        Danger = new Color(239, 68, 68),                 // red-500
        DangerText = Color.White,
        FocusRing = new Color(99, 102, 241, 180),       // indigo-500 with alpha
        Tooltip = new TooltipTheme
        {
            Background = new Color(51, 65, 85),          // slate-700
            TextColor = new Color(226, 232, 240),        // slate-200
        },
        ContextMenu = new ContextMenuTheme
        {
            Background = new Color(30, 41, 59),          // slate-800
            BorderColor = new Color(51, 65, 85),         // slate-700
            TextColor = new Color(226, 232, 240),        // slate-200
            DisabledTextColor = new Color(100, 116, 139),// slate-500
            HoverBackground = new Color(51, 65, 85),     // slate-700
            SeparatorColor = new Color(51, 65, 85),      // slate-700
            ShortcutTextColor = new Color(148, 163, 184),// slate-400
        },
    };
}

public sealed class TooltipTheme : IThemeExtension<TooltipTheme>
{
    public Color Background { get; init; } = new(15, 23, 42);       // slate-900
    public Color TextColor { get; init; } = new(248, 250, 252);     // slate-50
    public float CornerRadius { get; init; } = 4f;
    public Thickness Padding { get; init; } = new(6, 10);
    public float FontSize { get; init; } = 13f;
    public float ShowDelay { get; init; } = 0.5f;
    public float Gap { get; init; } = 4f;

    /// <summary>
    /// The built-in defaults are literal rather than derived from the palette: they are the
    /// values these controls already shipped with, and deriving them here would repaint
    /// every existing app. New extensions should prefer deriving from the theme — see
    /// <see cref="IThemeExtension{TSelf}"/>.
    /// </summary>
    public static TooltipTheme CreateDefault(Theme theme) => new();
}
