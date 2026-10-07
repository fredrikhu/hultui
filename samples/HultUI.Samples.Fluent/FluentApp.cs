using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Platform;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.Rendering.Skia;

namespace HultUI.Samples.Fluent;

// Inside the namespace on purpose. This assembly sits under HultUI, so an unqualified `Text`
// here binds to the HultUI.Text namespace (the shaping engine) rather than to the label
// builder — and an alias declared above the namespace loses to that. Declared in the
// namespace body, it wins.
using Text = HultUI.Core.Builders.Text;

public enum Page { Home, Personalization, Gallery, RichText, About }

/// <summary>
/// A Windows 11 Settings-shaped app: a custom title bar, a NavigationView down the left, and a
/// content frame with its top-left corner rounded off.
///
/// <para>The whole point of the sample is that none of it is special-cased in the framework.
/// The window is borderless and the caption is HultUI elements; the nav pane is a column of
/// boxes; the content frame is one box with a single rounded corner. What makes it read as
/// WinUI rather than as a dark-grey app is the arithmetic — 32px controls, 4px radii, 68px
/// setting rows, a 3x16 selection pill — and the fact that the framework's own widgets are
/// pointed at the same palette instead of being left in their defaults.</para>
/// </summary>
public sealed partial class FluentApp : Component
{
    private readonly Application _app;
    private readonly Window _window;

    [Reactive] public partial Page Current { get; set; }
    [Reactive] public partial bool Dark { get; set; }
    [Reactive] public partial int AccentIndex { get; set; }
    [Reactive] public partial bool SearchFocused { get; set; }

    // Personalization
    [Reactive] public partial bool Transparency { get; set; }
    [Reactive] public partial bool Animations { get; set; }
    [Reactive] public partial double TextScale { get; set; }
    [Reactive] public partial bool ShowAccentOnSurfaces { get; set; }

    // Gallery
    [Reactive] public partial bool ToggleA { get; set; }
    [Reactive] public partial bool ToggleB { get; set; }
    [Reactive] public partial bool CheckA { get; set; }
    [Reactive] public partial bool CheckB { get; set; }
    [Reactive] public partial int RadioChoice { get; set; }
    [Reactive] public partial double Volume { get; set; }
    [Reactive] public partial int ComboChoice { get; set; }
    [Reactive] public partial bool ExpanderOpen { get; set; }
    [Reactive] public partial bool NotifyMail { get; set; }
    [Reactive] public partial bool NotifyCalendar { get; set; }
    [Reactive] public partial bool NotifyStore { get; set; }
    [Reactive] public partial bool InfoBarShown { get; set; }
    [Reactive] public partial int ClickCount { get; set; }

    // Rich text
    [Reactive] public partial HultUI.Core.RichText.RichDocument Notes { get; set; }
    [Reactive] public partial bool NotesReadOnly { get; set; }
    public readonly Ref<RichTextEditorElement> NotesEditor = new();
    public readonly RichTextToolbar NotesToolbar;

    /// <summary>Builds again: what the toolbar asks for when the caret moves.</summary>
    public void Refresh() => MarkDirty();

    /// <summary>Ctrl+F on the rich text page: the notes' matches tinted, Enter walking them.</summary>
    public readonly FindBar NotesFind;

    protected override void OnAfterBuild() => NotesFind.Service();

    private readonly Dictionary<string, IImageSource?> _pictures = new(StringComparer.Ordinal);

    /// <summary>The notes' pictures: <c>sample:</c> ones drawn here, anything else a file on
    /// disk. Kept once made, failures too, so a missing file is not looked for every build.</summary>
    public IImageSource? Picture(string source)
    {
        if (_pictures.TryGetValue(source, out var known)) return known;
        IImageSource? made = null;
        try
        {
            if (source == "sample:aurora") made = Pages.Aurora(560, 180);
            else if (File.Exists(source)) made = SkiaImageSource.FromFile(source);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
        }
        return _pictures[source] = made;
    }

    /// <summary>What the last export did, under the editor: where it went, or why it did not.</summary>
    [Reactive] public partial string? NotesExported { get; set; }

