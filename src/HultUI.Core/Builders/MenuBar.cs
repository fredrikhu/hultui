using HultUI.Core.Accessibility;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;

namespace HultUI.Core.Builders;

/// <summary>One menu of a <see cref="MenuBar"/>: its title, and the items it opens.</summary>
/// <param name="Title">The title; an <c>&amp;</c> marks the letter Alt opens it with
/// (<c>"&amp;File"</c>), and <c>&amp;&amp;</c> is a literal ampersand.</param>
/// <param name="Items">The items, asked for each time the menu opens, so a check mark or a
/// disabled entry follows the state it shows.</param>
public sealed record MenuBarItem(string Title, Func<IReadOnlyList<ContextMenuItem>> Items)
{
    public MenuBarItem(string title, IReadOnlyList<ContextMenuItem> items) : this(title, () => items) { }

    /// <summary>The title as shown, without the mnemonic marker.</summary>
    public string Label => Parse(Title).Label;

    /// <summary>The mnemonic letter, lower case, or '\0' for none.</summary>
    public char Mnemonic => Parse(Title) is { Index: >= 0 } p ? char.ToLowerInvariant(p.Label[p.Index]) : '\0';

    internal int MnemonicIndex => Parse(Title).Index;

    private static (string Label, int Index) Parse(string title)
    {
        var label = new System.Text.StringBuilder(title.Length);
        int index = -1;
        for (int i = 0; i < title.Length; i++)
        {
            if (title[i] == '&' && i + 1 < title.Length)
            {
                i++;
                if (title[i] != '&' && index < 0) index = label.Length;
            }
            label.Append(title[i]);
        }
        return (label.ToString(), index);
    }
}

/// <summary>
/// A row of menus across the top of a window — File, Edit, View — opening context menus as
/// dropdowns. Mount it where the bar goes, and route the window's keys through it:
/// </summary>
/// <remarks>
/// <code>
/// _menus = new MenuBar(
/// [
///     new("&amp;File", [new() { Label = "Open…", ShortcutHint = keys.Keys("open"), Action = Open }]),
///     new("&amp;View", () => [new() { Label = "Word wrap", IsChecked = _wrap, Action = ToggleWrap }]),
/// ]);
/// titleBar.AddChild(_menus.Mount());
/// // On the window's root element — where keys land when nothing has the focus:
/// appHost.On&lt;KeyDownEvent&gt;(e => { if (_menus.HandleKey(e)) e.StopPropagation = true; });
/// appHost.On&lt;KeyUpEvent&gt;(e => _menus.HandleKeyUp(e));
/// </code>
/// <para><b>Pointer:</b> a press on a title opens its menu and another closes it; while one is
/// open, moving to another title opens that one instead.</para>
/// <para><b>Keyboard</b>, the Windows way: F10, or Alt pressed and let go on its own, puts the
/// bar in charge of the keys — ← → move between titles, ↓ ↑ Enter or Space open, a title's
/// letter opens it, Esc, F10 or Alt leaves. Alt and a title's letter opens it from anywhere.
/// In an open menu ↑ ↓ Enter work as in any context menu, ← and → move to the neighbouring
/// menu where there is no submenu to close or open, and Esc goes back to the titles. Mnemonic
/// letters are underlined while Alt is held or the bar has the keys. The keyboard comes back to
/// whatever had it when the bar lets go. Right Alt is left alone: on many layouts it is AltGr.</para>
/// <para><b>Accessibility:</b> the bar is a menu bar, each title a menu item that reports
/// whether its menu is open.</para>
/// <para>Its look is the theme's <see cref="MenuBarTheme"/>; the menus are context menus.</para>
/// </remarks>
public sealed class MenuBar : Component
{
    private IReadOnlyList<MenuBarItem> _menus;
    private int _open = -1;        // the title whose menu is open
    private int _highlight = -1;   // the title the keyboard is on
    private bool _keyboard;        // the bar has the keys (F10, Alt)
    private bool _altHeld;         // Alt is down: show the mnemonics
    private bool _altAlone;        // ...and nothing else has been pressed since
    private bool _pointerOpened;   // a press opened the menu, so its click must not toggle it shut
    private int _generation;       // which open the next close notice belongs to
    private Element? _restoreFocus;

