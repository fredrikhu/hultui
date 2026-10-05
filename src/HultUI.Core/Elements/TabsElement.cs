using HultUI.Core.Accessibility;
using HultUI.Core.Animation;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;

// The element's own Flex/Box properties shadow the builder classes inside this
// type, so alias the builders.
using BoxBuild = HultUI.Core.Builders.Box;
using TextBuild = HultUI.Core.Builders.Text;

namespace HultUI.Core.Elements;

public enum TabStyle : byte
{
    Underline,
    Pill,
    Enclosed
}

public enum TabPlacement : byte
{
    Top,
    Bottom,
    Left,
    Right
}

/// <summary>A single tab: its header text (plus optional badge/close) and a factory
/// for the panel shown when it is selected.</summary>
public sealed class TabItem
{
    public required string Label { get; init; }
    public required Func<Element> Content { get; init; }
    public string? Badge { get; init; }
    /// <summary>When set, the tab shows a close affordance that runs this.</summary>
    public Action? OnClose { get; init; }
    /// <summary>Keep the built panel alive across tab switches (preserves its state).</summary>
    public bool KeepAlive { get; init; }
}

/// <summary>
/// A tabbed container: a strip of tab headers plus the selected tab's content.
/// Follows the built-in-widget pattern — a custom element that themes itself via
/// <see cref="ApplyTheme"/>. Supports three styles and four placements.
/// </summary>
/// <remarks>
/// <para><b>Keyboard.</b> The ARIA tab pattern: Tab stops at the open tab only; the arrow keys
/// along the strip (Left and Right, or Up and Down for a strip down the side) go to the next
/// tab round the ends and open it; Home and End go to the first and last; Enter and Space open
/// a tab that has focus without being open; Delete closes a closable one.</para>
///
/// <para><b>Elements.</b> The element owns its children (<see cref="ManagesOwnChildren"/>) and
/// keeps them: the strip, a header for each tab, and the panel's host. A switch restyles two
/// headers and swaps the panel, so a reader hears the selection move rather than the tabs being
/// replaced, and the keyboard stays where it is. A rebuild of the app patches the open panel
/// from the tab's function, as it would any child, so what is in it keeps its state.</para>
///
/// <para><b>Which tab is which.</b> Across a rebuild a tab is known by its label, or, when no
/// tab came or went, by its place, so a renamed tab is still itself. Its header, its kept panel
/// and the selection go with it: the open tab stays open when tabs close around it, and when it
/// closes, the tab in its place opens. A panel is only ever patched from its own tab's.</para>
/// </remarks>
public sealed class TabsElement : BoxElement
{
    private readonly List<TabItem> _tabs = [];
    private readonly BoxElement _strip = new();
    private readonly List<TabHeaderElement> _headers = [];
    private readonly BoxElement _panelHost = new();
    private Element? _panel;
    /// <summary>The tab the panel showing was built for.</summary>
    private PanelKey _panelKey;
    private readonly Dictionary<PanelKey, Element> _aliveContent = [];
    private TabStyle _style = TabStyle.Underline;
    private TabPlacement _placement = TabPlacement.Top;
    private int _selectedIndex;
    private bool _indexExplicitlySet;
    private Action<int>? _onSelectionChanged;
    private Theme _theme = Theme.Light;
    private bool _built;
    /// <summary>A header that had the keyboard went with its tab; the open tab takes it.</summary>
    private bool _keyboardLeft;

    public TabsElement()
    {
        _strip.Role = AccessibilityRole.TabList;
        _strip.Flex.FlexShrink = 0;
        _panelHost.Role = AccessibilityRole.TabPanel;
        _panelHost.Flex.FlexGrow = 1;
    }

    public new static TabsElement New([OriginFile] string? file = null, [OriginLine] int line = 0) => new TabsElement().WithOrigin(file, line);

    public int SelectedIndex => _selectedIndex;

    internal override bool ManagesOwnChildren => true;

    // ──── Fluent configuration ────

