using HultUI.Core.Accessibility;
using HultUI.Core.Components;
using HultUI.Core.Dialogs;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Styling;

namespace HultUI.Core.Builders;

/// <summary>
/// A command palette: a query field over a list of everything the app can do, narrowed as the
/// person types and run with Enter or a click. Shown as a modal dialog near the top of the window.
/// </summary>
/// <remarks>
/// <code>
/// _palette = new CommandPalette(Commands)
/// {
///     ToggleKeys = shortcuts["palette"].Keys,   // the key that opened it closes it
///     GroupStyle = PaletteGroupStyle.Trailing,
/// };
/// if (shortcuts.Matches("palette", e)) _palette.Toggle(window);
///
/// IEnumerable&lt;PaletteCommand&gt; Commands() =>
/// [
///     new("Push", DoPush) { Keys = shortcuts.Keys("push") },
///     new("main", () => Checkout("main")) { Group = "Branch", Detail = "origin/main" },
/// ];
/// </code>
/// <para><b>The list is taken when it opens</b> and filtered as the query changes, so a provider
/// can afford to count things or ask a repository; it is not called per keystroke.</para>
/// <para><b>Matching</b> is <see cref="FuzzyMatch"/> over the title, and then, ranked after every
/// title match, over the group, the detail and the keywords: "gtr" finds "Go to Runner", and a
/// branch is found by its full name in its detail line. Ties go to the shorter title, then to
/// list order.</para>
/// <para><b>Keys:</b> ↑ ↓ and Page Up/Down move, Enter runs, Esc or a click outside closes, and
/// so do <see cref="ToggleKeys"/> — the dialog is modal, so the app's own handler never sees the
/// key that opened it. The chosen row is kept in view. A long list shows its first
/// <see cref="MaxRows"/> and says how many more the query would reveal.</para>
/// <para>Its look is the theme's <see cref="CommandPaletteTheme"/>; key chips are
/// <see cref="Kbd"/>s.</para>
/// </remarks>
public sealed class CommandPalette
{
    private readonly Func<IEnumerable<PaletteCommand>> _commands;
    private List<PaletteCommand> _all = [];
    private IReadOnlyList<PaletteCommand> _matches = [];
    private string _query = "";
    private int _selected;
    private IDialogController<bool>? _dialog;
    private View? _view;
    private Theme _theme = Theme.Light;
    private CommandPaletteTheme _look = new();

    public CommandPalette(Func<IEnumerable<PaletteCommand>> commands)
    {
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
    }

    public string Placeholder { get; init; } = "Search or run a command…";

    /// <summary>What assistive technology calls the dialog.</summary>
    public string AccessibleName { get; init; } = "Command palette";

    /// <summary>Keys that close the palette again, typically the ones that opened it. The first
    /// is shown in the query row; with none, Esc is.</summary>
    public IReadOnlyList<KeyChord> ToggleKeys { get; init; } = [];

    /// <summary>Where each command's group is shown.</summary>
    public PaletteGroupStyle GroupStyle { get; init; } = PaletteGroupStyle.Trailing;

    /// <summary>A leading picture in the query row — a magnifying glass.</summary>
    public Func<Element>? SearchIcon { get; init; }

    /// <summary>A line under the list: what can be found here. Null shows none.</summary>
    public string? Footer { get; init; }

    /// <summary>The most rows shown at once; a query narrows the rest.</summary>
    public int MaxRows { get; init; } = 100;

    public bool IsOpen => _dialog is not null;

    /// <summary>Raised when the palette closes, however it closes.</summary>
    public event Action? Closed;

    /// <summary>The query. Setting it filters and goes back to the first match, as typing does.</summary>
    public string Query
    {
        get => _query;
        set
        {
            _query = value ?? "";
            _matches = Filter(_all, _query);
            _selected = 0;
            _view?.Changed(reveal: true);
        }
    }

    /// <summary>The commands the query matches, best first.</summary>
    public IReadOnlyList<PaletteCommand> Matches => _matches;

    /// <summary>The chosen row's index in <see cref="Matches"/>.</summary>
    public int SelectedIndex => _selected;