    /// <summary>The bar on screen. Not the one Build last returned: a rebuild patches the live
    /// bar from a fresh one and throws the fresh one away, so a reference kept from Build names
    /// an element that was never mounted — no overlay host, no layout, no focus.</summary>
    private MenuBarElement? Bar => Host is { Children.Count: > 0 } host ? host.Children[0] as MenuBarElement : null;

    private Element? TitleAt(int index) =>
        Bar is { } bar && index >= 0 && index < bar.Children.Count ? bar.Children[index] : null;

    public MenuBar(IReadOnlyList<MenuBarItem> menus)
    {
        _menus = menus ?? throw new ArgumentNullException(nameof(menus));
    }

    /// <summary>The menus, in order. Setting them rebuilds the bar.</summary>
    public IReadOnlyList<MenuBarItem> Menus
    {
        get => _menus;
        set { _menus = value ?? throw new ArgumentNullException(nameof(value)); Close(); MarkDirty(); }
    }

    /// <summary>The index of the menu that is open, or -1.</summary>
    public int OpenIndex => _open;

    /// <summary>Whether the bar has the keyboard (F10, Alt) or a menu is open.</summary>
    public bool IsActive => _keyboard || _open >= 0;

    /// <summary>The title the keyboard is on while the bar has the keys, or -1.</summary>
    public int HighlightIndex => _keyboard ? _highlight : -1;

    // ──── Keys routed from the window ────

    /// <summary>
    /// The window's keys, before the app's own: F10, Alt and a title's letter, and Alt itself.
    /// True when the bar took the key.
    /// </summary>
    public bool HandleKey(KeyDownEvent e)
    {
        bool leftAlt = e.Key == KeyCode.AltLeft
            || (e.Key == KeyCode.AltRight && e.ModifierKeys == ModifierKeys.None);   // unknown side
        if (leftAlt && (e.ModifierKeys & ModifierKeys.AltGr) == 0)
        {
            if (!e.IsRepeat) { _altAlone = true; SetAltHeld(true); }
            return false;
        }
        _altAlone = false;

        if (e.Key == KeyCode.F10 && (e.Modifiers & (KeyModifiers.Ctrl | KeyModifiers.Alt | KeyModifiers.Shift | KeyModifiers.Super)) == 0)
        {
            if (IsActive) Deactivate();
            else Activate(0);
            return true;
        }

        for (int i = 0; i < _menus.Count; i++)
        {
            char m = _menus[i].Mnemonic;
            if (m != '\0' && KeyChord.Of(m, KeyModifiers.Alt).Matches(e))
            {
                Activate(i);
                Open(i, fromKeyboard: true);
                return true;
            }
        }
        return false;
    }

    /// <summary>The window's key releases: Alt let go on its own toggles the bar. True when the
    /// bar took it.</summary>
    public bool HandleKeyUp(KeyUpEvent e)
    {
        if (e.Key is not (KeyCode.AltLeft or KeyCode.AltRight)) return false;
        SetAltHeld(false);
        if (!_altAlone) return false;
        _altAlone = false;
        if (IsActive) Deactivate();
        else Activate(0);
        return true;
    }

    // ──── Opening and closing ────

    /// <summary>Opens the menu at <paramref name="index"/> under its title.</summary>
    public void Open(int index, bool fromKeyboard = false)
    {
        if (index < 0 || index >= _menus.Count || TitleAt(index) is not { } titleElement) return;
        var host = Bar?.OverlayHost;
        if (host is null) return;

        int generation = ++_generation;
        _open = index;
        _highlight = index;
        var box = titleElement.Layout.AbsoluteBorderBox;
        MarkDirty();
        host.OpenMenu(_menus[index].Items(), new Point(box.X, box.Y + box.Height), new MenuRequest
        {
            HighlightFirst = fromKeyboard,
            Navigate = step => Open(Wrap(index + step), fromKeyboard: true),
            Closed = reason => OnClosed(generation, reason),
        });
    }

    /// <summary>Closes the open menu and lets go of the keyboard.</summary>
    public void Close()
    {
        if (_open >= 0)
        {
            _generation++;   // the close notice that follows belongs to no open
            _open = -1;
            Bar?.OverlayHost?.CloseMenu();
        }
        Deactivate();
    }