    public TabsElement Tab(string label, Func<Element> content, string? badge = null, bool keepAlive = false, Action? onClose = null) =>
        Tab(new TabItem { Label = label, Content = content, Badge = badge, KeepAlive = keepAlive, OnClose = onClose });

    public TabsElement Tab(TabItem tab)
    {
        _tabs.Add(tab);
        if (_built) Sync();
        return this;
    }

    public TabsElement Style(TabStyle style) { _style = style; if (_built) Sync(); return this; }
    public TabsElement Placement(TabPlacement placement) { _placement = placement; if (_built) Sync(); return this; }
    public TabsElement Selected(int index) { _selectedIndex = index; _indexExplicitlySet = true; if (_built) Sync(); return this; }
    public TabsElement OnTabChanged(Action<int> handler) { _onSelectionChanged = handler; return this; }

    // ──── Theming + reconciliation ────

    protected internal override void ApplyTheme(Theme theme)
    {
        // First theming is where the colours become known, and a theme swap changes them;
        // the parts are styled here so they resolve in the same style pass.
        if (_built && ReferenceEquals(theme, _theme)) return;
        _theme = theme;
        Sync();
    }

    internal override void PatchFrom(Element source)
    {
        base.PatchFrom(source);
        if (source is not TabsElement tabs) return;

        var before = Keys();
        _tabs.Clear();
        _tabs.AddRange(tabs._tabs);
        _style = tabs._style;
        _placement = tabs._placement;
        _onSelectionChanged = tabs._onSelectionChanged;
        var after = Keys();
        var moved = Follow(before, after);

        // Adopt an explicitly-controlled index; otherwise the open tab stays open wherever it
        // went, and when it went altogether the tab now in its place opens.
        if (tabs._indexExplicitlySet)
            _selectedIndex = tabs._selectedIndex;
        else if (_selectedIndex < moved.Length && moved[_selectedIndex] >= 0)
            _selectedIndex = moved[_selectedIndex];
        _selectedIndex = Math.Clamp(_selectedIndex, 0, Math.Max(0, _tabs.Count - 1));

        FollowPanels(before, after, moved);
        if (!_built) return;
        FollowHeaders(moved);
        PatchOpenPanel();
        Sync();
    }

    /// <summary>Which tab each tab is: its label, and which of the tabs with that label.</summary>
    private List<PanelKey> Keys()
    {
        var keys = new List<PanelKey>(_tabs.Count);
        for (int i = 0; i < _tabs.Count; i++)
            keys.Add(KeyOf(i));
        return keys;
    }

    /// <summary>
    /// Where each tab of the last build is in this one, or -1 for one that is gone. A tab is
    /// the one with the same label; and when no tab came or went, a tab whose label is new is
    /// the one that was in its place, renamed — a "•" for unsaved changes, say.
    /// </summary>
    private static int[] Follow(List<PanelKey> before, List<PanelKey> after)
    {
        var moved = new int[before.Count];
        for (int i = 0; i < before.Count; i++)
            moved[i] = after.IndexOf(before[i]);
        if (before.Count == after.Count)
            for (int i = 0; i < moved.Length; i++)
                if (moved[i] < 0 && !before.Contains(after[i]))
                    moved[i] = i;
        return moved;
    }

    /// <summary>The headers go with their tabs, so a closed tab's header is the one that goes,
    /// and the one with the keyboard on it keeps it wherever it moved.</summary>
    private void FollowHeaders(int[] moved)
    {
        var next = new TabHeaderElement?[_tabs.Count];
        for (int i = 0; i < _headers.Count; i++)
        {
            if (i < moved.Length && moved[i] >= 0)
                next[moved[i]] = _headers[i];
            else
                _keyboardLeft |= _headers[i].IsFocused;
        }
        _headers.Clear();
        foreach (var header in next)
            _headers.Add(header ?? new TabHeaderElement(this));
    }

