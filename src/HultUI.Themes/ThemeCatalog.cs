using HultUI.Core.Primitives;

namespace HultUI.Themes;

/// <summary>
/// The colour schemes an app can offer. Ten borrowed from the schemes people already read
/// code in — Gruvbox, Nord, Dracula, Solarized and friends — plus whatever the app itself
/// ships as its default, which it prepends.
/// </summary>
/// <remarks>
/// <para>Each entry is a transcription of the scheme's own published palette: the ground,
/// the raised surface, the foreground, an accent or two, and the red / green / amber. What
/// is <i>not</i> transcribed is the tonal ramps, because none of these schemes publishes
/// one — they are terminal and editor palettes, sixteen flat colours. The ramps come out of
/// <see cref="Ramp"/>, which is why a scheme reads as itself here without matching any
/// particular editor's rendering of it pixel for pixel.</para>
///
/// <para>Ids are persisted. Renaming one drops every user who had it selected back to the
/// default, so they are frozen.</para>
/// </remarks>
public static class ThemeCatalog
{
    private static Color Hex(string h) => Color.FromHex(h);

    /// <summary>
    /// Nocturne — the design system Junction and Vesper Commander were built in, and the
    /// default in both.
    ///
    /// <para>The seeds here are for completeness: both apps hold Nocturne's ramps as
    /// literals, because they were drawn by hand rather than derived, and running them back
    /// through <see cref="Ramp"/> would shift the default look for no gain. What this entry
    /// really carries is the id, the name and the code theme.</para>
    /// </summary>
    public static readonly NamedTheme Nocturne = new()
    {
        Id = "nocturne",
        Name = "Nocturne",
        Blurb = "The house style. Deep indigo under a single blurple accent.",
        IsDark = true,
        Bg = Hex("#161826"),
        Surface = Hex("#232532"),
        Text = Hex("#e9e9ed"),
        Neutral = Hex("#9397ab"),
        Accent = Hex("#9184d9"),
        Accent2 = Hex("#a7a1db"),
        Red = Hex("#cf8f8f"),
        Green = Hex("#7fbf79"),
        Amber = Hex("#e3b341"),
        SyntaxTheme = "DarkPlus",
    };

    // ── Gruvbox — https://github.com/morhetz/gruvbox ─────────────────────
    // The "hard" dark background (bg0_h) rather than the medium one: the apps put large
    // flat panels next to each other, and the extra step down gives the raised surfaces
    // somewhere to sit.
    public static readonly NamedTheme GruvboxDark = new()
    {
        Id = "gruvbox-dark",
        Name = "Gruvbox Dark",
        Blurb = "Retro groove. Warm, low-glare, unmistakable.",
        IsDark = true,
        Bg = Hex("#1d2021"),
        Surface = Hex("#32302f"),
        Text = Hex("#ebdbb2"),
        Neutral = Hex("#a89984"),
        Accent = Hex("#fabd2f"),      // bright yellow — gruvbox's signature
        Accent2 = Hex("#8ec07c"),     // bright aqua
        Red = Hex("#fb4934"),
        Green = Hex("#b8bb26"),
        Amber = Hex("#fe8019"),       // bright orange
        SyntaxTheme = "KimbieDark",
    };

    public static readonly NamedTheme GruvboxLight = new()
    {
        Id = "gruvbox-light",
        Name = "Gruvbox Light",
        Blurb = "The same warm paper, lit from the front.",
        IsDark = false,
        Bg = Hex("#fbf1c7"),
        Surface = Hex("#f2e5bc"),
        Text = Hex("#3c3836"),
        Neutral = Hex("#7c6f64"),
        Accent = Hex("#b57614"),      // faded yellow, dark enough to read on paper
        Accent2 = Hex("#427b58"),     // faded aqua
        Red = Hex("#9d0006"),
        Green = Hex("#79740e"),
        Amber = Hex("#af3a03"),
        SyntaxTheme = "SolarizedLight",
    };

    // ── Nord — https://www.nordtheme.com ─────────────────────────────────
    public static readonly NamedTheme Nord = new()
    {
        Id = "nord",
        Name = "Nord",
        Blurb = "Arctic, north-bluish. Cool and even-tempered.",
        IsDark = true,
        Bg = Hex("#2e3440"),          // nord0
        Surface = Hex("#3b4252"),     // nord1
        Text = Hex("#eceff4"),        // nord6
        Neutral = Hex("#8794a8"),     // between nord3 and nord4
        Accent = Hex("#88c0d0"),      // nord8 — the frost the theme is known for
        Accent2 = Hex("#b48ead"),     // nord15
        Red = Hex("#bf616a"),         // nord11
        Green = Hex("#a3be8c"),       // nord14
        Amber = Hex("#ebcb8b"),       // nord13
        SyntaxTheme = "OneDark",
    };

    // ── Dracula — https://draculatheme.com ───────────────────────────────
    public static readonly NamedTheme Dracula = new()
    {
        Id = "dracula",
        Name = "Dracula",
        Blurb = "Night colours turned all the way up.",
        IsDark = true,
        Bg = Hex("#282a36"),
        Surface = Hex("#343746"),
        Text = Hex("#f8f8f2"),
        Neutral = Hex("#6272a4"),     // "comment"
        Accent = Hex("#bd93f9"),      // purple
        Accent2 = Hex("#ff79c6"),     // pink
        Red = Hex("#ff5555"),
        Green = Hex("#50fa7b"),
        Amber = Hex("#f1fa8c"),
        SyntaxTheme = "Dracula",
    };

