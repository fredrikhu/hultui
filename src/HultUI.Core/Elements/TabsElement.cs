using HultUI.Core.Animation;
using HultUI.Core.Builders;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;

// The element's own Flex/Box properties shadow the builder classes inside this
// type, so alias the builders.
using FlexBuild = HultUI.Core.Builders.Flex;
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
/// <see cref="ApplyTheme"/> and owns its subtree, re-asserting it after
/// reconciliation. Supports three styles and four placements.
/// </summary>
public sealed class TabsElement : BoxElement
{
    private readonly List<TabItem> _tabs = [];
    private readonly Dictionary<int, Element> _aliveContent = [];
    private TabStyle _style = TabStyle.Underline;
    private TabPlacement _placement = TabPlacement.Top;
    private int _selectedIndex;
    private bool _indexExplicitlySet;
    private Action<int>? _onSelectionChanged;
    private Theme _theme = Theme.Light;
    private bool _built;

    public new static TabsElement New([OriginFile] string? file = null, [OriginLine] int line = 0) => new TabsElement().WithOrigin(file, line);

    public int SelectedIndex => _selectedIndex;

    // ──── Fluent configuration ────

    public TabsElement Tab(string label, Func<Element> content, string? badge = null, bool keepAlive = false, Action? onClose = null)
    {
        _tabs.Add(new TabItem { Label = label, Content = content, Badge = badge, KeepAlive = keepAlive, OnClose = onClose });
        return this;
    }

    public TabsElement Tab(TabItem tab)
    {
        _tabs.Add(tab);
        return this;
    }

    public TabsElement Style(TabStyle style) { _style = style; return this; }
    public TabsElement Placement(TabPlacement placement) { _placement = placement; return this; }
    public TabsElement Selected(int index) { _selectedIndex = index; _indexExplicitlySet = true; return this; }
    public TabsElement OnTabChanged(Action<int> handler) { _onSelectionChanged = handler; return this; }

    // ──── Theming + subtree ownership ────

    protected internal override void ApplyTheme(Theme theme)
    {
        // First theming (or a theme swap) is where colours become known; (re)build
        // the subtree here so its children are resolved in the same style pass.
        if (!_built || !ReferenceEquals(theme, _theme))
        {
            _theme = theme;
            RebuildSubtree();
        }
    }

    internal override void OnReconciled()
    {
        // Reconciliation may have patched our headers/content toward an incoming
        // build; re-assert the live selection as the final word.
        RebuildSubtree();
    }

    internal override void PatchFrom(Element source)
    {
        base.PatchFrom(source);
        if (source is TabsElement tabs)
        {
            _tabs.Clear();
            _tabs.AddRange(tabs._tabs);
            _style = tabs._style;
            _placement = tabs._placement;
            _onSelectionChanged = tabs._onSelectionChanged;
            // Adopt an explicitly-controlled index; otherwise keep our live one.
            if (tabs._indexExplicitlySet)
                _selectedIndex = tabs._selectedIndex;
            _selectedIndex = Math.Clamp(_selectedIndex, 0, Math.Max(0, _tabs.Count - 1));
            // The rebuild happens in OnReconciled once children are reconciled.
        }
    }

    internal void SetSelected(int index)
    {
        if (index < 0 || index >= _tabs.Count || index == _selectedIndex) return;
        _selectedIndex = index;
        RebuildSubtree();
        MarkStyleDirty();
        MarkLayoutDirty();
        MarkPaintDirty();
        _onSelectionChanged?.Invoke(index);
    }

    private void RebuildSubtree()
    {
        _built = true;
        _selectedIndex = _tabs.Count == 0 ? 0 : Math.Clamp(_selectedIndex, 0, _tabs.Count - 1);

        bool horizontalStrip = _placement is TabPlacement.Top or TabPlacement.Bottom;
        Flex.Direction = horizontalStrip ? FlexDirection.Column : FlexDirection.Row;

        var strip = BuildStrip(horizontalStrip);
        var content = BuildContent();

        bool stripFirst = _placement is TabPlacement.Top or TabPlacement.Left;
        if (stripFirst)
            ReplaceChildren(strip, content);
        else
            ReplaceChildren(content, strip);
    }