    /// <summary>The open panel and the kept ones go with their tabs too; one whose tab is gone
    /// is no tab's.</summary>
    private void FollowPanels(List<PanelKey> before, List<PanelKey> after, int[] moved)
    {
        PanelKey? Next(PanelKey key)
        {
            int i = before.IndexOf(key);
            return i >= 0 && moved[i] >= 0 ? after[moved[i]] : null;
        }

        if (_panel is not null)
            _panelKey = Next(_panelKey) ?? default;

        if (_aliveContent.Count == 0) return;
        var kept = new List<(PanelKey, Element)>(_aliveContent.Count);
        foreach (var (key, panel) in _aliveContent)
            if (Next(key) is { } now && _tabs[after.IndexOf(now)].KeepAlive)
                kept.Add((now, panel));
        _aliveContent.Clear();
        foreach (var (key, panel) in kept)
            _aliveContent[key] = panel;
    }

    internal void SetSelected(int index)
    {
        if (index < 0 || index >= _tabs.Count || index == _selectedIndex) return;
        _selectedIndex = index;
        Sync();
        _onSelectionChanged?.Invoke(index);
    }

    private void Open(int index, bool focus)
    {
        SetSelected(index);
        if (focus && index < _headers.Count)
            _headers[index].Focus();
    }

    private void Close(int index)
    {
        if (index < _tabs.Count)
            _tabs[index].OnClose?.Invoke();
    }

    private bool Horizontal => _placement is TabPlacement.Top or TabPlacement.Bottom;

    // ──── The parts, kept and brought in line ────

    /// <summary>Brings the parts in line with the tabs, the style, the placement and the
    /// selection, in place.</summary>
    private void Sync()
    {
        _built = true;
        _selectedIndex = _tabs.Count == 0 ? 0 : Math.Clamp(_selectedIndex, 0, _tabs.Count - 1);

        bool horizontal = Horizontal;
        Flex.Direction = horizontal ? FlexDirection.Column : FlexDirection.Row;
        StyleStrip(horizontal);
        StylePanelHost();

        // One header per tab. A header that goes with the keyboard on it hands the keyboard
        // to the open tab: the tab it was on has just been closed, most likely with Delete,
        // and the keyboard user is still in the strip.
        while (_headers.Count < _tabs.Count)
            _headers.Add(new TabHeaderElement(this));
        if (!SameChildren(_strip, _headers))
            _strip.ReplaceChildren([.. _headers]);
        for (int i = 0; i < _headers.Count; i++)
            _headers[i].Show(i, _tabs[i], i == _selectedIndex);

        ShowPanel();
        _panelHost.AccessibleName = _tabs.Count == 0 ? null : _tabs[_selectedIndex].Label;
        Arrange();
        MarkLayoutDirty();
        MarkPaintDirty();

        if (_keyboardLeft)
        {
            _keyboardLeft = false;
            if (_headers.Count > 0) _headers[_selectedIndex].Focus();
        }
    }

    private void Arrange()
    {
        bool stripFirst = _placement is TabPlacement.Top or TabPlacement.Left;
        Element first = stripFirst ? _strip : _panelHost;
        Element second = stripFirst ? _panelHost : _strip;
        if (Children.Count != 2 || !ReferenceEquals(Children[0], first) || !ReferenceEquals(Children[1], second))
            ReplaceChildren(first, second);
    }

    private static bool SameChildren(Element parent, List<TabHeaderElement> children)
    {
        if (parent.Children.Count != children.Count) return false;
        for (int i = 0; i < children.Count; i++)
            if (!ReferenceEquals(parent.Children[i], children[i])) return false;
        return true;
    }

    private void StyleStrip(bool horizontal)
    {
        _strip.Flex.Direction = horizontal ? FlexDirection.Row : FlexDirection.Column;
        _strip.Flex.Gap = horizontal && _style == TabStyle.Enclosed ? 4f : 2f;
        _strip.BackgroundColor = Color.Transparent;
        _strip.CornerRadius = default;
        _strip.Box.Padding = default;
        _strip.Flex.AlignSelf = AlignSelf.Auto;
        _strip.BorderColor = Color.Transparent;
        _strip.Box.BorderWidth = default;

        if (_style == TabStyle.Pill)
        {
            _strip.BackgroundColor = _theme.SurfaceVariant;
            _strip.CornerRadius = new CornerRadius(8f);
            _strip.Box.Padding = new Thickness(4f);
            // Pill strips hug their tabs rather than stretching the whole edge.
            _strip.Flex.AlignSelf = AlignSelf.FlexStart;
        }
        else if (_style == TabStyle.Underline)
        {
            // A divider along the content edge of the strip.
            _strip.BorderColor = _theme.Border;
            _strip.Box.BorderWidth = DividerBorder();
        }
        _strip.MarkLayoutDirty();
    }

