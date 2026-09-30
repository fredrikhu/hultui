namespace HultUI.WebView;

/// <summary>What a context menu entry is.</summary>
public enum WebContextMenuItemKind
{
    /// <summary>Does something when chosen.</summary>
    Command,
    /// <summary>A setting that is on or off; <see cref="WebContextMenuItem.IsChecked"/> says which.</summary>
    CheckBox,
    /// <summary>One of a set of choices; <see cref="WebContextMenuItem.IsChecked"/> marks the one in force.</summary>
    Radio,
    /// <summary>A line between groups.</summary>
    Separator,
    /// <summary>Opens <see cref="WebContextMenuItem.Children"/>.</summary>
    Submenu,
}

/// <summary>
/// One entry in a page's context menu: either the engine's (copy, paste, a spelling
/// suggestion), which the engine carries out when chosen, or the application's, which runs
/// its <see cref="Action"/>.
/// </summary>
public sealed class WebContextMenuItem
{
    /// <summary>An engine's entry. <paramref name="commandId"/> is the engine's own and goes
    /// back to it when the entry is chosen.</summary>
    public WebContextMenuItem(string name, string label, WebContextMenuItemKind kind, int commandId)
    {
        Name = name;
        Label = label;
        Kind = kind;
        CommandId = commandId;
    }

    /// <summary>An application's entry, which runs <paramref name="action"/> when chosen.</summary>
    public WebContextMenuItem(string label, Action action)
    {
        Name = "custom";
        Label = label;
        Kind = WebContextMenuItemKind.Command;
        Action = action ?? throw new ArgumentNullException(nameof(action));
    }

    /// <summary>A line between groups.</summary>
    public static WebContextMenuItem Separator() => new("separator", "", WebContextMenuItemKind.Separator, 0);

    /// <summary>
    /// The entry's name, the same in every language: the engine's for its entries —
    /// <c>"copy"</c>, <c>"paste"</c>, <c>"copyLinkLocation"</c>, <c>"spellCheck"</c> — and
    /// <c>"custom"</c> for the application's. What to test when choosing entries.
    /// </summary>
    public string Name { get; }

    /// <summary>What the menu shows, in the user's language.</summary>
    public string Label { get; init; }

    /// <summary>The key that does the same, as text to show beside the label: "Ctrl+C".</summary>
    public string? ShortcutHint { get; init; }

    public WebContextMenuItemKind Kind { get; }
    public bool IsEnabled { get; init; } = true;
    public bool IsChecked { get; init; }
    public IReadOnlyList<WebContextMenuItem> Children { get; init; } = [];

    /// <summary>The application's handler; null for the engine's entries.</summary>
    public Action? Action { get; }

    /// <summary>The engine's id for the entry; zero for the application's.</summary>
    public int CommandId { get; }
}

/// <summary>What the context menu was asked for over.</summary>
[Flags]
public enum WebContextMenuTargetKind
{
    /// <summary>The page itself, or anything not listed here.</summary>
    Page = 0,
    Image = 1,
    SelectedText = 2,
    Audio = 4,
    Video = 8,
}

/// <summary>Where a context menu was asked for, and what is there.</summary>
public sealed class WebContextMenuTarget
{
    public WebContextMenuTargetKind Kind { get; init; }

    /// <summary>A text field or other editable content, so cut, paste and spelling apply.</summary>
    public bool IsEditable { get; init; }

    /// <summary>The page's address.</summary>
    public Uri? PageUri { get; init; }

    /// <summary>The link under the pointer, if any.</summary>
    public Uri? LinkUri { get; init; }

    /// <summary>The link's text, if any.</summary>
    public string? LinkText { get; init; }

    /// <summary>The image, audio or video's address, if any.</summary>
    public Uri? SourceUri { get; init; }

    /// <summary>The selected text, if any.</summary>
    public string? SelectionText { get; init; }
}