    /// <summary>Opens the palette over <paramref name="host"/>, taking the list from the provider.</summary>
    public void Open(IOverlayHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (IsOpen) return;
        _all = _commands().ToList();
        _query = "";
        _matches = _all;
        _selected = 0;
        _theme = host.ActiveTheme ?? Theme.Light;
        _look = _theme.Get<CommandPaletteTheme>();

        var shown = Dialog.Show<bool>(host, controller =>
        {
            _dialog = controller;
            _view = new View(this);
            // The dialog's panel is the host, so the name goes there: Dialog.Show makes its panel
            // the dialog, and a role on the card inside as well announced two, the outer unnamed.
            var host = _view.Mount();
            host.AccessibleRole(AccessibilityRole.Dialog).AccessibleName(AccessibleName).AutomationId("palette");
            return host;
        }, new DialogOptions { ScrimColor = _look.Scrim, TopOffset = _look.TopOffset });

        // The dialog completes its task inline as it closes — by Esc, a click outside, or
        // Close — so the palette knows at once, not a frame later.
        shown.ContinueWith(_ => OnClosed(), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    /// <summary>Closes the palette without running anything.</summary>
    public void Close() => _dialog?.Close(false);

    public void Toggle(IOverlayHost host)
    {
        if (IsOpen) Close();
        else Open(host);
    }

    /// <summary>Moves the choice by <paramref name="delta"/> rows, within the list.</summary>
    public void Move(int delta)
    {
        if (_matches.Count == 0) return;
        _selected = Math.Clamp(_selected + delta, 0, Math.Min(_matches.Count, MaxRows) - 1);
        _view?.Changed(reveal: true);
    }

    /// <summary>Closes the palette and runs the chosen command.</summary>
    public void RunSelected() => Run(_selected);

    /// <summary>Closes the palette and runs the command at <paramref name="index"/> in
    /// <see cref="Matches"/>.</summary>
    public void Run(int index)
    {
        if (index < 0 || index >= _matches.Count) return;
        var command = _matches[index];
        Close();
        command.Run();
    }

    private void OnClosed()
    {
        _dialog = null;
        _view = null;
        _all = [];
        _matches = [];
        Closed?.Invoke();
    }

    /// <summary>The commands matching <paramref name="query"/>, best first; all of them, in
    /// order, for an empty one. See the type's remarks for the ranking.</summary>
    public static IReadOnlyList<PaletteCommand> Filter(IReadOnlyList<PaletteCommand> commands, string query)
    {
        query = query.Trim();
        if (query.Length == 0) return commands;
        return commands
            .Select((command, index) => (command, index, score: ScoreOf(command, query)))
            .Where(x => x.score >= 0)
            .OrderBy(x => x.score)
            // Ties are common — "save" matches "Save" and "Save all" with the same span — and
            // the shorter title is the tighter match.
            .ThenBy(x => x.command.Title.Length)
            .ThenBy(x => x.index)
            .Select(x => x.command)
            .ToList();
    }

    /// <summary>Everything a title match scores under; a match elsewhere ranks after them all.</summary>
    private const int Elsewhere = 1_000_000;

    private static int ScoreOf(PaletteCommand command, string query)
    {
        int score = FuzzyMatch.Score(command.Title, query);
        if (score >= 0) return score;
        var rest = string.Join(" ", new[] { command.Title, command.Group, command.Detail, command.Keywords }
            .Where(s => !string.IsNullOrEmpty(s)));
        score = FuzzyMatch.Score(rest, query);
        return score < 0 ? -1 : Elsewhere + score;
    }

    // ──── View ────

    private float RowHeight(PaletteCommand command) =>
        string.IsNullOrEmpty(command.Detail) ? _look.RowHeight : _look.DetailRowHeight;

    /// <summary>Brings the chosen row into view. The rows have fixed heights, so its band is
    /// known before layout — a reveal asked for before the list is laid out is held by the
    /// scroll view until it is.</summary>
    private void RevealSelected(ScrollViewElement? scroll)
    {
        if (scroll is null || _selected >= _matches.Count) return;
        float top = _look.ListPadding.Top;
        for (int i = 0; i < _selected; i++) top += RowHeight(_matches[i]);
        float height = RowHeight(_matches[_selected]);
        // The first row takes the list's top padding with it, so going back to it scrolls all
        // the way up rather than stopping a few pixels short.
        if (_selected == 0) { height += top; top = 0; }
        scroll.ScrollRangeIntoView(top, height);
    }

    private void OnKey(KeyDownEvent e)
    {
        foreach (var chord in ToggleKeys)
            if (chord.Matches(e)) { e.StopPropagation = true; Close(); return; }
        if (e.Modifiers is not (KeyModifiers.None or KeyModifiers.NumLock or KeyModifiers.CapsLock)) return;
        int page = Math.Max(1, (int)((_look.MaxHeight - _look.HeaderHeight) / _look.RowHeight) - 1);
        switch (e.Key)
        {
            case KeyCode.Down: Move(1); break;
            case KeyCode.Up: Move(-1); break;
            case KeyCode.PageDown: Move(page); break;
            case KeyCode.PageUp: Move(-page); break;
            case KeyCode.Enter or KeyCode.NumpadEnter: RunSelected(); break;
            default: return;
        }
        e.StopPropagation = true;
    }

    private Element BuildCard(Ref<ScrollViewElement> scrollRef)
    {
        var t = _look;
        var divider = t.Divider ?? _theme.Border;
        var muted = t.Muted ?? _theme.Secondary;

        var card = Flex.Column(gap: 0)
            .Width(t.Width).MaxHeight(t.MaxHeight)
            .Background(t.Background ?? _theme.Surface)
            .BorderColor(t.Border ?? _theme.Border).BorderWidth(1f)
            .CornerRadius(t.CornerRadius)
            .AutomationId("palette-card");
        card.On<KeyDownEvent>(OnKey);

        // The query row. The height is pinned so the row does not collapse to the empty field's
        // measured height; FlexBasis(0) lets the field take the rest of the width.
        var header = Flex.Row(gap: 10).AlignItems(AlignItems.Center)
            .Height(t.HeaderHeight).FlexShrink(0)
            .Padding(new Thickness(0, 14, 0, 14))
            .BorderColor(divider).BorderWidth(new Thickness(0, 0, 1, 0));
        if (SearchIcon is { } icon) header.AddChild(icon().FlexShrink(0));
        var field = TextInput.New(_query)
            .PlaceholderText(Placeholder)
            .OnTextChanged(q => Query = q)
            .FontSize(t.FieldFontSize)
            .Height(Dimension.Px(30))
            .FlexGrow(1).FlexShrink(1).FlexBasis(Dimension.Px(0))
            .AutomationId("palette-input");
        if (t.Field is { } fieldStyle) field.Style(fieldStyle);
        header.AddChild(field);
        header.AddChild(Kbd.New(ToggleKeys.Count > 0 ? ToggleKeys[0].ToString() : "Esc").FlexShrink(0));
        card.AddChild(header);

        var rows = Flex.Column(gap: 0).Padding(t.ListPadding);
        if (_matches.Count == 0)
        {
            rows.AddChild(Text.New(_query.Length == 0 ? "Nothing to run here" : $"No matches for \"{_query}\"")
                .FontSize(t.MutedFontSize).TextColor(muted)
                .Padding(new Thickness(12))
                .AutomationId("palette-no-match"));
        }
        int shown = Math.Min(_matches.Count, MaxRows);
        for (int i = 0; i < shown; i++)
            rows.AddChild(BuildRow(_matches[i], i));
        if (_matches.Count > shown)
            rows.AddChild(Text.New($"{_matches.Count - shown} more — type to narrow them down")
                .FontSize(t.MutedFontSize).TextColor(muted).Padding(new Thickness(8, 10, 8, 10)));
        // Sized to its rows, and shrinking once the card reaches its height: growing it filled the
        // card to its full height for a list of one.
        card.AddChild(ScrollView.New().FlexShrink(1).WithRef(scrollRef).Children(rows));

        if (Footer is { } footer)
            card.AddChild(Flex.Row(gap: 0).FlexShrink(0)
                .Padding(new Thickness(8, 14, 8, 14))
                .BorderColor(divider).BorderWidth(new Thickness(1, 0, 0, 0))
                .Children(Text.New(footer).FontSize(t.MutedFontSize).TextColor(muted)));
        return card;
    }

    private Element BuildRow(PaletteCommand command, int index)
    {
        var t = _look;
        bool selected = index == _selected;
        var normal = selected ? t.SelectedBackground ?? _theme.Primary with { A = 36 } : Color.Transparent;
        var row = Flex.Row(gap: t.RowGap).AlignItems(AlignItems.Center)
            .Height(RowHeight(command)).FlexShrink(0)
            .Padding(t.RowPadding).CornerRadius(t.RowCornerRadius)
            .Background(normal)
            .Cursor(CursorType.Pointer)
            .AccessibleRole(AccessibilityRole.Button)
            .AccessibleName(command.Group is { Length: > 0 } g ? $"{command.Title} ({g})" : command.Title)
            .AutomationId($"palette-row-{index}");
        // Matched by identity, not position, so a narrowed list does not hand a row the click
        // handler of the one that used to be in its place.
        row.Key = $"{command.Group}|{command.Title}|{index}";

        var groupColor = t.Group ?? _theme.Secondary;
        if (GroupStyle == PaletteGroupStyle.Leading && command.Group is { } leading)
        {
            var column = Ellipsis(Text.New(leading).FontSize(t.GroupFontSize).TextColor(groupColor)
                .Width(t.GroupWidth).FlexShrink(0));
            if (t.GroupFontFamily is { } family) column.FontFamily(family);
            row.AddChild(column);
        }
        if (command.Icon is { } icon) row.AddChild(icon().FlexShrink(0));

        // Centred by the row, not by JustifyContent: on an auto-height column that measured the
        // column a half row taller and set the title on its floor.
        var text = Flex.Column(gap: 1).FlexGrow(1).FlexShrink(1);
        var title = Ellipsis(Text.New(command.Title).FontSize(t.TitleFontSize));
        if (t.Title is { } titleColor) title.TextColor(titleColor);
        text.AddChild(title);
        if (command.Detail is { Length: > 0 } detail)
            text.AddChild(Ellipsis(Text.New(detail).FontSize(t.DetailFontSize).TextColor(t.Detail ?? _theme.Secondary)));
        row.AddChild(text);

        if (GroupStyle == PaletteGroupStyle.Trailing && command.Group is { } trailing)
        {
            var tag = Text.New(trailing).FontSize(t.GroupFontSize).TextColor(groupColor);
            if (t.GroupFontFamily is { } family) tag.FontFamily(family);
            row.AddChild(Box.New().FlexShrink(0).Padding(new Thickness(1, 6, 1, 6)).CornerRadius(4f)
                .Background(t.GroupBackground ?? _theme.SurfaceVariant).Children(tag));
        }
        if (command.Keys is { Length: > 0 } keys) row.AddChild(Kbd.New(keys).FlexShrink(0));

        row.OnClick(_ => Run(index));
        if (!selected)
        {
            var hover = t.HoverBackground ?? _theme.SurfaceVariant;
            row.OnHover(b => b.BackgroundColor = hover);
        }
        return row;
    }

    /// <summary>One line, cut with an ellipsis: a row's height is fixed, so a label must not wrap.</summary>
    private static TextElement Ellipsis(TextElement text)
    {
        text.MaxLines(1);
        text.Overflow = TextOverflow.Ellipsis;
        return text;
    }

    /// <summary>The palette's content: rebuilt from the palette's state whenever it changes.</summary>
    private sealed class View(CommandPalette palette) : Component
    {
        private readonly Ref<ScrollViewElement> _scroll = new();
        private bool _reveal;

        public void Changed(bool reveal)
        {
            _reveal |= reveal;
            MarkDirty();
        }

        public override Element Build() => palette.BuildCard(_scroll);

        protected internal override void OnAfterBuild()
        {
            if (!_reveal) return;
            _reveal = false;
            palette.RevealSelected(_scroll.Current);
        }
    }
}