    private void OnClosed(int generation, MenuCloseReason reason)
    {
        if (generation != _generation) return;   // replaced by another menu, or closed by Close
        _open = -1;
        // Escape steps back to the titles; anything else is the end of using the bar.
        if (reason == MenuCloseReason.Escape) Activate(_highlight);
        else Deactivate();
        MarkDirty();
    }

    private void Activate(int highlight)
    {
        if (!_keyboard)
        {
            var focused = Bar?.FocusManager?.FocusedElement;
            _restoreFocus = focused is not null && !Contains(Bar, focused) ? focused : _restoreFocus;
        }
        _keyboard = true;
        _highlight = Math.Clamp(highlight, 0, Math.Max(0, _menus.Count - 1));
        Bar?.Focus();
        MarkDirty();
    }

    private void Deactivate()
    {
        bool hadKeys = _keyboard;
        _keyboard = false;
        _highlight = -1;
        var restore = _restoreFocus;
        _restoreFocus = null;
        if (hadKeys && Bar is { IsFocused: true } && restore is not null && restore.IsHosted) restore.Focus();
        MarkDirty();
    }

    private void SetAltHeld(bool held)
    {
        if (_altHeld == held) return;
        _altHeld = held;
        MarkDirty();
    }

    private int Wrap(int index) => _menus.Count == 0 ? -1 : ((index % _menus.Count) + _menus.Count) % _menus.Count;

    private static bool Contains(Element? root, Element element)
    {
        for (var e = element; e is not null; e = e.Parent)
            if (e == root) return true;
        return false;
    }

    // ──── The bar's own keys, while it has the keyboard ────

    private void OnBarKey(KeyDownEvent e)
    {
        if (!_keyboard || _open >= 0) return;
        bool bare = (e.Modifiers & (KeyModifiers.Ctrl | KeyModifiers.Alt | KeyModifiers.Super)) == 0;
        switch (e.Key)
        {
            case KeyCode.Left when bare: _highlight = Wrap(_highlight - 1); MarkDirty(); break;
            case KeyCode.Right when bare: _highlight = Wrap(_highlight + 1); MarkDirty(); break;
            case KeyCode.Down or KeyCode.Up or KeyCode.Enter or KeyCode.NumpadEnter or KeyCode.Space when bare:
                Open(_highlight, fromKeyboard: true);
                break;
            case KeyCode.Escape:
                Deactivate();
                break;
            default:
                for (int i = 0; i < _menus.Count; i++)
                {
                    char m = _menus[i].Mnemonic;
                    if (m != '\0' && bare && KeyChord.Of(m).Matches(e)) { Open(i, fromKeyboard: true); e.StopPropagation = true; return; }
                }
                return;   // not the bar's: let it reach the window (F10 and Alt come back through HandleKey)
        }
        e.StopPropagation = true;
    }

    // ──── Build ────

    public override Element Build()
    {
        var bar = new MenuBarElement();
        bar.Flex.Direction = FlexDirection.Row;
        bar.Flex.AlignItems = AlignItems.Center;
        bar.IsFocusable = true;
        bar.TabIndex = -1;   // reached with F10 or Alt, not Tab
        bar.Role = AccessibilityRole.MenuBar;
        bar.AccessibleName = "Menu bar";
        bar.On<KeyDownEvent>(OnBarKey);
        // The keyboard went elsewhere — a click into the document — so the bar is done with it.
        bar.On<BlurEvent>(_ => { if (_keyboard && _open < 0) { _keyboard = false; _highlight = -1; _restoreFocus = null; MarkDirty(); } });

        for (int i = 0; i < _menus.Count; i++)
        {
            int index = i;
            var menu = _menus[i];
            var title = new MenuTitleElement
            {
                IsOpen = i == _open,
                IsHighlighted = _keyboard && i == _highlight && _open < 0,
                Role = AccessibilityRole.MenuItem,
                AccessibleName = menu.Label,
                AccessibleExpanded = i == _open,
                AutomationId = $"menubar-{i}",
            };
            title.Key = $"menu|{i}|{menu.Title}";
            title.Cursor(CursorType.Default);

            // Not hit-testable, so a press lands on the title: in a title bar marked for window
            // dragging, a hit with no handlers of its own is taken as a drag of the window.
            var label = Text.New(menu.Label);
            label.IsHitTestVisible = false;
            if ((_altHeld || _keyboard) && menu.MnemonicIndex >= 0)
                label.Spans([new TextSpan(menu.MnemonicIndex, 1, Color.Transparent, Decoration: TextDecoration.Underline)]);
            title.AddChild(label);

            title.On<PointerDownEvent>(e =>
            {
                if (e.Button != PointerEvent.LeftButton) return;
                _pointerOpened = true;
                if (_open == index) Close();
                else
                {
                    Open(index);
                }
            });
            // A click the press already answered; otherwise an invoke from assistive technology.
            title.On<ClickEvent>(_ =>
            {
                if (_pointerOpened) { _pointerOpened = false; return; }
                if (_open == index) Close(); else Open(index);
            });
            title.On<PointerEnterEvent>(_ =>
            {
                if (_open >= 0 && _open != index) Open(index, fromKeyboard: false);
            });
            bar.AddChild(title);
        }
        return bar;
    }
}