    private Element BuildStrip(bool horizontal)
    {
        var strip = (horizontal ? FlexBuild.Row(gap: _style == TabStyle.Enclosed ? 4f : 2f) : FlexBuild.Column(gap: 2f))
            .FlexShrink(0);

        if (_style == TabStyle.Pill)
        {
            strip.Background(_theme.SurfaceVariant).CornerRadius(8f).Padding(4f);
            // Pill strips hug their tabs rather than stretching the whole edge.
            strip.AlignSelf(AlignSelf.FlexStart);
        }
        else if (_style == TabStyle.Underline)
        {
            // A divider along the content edge of the strip.
            strip.BorderColor(_theme.Border).BorderWidth(DividerBorder());
        }

        for (int i = 0; i < _tabs.Count; i++)
            strip.AddChild(BuildHeader(i));

        return strip;
    }

    private Element BuildHeader(int index)
    {
        var tab = _tabs[index];
        bool active = index == _selectedIndex;

        var header = BoxBuild.New()
            .FlexDirection(FlexDirection.Row).Gap(7f)
            .Padding(new Thickness(7, 14, 7, 14))
            .AlignItems(AlignItems.Center)
            .Cursor(CursorType.Pointer);

        ApplyHeaderStyle(header, active);

        if (!active)
            header.OnHover(h => h.Background(_theme.SurfaceVariant)).Transition(TransitionProperty.Background, 120f);

        // Keep the weight constant across active/inactive so the label width never
        // changes — otherwise switching tabs reflows the ones after it. Active is
        // distinguished by colour and the style indicator instead.
        header.AddChild(TextBuild.New(tab.Label).FontSize(13f)
            .FontWeight(FontWeight.SemiBold)
            .TextColor(active ? ActiveTextColor() : _theme.Secondary));

        if (tab.Badge is { } badge)
            header.AddChild(BuildBadge(badge, active));

        if (tab.OnClose is { } onClose)
            header.AddChild(BuildClose(onClose));

        header.OnOwn<ClickEvent>(_ => SetSelected(index));
        return header;
    }

    private void ApplyHeaderStyle(BoxElement header, bool active)
    {
        switch (_style)
        {
            case TabStyle.Underline:
                // Reserve the indicator's width on every header (transparent when
                // inactive) so activating doesn't grow the box and shift the label.
                header.BorderWidth(ActiveEdge(2f))
                      .BorderColor(active ? _theme.Primary : Color.Transparent);
                break;

            case TabStyle.Pill:
                // Background/radius are paint-only, so no layout shift to reserve for.
                if (active)
                    header.Background(_theme.Surface).CornerRadius(6f);
                break;

            case TabStyle.Enclosed:
                // Same reasoning as Underline — reserve the border space always.
                header.CornerRadius(EnclosedCorners())
                      .BorderWidth(EnclosedActiveBorder())
                      .BorderColor(active ? _theme.Border : Color.Transparent);
                if (active)
                    header.Background(_theme.Surface);
                break;
        }
    }

    private Element BuildContent()
    {
        var host = BoxBuild.New().FlexGrow(1);
        if (_style == TabStyle.Enclosed)
            host.Background(_theme.Surface).BorderColor(_theme.Border).BorderWidth(new Thickness(1));

        if (_tabs.Count > 0)
            host.ReplaceChildren(GetPanel(_selectedIndex));

        return host;
    }

    private Element GetPanel(int index)
    {
        var tab = _tabs[index];
        if (!tab.KeepAlive)
            return tab.Content();

        if (!_aliveContent.TryGetValue(index, out var panel))
        {
            panel = tab.Content();
            _aliveContent[index] = panel;
        }
        return panel;
    }

    private Element BuildBadge(string text, bool active)
    {
        var bg = active ? _theme.Primary : _theme.SurfaceVariant;
        var fg = active ? _theme.PrimaryText : _theme.Secondary;
        return BoxBuild.New()
            .Padding(new Thickness(1, 7, 1, 7)).CornerRadius(999f).Background(bg)
            .Children(TextBuild.New(text).FontSize(11f).FontWeight(FontWeight.SemiBold).TextColor(fg));
    }

    private Element BuildClose(Action onClose)
    {
        var close = BoxBuild.New()
            .Width(16).Height(16).CornerRadius(4f)
            .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
            .Cursor(CursorType.Pointer)
            .OnHover(c => c.Background(_theme.SurfaceVariant))
            .Children(TextBuild.New("×").FontSize(13f).TextColor(_theme.Secondary));
        close.OnOwn<ClickEvent>(e => { e.StopPropagation = true; onClose(); });
        return close;
    }

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
}

public static class Tabs
{
    public static TabsElement New([OriginFile] string? file = null, [OriginLine] int line = 0) => TabsElement.New(file, line);
}