    // ── Solarized — https://ethanschoonover.com/solarized ────────────────
    public static readonly NamedTheme SolarizedDark = new()
    {
        Id = "solarized-dark",
        Name = "Solarized Dark",
        Blurb = "Measured contrast, worked out on paper first.",
        IsDark = true,
        Bg = Hex("#002b36"),          // base03
        Surface = Hex("#073642"),     // base02
        Text = Hex("#93a1a1"),        // base1
        Neutral = Hex("#657b83"),     // base00
        Accent = Hex("#268bd2"),      // blue
        Accent2 = Hex("#6c71c4"),     // violet
        Red = Hex("#dc322f"),
        Green = Hex("#859900"),
        Amber = Hex("#b58900"),
        SyntaxTheme = "SolarizedDark",
    };

    public static readonly NamedTheme SolarizedLight = new()
    {
        Id = "solarized-light",
        Name = "Solarized Light",
        Blurb = "The same palette on its light ground.",
        IsDark = false,
        Bg = Hex("#fdf6e3"),          // base3
        Surface = Hex("#eee8d5"),     // base2
        // base02 rather than the scheme's base00 body tone. Solarized is deliberately
        // low-contrast, and on the raised surface base00 lands at 4.5:1 — fine for prose on
        // the page, not for the labels in a toolbar. base02 is still Solarized's own ink.
        Text = Hex("#073642"),        // base02
        Neutral = Hex("#657b83"),     // base00
        Accent = Hex("#268bd2"),
        Accent2 = Hex("#6c71c4"),
        Red = Hex("#dc322f"),
        Green = Hex("#859900"),
        Amber = Hex("#b58900"),
        SyntaxTheme = "SolarizedLight",
    };

    // ── Tokyo Night — https://github.com/enkia/tokyo-night-vscode-theme ──
    public static readonly NamedTheme TokyoNight = new()
    {
        Id = "tokyo-night",
        Name = "Tokyo Night",
        Blurb = "Deep blue-black, neon at the edges.",
        IsDark = true,
        Bg = Hex("#1a1b26"),
        Surface = Hex("#24283b"),
        Text = Hex("#c0caf5"),
        Neutral = Hex("#565f89"),
        Accent = Hex("#7aa2f7"),      // blue
        Accent2 = Hex("#bb9af7"),     // magenta
        Red = Hex("#f7768e"),
        Green = Hex("#9ece6a"),
        Amber = Hex("#e0af68"),
        SyntaxTheme = "TomorrowNightBlue",
    };

    // ── Catppuccin Mocha — https://catppuccin.com ────────────────────────
    public static readonly NamedTheme CatppuccinMocha = new()
    {
        Id = "catppuccin-mocha",
        Name = "Catppuccin Mocha",
        Blurb = "Pastels on a soft dark ground.",
        IsDark = true,
        Bg = Hex("#1e1e2e"),          // base
        Surface = Hex("#313244"),     // surface0
        Text = Hex("#cdd6f4"),
        Neutral = Hex("#7f849c"),     // overlay1
        Accent = Hex("#cba6f7"),      // mauve
        Accent2 = Hex("#89b4fa"),     // blue
        Red = Hex("#f38ba8"),
        Green = Hex("#a6e3a1"),
        Amber = Hex("#f9e2af"),
        SyntaxTheme = "Dracula",
    };

    // ── Everforest — https://github.com/sainnhe/everforest ───────────────
    public static readonly NamedTheme Everforest = new()
    {
        Id = "everforest",
        Name = "Everforest",
        Blurb = "Green-grey and soft on the eyes for long reads.",
        IsDark = true,
        Bg = Hex("#2d353b"),
        Surface = Hex("#3d484d"),
        Text = Hex("#d3c6aa"),
        Neutral = Hex("#859289"),
        Accent = Hex("#a7c080"),      // green
        Accent2 = Hex("#7fbbb3"),     // blue
        Red = Hex("#e67e80"),
        Green = Hex("#83c092"),       // aqua — reads as "added" against the green accent
        Amber = Hex("#dbbc7f"),
        SyntaxTheme = "OneDark",
    };

    // ── Rosé Pine — https://rosepinetheme.com ────────────────────────────
    public static readonly NamedTheme RosePine = new()
    {
        Id = "rose-pine",
        Name = "Rosé Pine",
        Blurb = "Muted plum and gold. Quiet, slightly formal.",
        IsDark = true,
        Bg = Hex("#191724"),
        Surface = Hex("#1f1d2e"),
        Text = Hex("#e0def4"),
        Neutral = Hex("#908caa"),     // subtle
        Accent = Hex("#c4a7e7"),      // iris
        Accent2 = Hex("#9ccfd8"),     // foam
        Red = Hex("#eb6f92"),         // love
        Green = Hex("#31748f"),       // pine
        Amber = Hex("#f6c177"),       // gold
        SyntaxTheme = "Dracula",
    };

    /// <summary>Every scheme, in the order a picker should list them: the house style first,
    /// then the borrowed ones, darks before lights within each family.</summary>
    public static IReadOnlyList<NamedTheme> All { get; } =
    [
        Nocturne,
        GruvboxDark,
        GruvboxLight,
        Nord,
        Dracula,
        TokyoNight,
        CatppuccinMocha,
        Everforest,
        RosePine,
        SolarizedDark,
        SolarizedLight,
    ];

    /// <summary>The scheme with this id, or <see cref="Nocturne"/> when the id is unknown —
    /// which is what a settings file written by an older or newer build reads as.</summary>
    public static NamedTheme ById(string? id) =>
        All.FirstOrDefault(t => t.Id == id) ?? Nocturne;
}