/// <summary>
/// A page asked for a context menu — a right-click, or the menu key. The engine creates it
/// with a continuation. The controller shows it to the application's
/// <see cref="WebViewController.ContextMenuRequested"/> handlers, which may change
/// <see cref="Items"/> or take the menu over; then the element opens a HultUI context menu of
/// <see cref="Items"/> where the page asked. The menu is answered exactly once — with the
/// entry chosen, or with none — so an engine may wait for the answer across threads.
/// </summary>
public sealed class WebContextMenuRequest
{
    /// <summary>
    /// The entries shown unless the application changes <see cref="Items"/>: editing,
    /// copying, and spelling. Left out are the ones that belong to a browser rather than to a
    /// page inside an application — back, reload, save as, print, share, open in a new
    /// window — which the application offers in its own interface if it wants them.
    /// </summary>
    public static readonly IReadOnlySet<string> StandardNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "undo", "redo", "cut", "copy", "paste", "pasteAndMatchStyle", "selectAll",
        "copyLinkLocation", "copyLinkText", "copyImage", "copyImageLocation",
        "spellCheck", "emoji",
    };

    private Action<WebContextMenuItem?>? _complete;

    /// <param name="x">Where the menu was asked for: device pixels from the page's top-left.</param>
    /// <param name="y">Likewise.</param>
    /// <param name="target">What is under that point.</param>
    /// <param name="engineItems">Every entry the engine offers, in its order.</param>
    /// <param name="complete">Called once with the engine entry chosen, or null for none.</param>
    /// <param name="devTools">Whether the page's developer tools are allowed, which keeps
    /// the engine's "inspect" entry in the standard menu.</param>
    public WebContextMenuRequest(int x, int y, WebContextMenuTarget target, IReadOnlyList<WebContextMenuItem> engineItems,
        Action<WebContextMenuItem?> complete, bool devTools = false)
    {
        X = x;
        Y = y;
        Target = target;
        EngineItems = engineItems;
        _complete = complete ?? throw new ArgumentNullException(nameof(complete));
        Items = Standard(engineItems, devTools);
    }

    public int X { get; }
    public int Y { get; }
    public WebContextMenuTarget Target { get; }

    /// <summary>Everything the engine offered, for an application that wants an entry the
    /// standard menu leaves out.</summary>
    public IReadOnlyList<WebContextMenuItem> EngineItems { get; }

    /// <summary>What will be shown. Remove, reorder, or add the application's own entries.</summary>
    public List<WebContextMenuItem> Items { get; }

    /// <summary>
    /// Set by a handler that shows its own menu, or none: the element then opens nothing, and
    /// the handler must call <see cref="Select"/> or <see cref="Dismiss"/> itself.
    /// </summary>
    public bool Handled { get; set; }

    /// <summary>The menu has been answered.</summary>
    public bool IsCompleted => Volatile.Read(ref _complete) is null;

    /// <summary>
    /// Carries out <paramref name="item"/>: the application's action, or the engine's command.
    /// Later calls, and a <see cref="Dismiss"/> after, do nothing.
    /// </summary>
    public void Select(WebContextMenuItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var complete = Interlocked.Exchange(ref _complete, null);
        if (complete is null) return;
        if (item.Action is { } action)
        {
            complete(null);
            action();
        }
        else complete(item);
    }

    /// <summary>Closes the menu with nothing chosen. Later calls do nothing.</summary>
    public void Dismiss() => Interlocked.Exchange(ref _complete, null)?.Invoke(null);

    private static List<WebContextMenuItem> Standard(IReadOnlyList<WebContextMenuItem> items, bool devTools)
    {
        var kept = new List<WebContextMenuItem>();
        foreach (var item in items)
        {
            if (item.Kind == WebContextMenuItemKind.Separator)
            {
                // No separator first, none doubled; a trailing one is trimmed below.
                if (kept.Count > 0 && kept[^1].Kind != WebContextMenuItemKind.Separator) kept.Add(item);
                continue;
            }
            if (item.Kind == WebContextMenuItemKind.Submenu)
            {
                var children = Standard(item.Children, devTools);
                if (children.Count > 0 && StandardNames.Contains(item.Name))
                    kept.Add(new WebContextMenuItem(item.Name, item.Label, item.Kind, item.CommandId)
                    {
                        ShortcutHint = item.ShortcutHint, IsEnabled = item.IsEnabled, Children = children,
                    });
                continue;
            }
            if (StandardNames.Contains(item.Name) || (devTools && item.Name == "inspectElement"))
                kept.Add(item);
        }
        while (kept.Count > 0 && kept[^1].Kind == WebContextMenuItemKind.Separator) kept.RemoveAt(kept.Count - 1);
        return kept;
    }
}