    private void StylePanelHost()
    {
        bool enclosed = _style == TabStyle.Enclosed;
        _panelHost.BackgroundColor = enclosed ? _theme.Surface : Color.Transparent;
        _panelHost.BorderColor = enclosed ? _theme.Border : Color.Transparent;
        _panelHost.Box.BorderWidth = enclosed ? new Thickness(1) : default;
        _panelHost.MarkLayoutDirty();
    }

    private void StyleHeader(BoxElement header, bool active)
    {
        // Every property any style sets, back to nothing first, so a style change at run time
        // leaves nothing of the last one behind.
        header.BackgroundColor = Color.Transparent;
        header.CornerRadius = default;
        header.BorderColor = Color.Transparent;
        header.Box.BorderWidth = default;

        switch (_style)
        {
            case TabStyle.Underline:
                // Reserve the indicator's width on every header (transparent when
                // inactive) so activating doesn't grow the box and shift the label.
                header.Box.BorderWidth = ActiveEdge(2f);
                header.BorderColor = active ? _theme.Primary : Color.Transparent;
                break;

            case TabStyle.Pill:
                // Background/radius are paint-only, so no layout shift to reserve for.
                if (active)
                {
                    header.BackgroundColor = _theme.Surface;
                    header.CornerRadius = new CornerRadius(6f);
                }
                break;

            case TabStyle.Enclosed:
                // Same reasoning as Underline — reserve the border space always.
                header.CornerRadius = EnclosedCorners();
                header.Box.BorderWidth = EnclosedActiveBorder();
                header.BorderColor = active ? _theme.Border : Color.Transparent;
                if (active)
                    header.BackgroundColor = _theme.Surface;
                break;
        }
        header.MarkLayoutDirty();
    }

    // ──── The panel ────

    /// <summary>Which tab a panel is: its label, and which of the tabs with that label.</summary>
    private readonly record struct PanelKey(string Label, int Nth);

    private PanelKey KeyOf(int index)
    {
        string label = _tabs[index].Label;
        int nth = 0;
        for (int i = 0; i < index; i++)
            if (_tabs[i].Label == label) nth++;
        return new PanelKey(label, nth);
    }

    /// <summary>Shows the open tab's panel: the one showing if it is that tab's, a kept one,
    /// or a new one from the tab's function.</summary>
    private void ShowPanel()
    {
        if (_tabs.Count == 0)
        {
            SetPanel(null, default);
            return;
        }

        var key = KeyOf(_selectedIndex);
        if (_panel is not null && _panelKey == key) return;

        var tab = _tabs[_selectedIndex];
        if (!tab.KeepAlive || !_aliveContent.TryGetValue(key, out var panel))
        {
            panel = tab.Content();
            if (tab.KeepAlive) _aliveContent[key] = panel;
        }
        SetPanel(panel, key);
    }

    /// <summary>A rebuild: the open panel is patched from what the tab's function builds now,
    /// as a child in any build is, when the open tab is still the one it was built for.</summary>
    private void PatchOpenPanel()
    {
        if (_panel is null || _tabs.Count == 0 || KeyOf(_selectedIndex) != _panelKey) return;

        var tab = _tabs[_selectedIndex];
        var incoming = tab.Content();
        var next = TreeDiffer.TryPatch(_panel, incoming) ? _panel : incoming;
        if (ReferenceEquals(next, _panel)) return;
        if (tab.KeepAlive) _aliveContent[_panelKey] = next;
        SetPanel(next, _panelKey);
    }