/// <summary>The row <see cref="MenuBar"/> builds; it takes <see cref="MenuBarTheme"/> each style pass.</summary>
public sealed class MenuBarElement : BoxElement
{
    // Its own geometry and its titles' are the theme's, compared on a themed twin, which themes
    // the twin's titles too (MenuTitleElement compares against those).
    private protected override bool PatchMarksOwnLayout => true;
    private protected override bool PatchThemesTwin => true;

    protected internal override void ApplyTheme(Theme theme)
    {
        var t = theme.Get<MenuBarTheme>();
        BackgroundColor = t.Background ?? Color.Transparent;
        BorderColor = t.Divider ?? Color.Transparent;
        // A theme with other sizes lays the bar out again, and a title it resizes.
        var border = t.Divider is null ? new Thickness(0) : new Thickness(0, 0, 1, 0);
        var height = Dimension.Px(t.Height);
        if (BoxRead.BorderWidth != border || BoxRead.Padding != t.Padding || FlexRead.Gap != t.Gap || BoxRead.Height != height)
        {
            Box.BorderWidth = border;
            Box.Padding = t.Padding;
            Flex.Gap = t.Gap;
            Box.Height = height;
            MarkLayoutDirty();
        }

        var hover = t.HoverBackground ?? theme.SurfaceVariant;
        // By index: foreach over the Children interface boxes an enumerator, now on twins too.
        var children = Children;
        for (int i = 0; i < children.Count; i++)
        {
            if (children[i] is not MenuTitleElement title) continue;
            title.HoverColor = hover;
            title.BackgroundColor = title.IsOpen ? t.OpenBackground ?? hover
                : title.IsHighlighted ? hover
                : Color.Transparent;
            title.CornerRadius = new CornerRadius(t.TitleCornerRadius);
            if (title.BoxRead.Padding != t.TitlePadding || title.FlexRead.AlignItems != AlignItems.Center
                || title.BoxRead.Height != Dimension.Percent(100))
            {
                title.Box.Padding = t.TitlePadding;
                title.Flex.AlignItems = AlignItems.Center;
                title.Box.Height = Dimension.Percent(100);
                title.MarkLayoutDirty();
            }
            var texts = title.Children;
            for (int j = 0; j < texts.Count; j++)
            {
                var text = texts[j];
                text.Inherited.FontSize = t.FontSize;
                var color = title.IsOpen ? t.OpenText ?? t.Text : t.Text;
                if (color is { } c) text.Inherited.TextColor = c;
            }
        }
    }
}

/// <summary>One title in a <see cref="MenuBarElement"/>.</summary>
public sealed class MenuTitleElement : BoxElement
{
    internal bool IsOpen { get; set; }
    internal bool IsHighlighted { get; set; }
    internal Color HoverColor { get; set; }

    public MenuTitleElement()
    {
        Flex.Direction = FlexDirection.Row;
        this.OnHover(t => { if (!t.IsOpen) t.BackgroundColor = t.HoverColor; });
    }

    // Its geometry is written by its bar's theme, which a themed twin bar wrote into this twin
    // too; what it copies below is state the theme reads for colours.
    private protected override bool PatchMarksOwnLayout => true;

    internal override void PatchFrom(Element source)
    {
        base.PatchFrom(source);
        // State the theme reads: a patched title must not keep the old open fill.
        if (source is MenuTitleElement title)
        {
            IsOpen = title.IsOpen;
            IsHighlighted = title.IsHighlighted;
        }
    }
}