    /// <summary>The notes written out where the person picks: a web page, or RTF for a word
    /// processor. Pictures go in with them.</summary>
    public async void ExportNotes(bool rtf)
    {
        var dialogs = _window.FileDialogs;
        if (!dialogs.IsAvailable) return;
        var path = await dialogs.SaveFileAsync(rtf ? "Notes.rtf" : "Notes.html", null,
            [rtf ? new FileDialogFilter("Rich Text Format", "rtf") : new FileDialogFilter("Web page", "html;htm")]);
        if (path is null) return;
        var doc = Notes;
        var options = new HultUI.Core.RichText.RichExportOptions
        {
            Title = doc.Blocks.FirstOrDefault(b => b.Kind == HultUI.Core.RichText.BlockKind.Heading)?.Text ?? "Notes",
            FontFamily = Fluent.BodyFont,
            Pictures = PictureFile,
        };
        try
        {
            // RTF is ASCII by construction; the page says it is UTF-8 and is written so.
            if (rtf) File.WriteAllText(path, HultUI.Core.RichText.RichRtf.Write(doc, options), System.Text.Encoding.ASCII);
            else File.WriteAllText(path, HultUI.Core.RichText.RichHtml.WriteDocument(doc, options), new System.Text.UTF8Encoding(false));
            NotesExported = "Saved " + Path.GetFileName(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            NotesExported = "Could not save: " + e.Message;
        }
    }

    /// <summary>A picture's file, for an export to embed: the aurora encoded as a PNG, anything
    /// else read from disk.</summary>
    public byte[]? PictureFile(HultUI.Core.RichText.RichImage image)
    {
        try
        {
            if (image.Source == "sample:aurora") return Pages.AuroraPng(560, 180);
            return File.Exists(image.Source) ? File.ReadAllBytes(image.Source) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private readonly MenuBar _menus;

    /// <summary>The gallery's flyout: WinUI's "are you sure" card under a button.</summary>
    public readonly Popover EmptyBinFlyout;

    /// <summary>One row of the gallery's table.</summary>
    public sealed record Download(string Name, string Type, long Size, DateTime Added);

    /// <summary>The gallery's table: sorts from its headers, resizes from their edges, and hides
    /// columns from a right press on the header.</summary>
    public readonly TableView<Download> Downloads;

    public static readonly Download[] DownloadRows =
    [
        new("Quarterly report.pdf", "PDF document", 2_480_312, new DateTime(2026, 10, 2, 9, 41, 0)),
        new("Holiday photos.zip", "Compressed folder", 384_902_144, new DateTime(2026, 9, 28, 18, 5, 0)),
        new("setup-x64.msi", "Windows Installer", 92_110_848, new DateTime(2026, 10, 4, 21, 12, 0)),
        new("Budget 2027.xlsx", "Excel workbook", 88_064, new DateTime(2026, 10, 5, 8, 2, 0)),
        new("Meeting notes.docx", "Word document", 41_472, new DateTime(2026, 9, 30, 14, 30, 0)),
        new("wallpaper-dark.png", "PNG image", 6_201_344, new DateTime(2026, 8, 17, 11, 0, 0)),
        new("podcast-episode-112.mp3", "MP3 audio", 58_720_256, new DateTime(2026, 9, 3, 7, 15, 0)),
        new("README.txt", "Text document", 1_024, new DateTime(2026, 7, 21, 16, 48, 0)),
    ];

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} bytes",
        < 1024 * 1024 => $"{bytes / 1024.0:0} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.0} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):0.0} GB",
    };

    public FluentApp(Application app, Window window)
    {
        _app = app;
        _window = window;
        Downloads = new TableView<Download>(
        [
            new TableColumn<Download> { Id = "name", Header = "Name", Fill = 1, CanHide = false,
                Text = d => d.Name, Compare = (a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase) },
            new TableColumn<Download> { Id = "type", Header = "Type", Width = 150,
                Text = d => d.Type, Compare = (a, b) => string.Compare(a.Type, b.Type, StringComparison.CurrentCultureIgnoreCase) },
            new TableColumn<Download> { Id = "size", Header = "Size", Width = 96, Align = ColumnAlign.End,
                Text = d => FormatSize(d.Size), Compare = (a, b) => a.Size.CompareTo(b.Size),
                FirstSort = SortDirection.Descending },
            new TableColumn<Download> { Id = "added", Header = "Date added", Width = 150,
                Text = d => d.Added.ToString("g"), Compare = (a, b) => a.Added.CompareTo(b.Added),
                FirstSort = SortDirection.Descending },
        ], MarkDirty) { RowHeight = 36f };
        NotesToolbar = new RichTextToolbar(NotesEditor, MarkDirty);
        NotesFind = new FindBar(MarkDirty)
        {
            Placeholder = "Find in the notes",
            Body = () => NotesEditor.Current,
            Selection = () => NotesEditor.Current?.FindSelection,
            Reveal = () => NotesEditor.Current?.RevealSearchMatch(),
            // Selecting the match scrolls the editor when it next draws: no layout to wait for.
            RevealAfterLayout = false,
        };
        EmptyBinFlyout = new Popover(() =>
        {
            var p = Palette;
            return Flex.Column(gap: 12f).Width(260f).Children(
                F.Body(p, "All 12 items will be deleted for good. Empty the Recycle Bin?"),
                // In a row, at its own width: the Fluent button's two boxes do not stretch together.
                Flex.Row(gap: 0).Children(
                    F.Button(p, "Yes, empty it", () => { ClickCount++; EmptyBinFlyout!.Close(); }, accent: true)));
        });
        _menus = new MenuBar(
        [
            new("&File",
            [
                new ContextMenuItem { Label = "Home", Action = () => Current = Page.Home },
                ContextMenuItem.Separator(),
                new ContextMenuItem { Label = "Exit", ShortcutHint = "Alt+F4", Action = () => _window.Close() },
            ]),
            // Asked for on each open, so the checks follow the state.
            new("&View", () =>
            [
                new ContextMenuItem { Label = "Dark mode", IsChecked = Dark, Action = () => SetDark(!Dark) },
                new ContextMenuItem { Label = "Animations", IsChecked = Animations, Action = () => Animations = !Animations },
                ContextMenuItem.Separator(),
                new ContextMenuItem { Label = "Personalization", IsChecked = Current == Page.Personalization, Action = () => Current = Page.Personalization },
                new ContextMenuItem { Label = "Controls gallery", IsChecked = Current == Page.Gallery, Action = () => Current = Page.Gallery },
                new ContextMenuItem { Label = "Rich text", IsChecked = Current == Page.RichText, Action = () => Current = Page.RichText },
            ]),
            new("&Help", [new ContextMenuItem { Label = "About", Action = () => Current = Page.About }]),
        ]);

        // The menu bar's keys come through the window's: F10, Alt, and Alt with a title's letter.
        // On the host, where keys land when nothing has the focus — a handler on the column
        // inside it never sees a key aimed at the host.
        var host = Mount();
        host.On<KeyDownEvent>(e =>
        {
            if ((Current == Page.RichText && NotesFind.HandleKey(e)) || _menus.HandleKey(e)) e.StopPropagation = true;
        });
        host.On<KeyUpEvent>(e => _menus.HandleKeyUp(e));

        Current = Page.Home;
        Notes = Pages.NotesSample();
        Dark = true;
        AccentIndex = 0;
        Transparency = true;
        Animations = true;
        TextScale = 125;
        ToggleA = true;
        Volume = 62;
        ComboChoice = 1;
        NotifyMail = true;
        NotifyCalendar = true;
        InfoBarShown = true;

        // Set here as well as in Program.cs, so anything that hosts this component directly —
        // a test, the screenshot driver — gets the window the user sees rather than a frame
        // with the framework's stock light widgets scattered through a dark page. Junction and
        // Meridian do the same for the same reason.
        ApplyTheme();
    }

    public FluentPalette Palette => FluentPalette.For(Dark, AccentPreset.All[AccentIndex]);

    public void SetDark(bool dark) { Dark = dark; ApplyTheme(); }

    public void SetAccent(int index) { AccentIndex = index; ApplyTheme(); }

    private void ApplyTheme() => _app.Theme = Palette.ToHultTheme();

    public override Element Build()
    {
        var p = Palette;

        return Flex.Column(gap: 0)
            .Background(p.Backdrop)
            .Children(
                TitleBar(p),
                Flex.Row(gap: 0).FlexGrow(1).Children(
                    NavPane(p),
                    ContentFrame(p)));
    }

    // ──────────────────────────────────────────────────────────────────────
    // Title bar
    // ──────────────────────────────────────────────────────────────────────

    private Element TitleBar(FluentPalette p)
    {
        var caption = Flex.Row(gap: 0).FlexShrink(0).Children(
            CaptionButton(p, Icons.Minimize, "Minimize", () => _window.Minimize()),
            CaptionButton(p, _window.IsMaximized ? Icons.Restore : Icons.Maximize,
                _window.IsMaximized ? "Restore" : "Maximize", () => _window.ToggleMaximize(), isMaximize: true),
            CaptionButton(p, Icons.Close, "Close", () => _window.Close(), isClose: true));

        return Flex.Row(gap: 0).FlexShrink(0)
            .Height(Fluent.TitleBarHeight)
            .AlignItems(AlignItems.Center)
            .WindowDrag()
            .Children(
                Box.New().Width(4f).FlexShrink(0),
                F.SubtleIconButton(p, Icons.Back, "Back", () => Current = Page.Home, 40f, 16f, p.TextPrimary),
                Box.New().Width(8f).FlexShrink(0),
                IconRenderer.Draw(Icons.Personalize, 16f, p.Accent),
                Box.New().Width(12f).FlexShrink(0),
                Text.New("Fluent Sample").FontSize(Fluent.CaptionSize).TextColor(p.TextPrimary),
                Box.New().Width(16f).FlexShrink(0),
                _menus.Mount(),
                Box.New().FlexGrow(1),
                caption);
    }

    /// <summary>
    /// A caption button: 46x48 and square-cornered, because that is the Windows caption band
    /// and anything rounded there looks like a web page pretending to be a window. Close is
    /// the one control in the system that gets a red hover fill and white glyph on top of it.
    /// </summary>
    private Element CaptionButton(
        FluentPalette p, Icon icon, string name, Action onClick, bool isMaximize = false, bool isClose = false)
    {
        var hover = isClose ? Color.FromHex("#C42B1C") : p.SubtleHover;
        var glyph = IconRenderer.Draw(icon, 10f, p.TextPrimary);

        var btn = Box.New().FlexShrink(0)
            .Width(46f).Height(Fluent.TitleBarHeight)
            .Background(Color.Transparent)
            .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
            .OnHover(b => b.Background(hover))
            .OnClick(_ => onClick())
            .AccessibleRole(AccessibilityRole.Button)
            .AccessibleName(name)
            .Children(glyph);

        // Tells the Windows frame this is HTMAXBUTTON, which is what makes the Snap Layouts
        // flyout appear on hover. Nothing happens off Windows.
        if (isMaximize) btn.WindowMaximizeButton();
        return btn;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Navigation pane
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The pane's search field, magnifier and all.
    ///
    /// <para>Composed rather than themed: the box, the glyph and the spacing are this sample's,
    /// and the framework's input sits inside it with <c>Chromeless()</c> so it draws no box of
    /// its own. Focus has to be carried in app state because the ring belongs to the wrapper and
    /// an element only gets focus styles for its own focus — two rebuilds per interaction, which
    /// is why the text is left on the element rather than mirrored into a signal that would
    /// rebuild the pane on every keystroke.</para>
    /// </summary>
    private Element SearchBox(FluentPalette p)
    {
        var input = TextInput.New("")
            .Chromeless()
            .PlaceholderText("Find a setting")
            .Padding(Thickness.Zero)
            .FlexGrow(1)
            .AccessibleName("Find a setting");
        input.On<FocusEvent>(_ => SearchFocused = true);
        input.On<BlurEvent>(_ => SearchFocused = false);

        // Two boxes for one border, the same trick the buttons use — a box takes a single border
        // colour, and a text field's bottom edge is a different colour from its sides. Here it is
        // not a subtle two-tone either: at rest the bottom is ControlStrong against #4C4C4C
        // sides, and on focus it grows to a 2px accent while the sides stay put.
        var inner = Flex.Row(gap: 8f).FlexGrow(1)
            .AlignItems(AlignItems.Center)
            .Padding(new Thickness(0, 10, 0, 10))
            .CornerRadius(Fluent.ControlRadius)
            .Border(new Thickness(1, 1, 0, 1), p.ControlStroke)
            .Children(
                IconRenderer.Draw(Icons.Search, 16f, p.TextSecondary),
                input);

        return Box.New().FlexShrink(0)
            .Height(Fluent.ControlHeight)
            .CornerRadius(Fluent.ControlRadius)
            .Background(SearchFocused ? p.ControlInputActive : p.ControlFill)
            .Border(new Thickness(0, 0, SearchFocused ? 2f : 1f, 0),
                SearchFocused ? p.Accent : p.ControlStrong)
            .Margin(new Thickness(0, 0, 8, 0))
            .Children(inner);
    }

    private Element NavPane(FluentPalette p)
    {
        var search = SearchBox(p);

        return Flex.Column(gap: 4)
            .Width(Fluent.NavPaneWidth).FlexShrink(0)
            .Padding(new Thickness(0, 12, 12, 12))
            .Children(
                search,
                F.NavItem(p, Icons.Home, "Home", Current == Page.Home, () => Current = Page.Home),
                F.NavItem(p, Icons.Personalize, "Personalization", Current == Page.Personalization,
                    () => Current = Page.Personalization),
                F.NavItem(p, Icons.Gallery, "Controls gallery", Current == Page.Gallery,
                    () => Current = Page.Gallery),
                F.NavItem(p, Icons.Editor, "Rich text", Current == Page.RichText,
                    () => Current = Page.RichText),
                Box.New().FlexGrow(1),
                F.NavItem(p, Icons.About, "About", Current == Page.About, () => Current = Page.About));
    }

    // ──────────────────────────────────────────────────────────────────────
    // Content frame
    // ──────────────────────────────────────────────────────────────────────

    private Element ContentFrame(FluentPalette p)
    {
        var page = Current switch
        {
            Page.Personalization => Pages.Personalization(this, p),
            Page.Gallery => Pages.Gallery(this, p),
            Page.RichText => Pages.RichText(this, p),
            Page.About => Pages.About(this, p),
            _ => Pages.Home(this, p),
        };

        // The Windows 11 bar: a 2px hairline with no track while you read, widening to a 12px
        // bar with a track when the pointer comes near. The hairline is only usable because the
        // region that counts as "on the bar" is the expanded width, not the drawn one.
        var body = ScrollView.New().FlexGrow(1)
            // Right padding rather than a framework knob: the bar is drawn at the inner edge of
            // the content box, so padding insets it. WinUI leaves the same few pixels, and
            // without them the pill's right half is clipped off by the window edge.
            .Padding(new Thickness(0, 3, 0, 0));
        body.ScrollbarWidth = 2f;                       // the resting indicator
        body.ScrollbarExpandedWidth = 12f;              // and what it grows to under the pointer
        body.ScrollbarColor = Color.Transparent;        // no track until then
        body.ScrollbarTrackHoverColor = p.ControlFill;
        body.ScrollbarThumbColor = p.ControlStrong;
        body.Children(
            Flex.Column(gap: 0).FlexShrink(0)
                // Everything on the 4px grid the Spacing page insists on — 28 was not.
                .Padding(new Thickness(32, 36, 36, 36))
                .MaxWidth(1064f)
                .Children(page));

        // One rounded corner and two borders. This is the NavigationView content frame, and
        // it is doing a surprising amount of the work: it is what separates "a window with a
        // sidebar" from "a WinUI app".
        return Box.New().FlexGrow(1)
            .Background(p.Layer)
            .CornerRadius(new CornerRadius(Fluent.OverlayRadius, 0, 0, 0))
            // LayerStroke, not CardStroke: this border sits on the backdrop rather than on the
            // pane, and WinUI's translucent stroke lands darker there.
            .Border(new Thickness(1, 0, 0, 1), p.LayerStroke)
            .Overflow(Overflow.Hidden)
            .Children(body);
    }
}