    private void SetPanel(Element? panel, PanelKey key)
    {
        _panelKey = key;
        if (ReferenceEquals(panel, _panel)) return;
        // A panel that leaves with the keyboard in it hands the keyboard to the strip, as an
        // expander's closing body does to its header: its tab closed, most likely by a shortcut.
        if (_panel is not null && FocusManager?.FocusedElement is { } focused && Contains(_panel, focused))
            _keyboardLeft = true;
        _panel = panel;
        if (panel is null) _panelHost.ReplaceChildren();
        else _panelHost.ReplaceChildren(panel);
    }

    private static bool Contains(Element root, Element element)
    {
        for (Element? e = element; e is not null; e = e.Parent)
            if (ReferenceEquals(e, root)) return true;
        return false;
    }

    // ──── Look ────

    private Color ActiveTextColor() => _style == TabStyle.Underline ? _theme.Primary : _theme.TextColor;

    // The active-indicator/border sits on the edge facing the content.
    private Thickness ActiveEdge(float w) => _placement switch
    {
        TabPlacement.Top => new Thickness(0, 0, w, 0),
        TabPlacement.Bottom => new Thickness(w, 0, 0, 0),
        TabPlacement.Left => new Thickness(0, w, 0, 0),
        _ => new Thickness(0, 0, 0, w),
    };

    private Thickness DividerBorder() => ActiveEdge(1f);

    // Enclosed active tab: bordered on every side except the one meeting the content.
    private Thickness EnclosedActiveBorder() => _placement switch
    {
        TabPlacement.Top => new Thickness(1, 1, 0, 1),
        TabPlacement.Bottom => new Thickness(0, 1, 1, 1),
        TabPlacement.Left => new Thickness(1, 0, 1, 1),
        _ => new Thickness(1, 1, 1, 0),
    };

    private CornerRadius EnclosedCorners() => _placement switch
    {
        TabPlacement.Top => new CornerRadius(6, 6, 0, 0),
        TabPlacement.Bottom => new CornerRadius(0, 0, 6, 6),
        TabPlacement.Left => new CornerRadius(6, 0, 0, 6),
        _ => new CornerRadius(0, 6, 6, 0),
    };

    // ──── A tab's header ────

    /// <summary>
    /// One tab in the strip: its label, a badge and a close button when the tab has them. A tab
    /// to assistive technology, selected while open, and the keyboard's way round the strip.
    /// </summary>
    private sealed class TabHeaderElement : BoxElement
    {
        private const KeyModifiers Chord = KeyModifiers.Ctrl | KeyModifiers.Alt | KeyModifiers.Super;

        private readonly TabsElement _owner;
        private readonly TextElement _label = TextElement.New("");
        private BoxElement? _badge;
        private TextElement? _badgeText;
        private BoxElement? _close;
        private TextElement? _closeText;
        private bool _active;

        /// <summary>Which tab this header is, by position.</summary>
        public int Index { get; private set; }

        public TabHeaderElement(TabsElement owner)
        {
            _owner = owner;
            IsFocusable = true;
            ShowsFocusVisual = true;
            Role = AccessibilityRole.Tab;
            Flex.Direction = FlexDirection.Row;
            Flex.Gap = 7f;
            Flex.AlignItems = AlignItems.Center;
            Box.Padding = new Thickness(7, 14, 7, 14);
            Inherited.Cursor = CursorType.Pointer;

            // Keep the weight constant across active/inactive so the label width never
            // changes — otherwise switching tabs reflows the ones after it. Active is
            // distinguished by colour and the style indicator instead.
            _label.Inherited.FontSize = 13f;
            _label.Inherited.FontWeight = FontWeight.SemiBold;
            AddChild(_label);

            // Static and reading the element handed in: an open tab does not light up.
            AddHoverStyle(static e => ((TabHeaderElement)e).Lit());
            AddTransition(TransitionProperty.Background, 0.12f, Easing.EaseOutCubic);

            OnOwn<ClickEvent>(_ => _owner.Open(Index, focus: false));
            OnOwn<KeyDownEvent>(OnKey);
        }

        public void Show(int index, TabItem tab, bool active)
        {
            Index = index;
            _active = active;
            var theme = _owner._theme;

            // Only the open tab is in the Tab order; the arrows reach the rest.
            TabIndex = active ? 0 : -1;
            AccessibleSelected = active;
            // Named from its text, label and badge; a closable tab outright, or the close
            // button's "×" would join the name.
            AccessibleName = tab.OnClose is null ? null : tab.Badge is { } count ? $"{tab.Label} {count}" : tab.Label;

            _label.Content = tab.Label;
            _label.Inherited.TextColor = active ? _owner.ActiveTextColor() : theme.Secondary;
            _label.MarkStyleDirty();

            if (tab.Badge is { } badge)
            {
                EnsureBadge();
                _badgeText!.Content = badge;
                _badge!.BackgroundColor = active ? theme.Primary : theme.SurfaceVariant;
                _badgeText.Inherited.TextColor = active ? theme.PrimaryText : theme.Secondary;
                _badgeText.MarkStyleDirty();
            }

            if (tab.OnClose is not null)
            {
                EnsureClose();
                _close!.AccessibleName = $"Close {tab.Label}";
                _closeText!.Inherited.TextColor = theme.Secondary;
                _closeText.MarkStyleDirty();
            }

            ArrangeParts(tab.Badge is not null, tab.OnClose is not null);
            _owner.StyleHeader(this, active);
            // That is the rest look now; a header under the pointer keeps its hover on it.
            RefreshInteractionAfterPatch();
        }

        private void Lit()
        {
            if (!_active) BackgroundColor = _owner._theme.SurfaceVariant;
        }

        private void OnKey(KeyDownEvent e)
        {
            if (!ReferenceEquals(e.Target, this) || (e.Modifiers & Chord) != 0) return;

            int last = _owner._tabs.Count - 1;
            bool along = _owner.Horizontal;
            int? to = e.Key switch
            {
                KeyCode.Right when along => Index >= last ? 0 : Index + 1,
                KeyCode.Down when !along => Index >= last ? 0 : Index + 1,
                KeyCode.Left when along => Index <= 0 ? last : Index - 1,
                KeyCode.Up when !along => Index <= 0 ? last : Index - 1,
                KeyCode.Home => 0,
                KeyCode.End => last,
                KeyCode.Enter or KeyCode.NumpadEnter or KeyCode.Space => Index,
                _ => null,
            };

            if (to is { } index && last >= 0)
            {
                _owner.Open(index, focus: true);
                e.StopPropagation = true;
            }
            else if (e.Key == KeyCode.Delete && _owner._tabs[Index].OnClose is not null)
            {
                _owner.Close(Index);
                e.StopPropagation = true;
            }
        }

        private void ArrangeParts(bool badge, bool close)
        {
            int count = 1 + (badge ? 1 : 0) + (close ? 1 : 0);
            bool same = Children.Count == count
                && (!badge || ReferenceEquals(Children[1], _badge))
                && (!close || ReferenceEquals(Children[^1], _close));
            if (same) return;

            var parts = new List<Element>(count) { _label };
            if (badge) parts.Add(_badge!);
            if (close) parts.Add(_close!);
            ReplaceChildren([.. parts]);
        }

        private void EnsureBadge()
        {
            if (_badge is not null) return;
            _badgeText = TextBuild.New("").FontSize(11f).FontWeight(FontWeight.SemiBold);
            _badge = BoxBuild.New()
                .Padding(new Thickness(1, 7, 1, 7)).CornerRadius(999f)
                .Children(_badgeText);
        }

        private void EnsureClose()
        {
            if (_close is not null) return;
            _closeText = TextBuild.New("×").FontSize(13f);
            _close = BoxBuild.New()
                .Width(16).Height(16).CornerRadius(4f)
                .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
                .Cursor(CursorType.Pointer)
                .OnHover(c => c.Background(_owner._theme.SurfaceVariant))
                .Children(_closeText);
            _close.OnOwn<ClickEvent>(e => { e.StopPropagation = true; _owner.Close(Index); });
        }
    }
}

public static class Tabs
{
    public static TabsElement New([OriginFile] string? file = null, [OriginLine] int line = 0) => TabsElement.New(file, line);
}
