using System.Collections.Concurrent;
using System.Diagnostics;
using FlaUI.UIA3;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Platform.SDL3;
using Xunit;
using Xunit.Abstractions;

namespace HultUI.UiTests;

using Text = HultUI.Core.Builders.Text;

/// <summary>
/// What a real UI Automation client's focus handler hears as the selection of a focused list moves.
/// Narrator and NVDA said nothing as the arrows walked a list or a menu (2026-11-15): they speak focus
/// moves, and the bridge raised only SelectionItem.IsSelected changes. The headless tests check the
/// bridge's own events and answers; this one checks that UI Automation delivers the focus event to a
/// client, in this process, through <c>IUIAutomation.AddFocusChangedEventHandler</c>, and that the
/// element it hands over is the item, by name, with HasKeyboardFocus. The window is never the
/// foreground (<see cref="ForegroundGuard"/>); its keys are not needed, the selection is moved from
/// code.
/// </summary>
[Collection("ui")] // one Application per process, and SDL is process-global
public sealed class ReaderFocusTests(ITestOutputHelper output)
{
    [UiFact]
    public void AFocusedListsSelectionMove_ReachesAClientsFocusHandler_AsItsItem()
    {
        using var guard = ForegroundGuard.Begin();
        using var app = new Application(new SdlWindowingPlatform(), "reader focus", 360, 260);
        var items = Enumerable.Range(0, 20).Select(i => $"file {i}").ToList();
        var list = VirtualListElement<string>.New().ItemHeight(20)
            .RenderWith(t => Flex.Row().Children(Text.New(t).FontSize(12f)))
            .Items(items)
            .SelectedIndex(0)
            .FlexGrow(1);
        list.IsFocusable = true;
        list.AccessibleName = "Files";
        app.SetRoot(Flex.Column().FlexGrow(1).Children(list));
        app.RunFrame(TimeSpan.FromMilliseconds(16));

        using var automation = new UIA3Automation();
        var heard = new ConcurrentQueue<(string Name, bool HasFocus)>();
        automation.RegisterFocusChangedEvent(element =>
        {
            try { heard.Enqueue((element.Name, element.Properties.HasKeyboardFocus.ValueOrDefault)); }
            catch (Exception ex) { heard.Enqueue(($"<{ex.GetType().Name}>", false)); }
        });
        try
        {
            // The client is listening now: the window builds its tree for it.
            Pump(app, TimeSpan.FromMilliseconds(300));
            list.Focus();
            Pump(app, TimeSpan.FromMilliseconds(500));
            list.SelectedIndex = 3;
            bool got = Pump(app, TimeSpan.FromSeconds(3), () => heard.Any(h => h.Name == "file 3"));
            // Only this window's: the handler hears the desktop's focus moves too (another app's).
            foreach (var (name, focus) in heard.Where(h => h.Name.StartsWith("file", StringComparison.OrdinalIgnoreCase)))
                output.WriteLine($"focus event: {name}, HasKeyboardFocus {focus}");
            if (!got && heard.IsEmpty)
                Assert.Fail("no focus event reached the client's handler from a window that is not the foreground: "
                    + "UI Automation may not deliver one without it");
            Assert.Contains(heard, h => h.Name == "file 0" && h.HasFocus);
            Assert.Contains(heard, h => h.Name == "file 3" && h.HasFocus);
            // One focus event a move, on the item: none on the list itself, none twice.
            Assert.Equal(["file 0", "file 3"], heard.Where(h => h.Name.StartsWith("file", StringComparison.OrdinalIgnoreCase)
                || h.Name == "Files").Select(h => h.Name));
        }
        finally { automation.UnregisterAllEvents(); }
    }

    /// <summary>
    /// Junction's History when a commit lands under the reader: a list over 200 items is served on demand on
    /// Windows (plan 17 round 16), so its items have no nodes, and a rename of the one the reader's focus is
    /// on was told nothing, where an item node told its Name change. A real client's property handler hears
    /// the item's new name once, from the item, which has the keyboard focus, and its focus handler hears
    /// no move.
    /// </summary>
    [UiFact]
    public void AServedListsFocusedItemRenamed_ReachesAClientsPropertyHandler_Once()
    {
        using var guard = ForegroundGuard.Begin();
        using var app = new Application(new SdlWindowingPlatform(), "reader focus", 360, 260);
        var items = Enumerable.Range(0, 300).Select(i => $"file {i}").ToList();
        var list = VirtualListElement<string>.New().ItemHeight(20)
            .RenderWith(t => Flex.Row().Children(Text.New(t).FontSize(12f)))
            .Items(items)
            .SelectedIndex(0)
            .FlexGrow(1);
        list.IsFocusable = true;
        list.AccessibleName = "Files";
        app.SetRoot(Flex.Column().FlexGrow(1).Children(list));
        app.RunFrame(TimeSpan.FromMilliseconds(16));

        using var automation = new UIA3Automation();
        var focus = new ConcurrentQueue<string>();
        var names = new ConcurrentQueue<string>();
        var senders = new ConcurrentQueue<(string Name, bool HasFocus)>();
        automation.RegisterFocusChangedEvent(element =>
        {
            try { focus.Enqueue(element.Name); }
            catch (Exception ex) { focus.Enqueue($"<{ex.GetType().Name}>"); }
        });
        var hwnd = ((HultUI.Core.Platform.IWin32WindowHandle)typeof(Window).GetProperty("PlatformWindow",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(app.PrimaryWindow)!).Hwnd;
        var window = automation.FromHandle(hwnd);
        // Let go with the others (UnregisterAllEvents, below).
        window.RegisterPropertyChangedEvent(FlaUI.Core.Definitions.TreeScope.Subtree,
            (element, _, value) =>
            {
                names.Enqueue(value?.ToString() ?? "<null>");
                try { senders.Enqueue((element.Name, element.Properties.HasKeyboardFocus.ValueOrDefault)); }
                catch (Exception ex) { senders.Enqueue(($"<{ex.GetType().Name}>", false)); }
            }, automation.PropertyLibrary.Element.Name);
        try
        {
            Pump(app, TimeSpan.FromMilliseconds(300));
            list.Focus();
            bool focused = Pump(app, TimeSpan.FromSeconds(3), () => focus.Contains("file 0"));
            if (!focused && focus.IsEmpty)
                Assert.Fail("no focus event reached the client's handler from a window that is not the foreground: "
                    + "UI Automation may not deliver one without it");
            Assert.True(focused, "the focus handler heard " + string.Join(" | ", focus));
            while (focus.TryDequeue(out _)) { }
            while (names.TryDequeue(out _)) { }
            while (senders.TryDequeue(out _)) { }

            list.Items = ["new commit", .. items];
            bool renamed = Pump(app, TimeSpan.FromSeconds(3), () => names.Contains("new commit"));
            Pump(app, TimeSpan.FromMilliseconds(300));
            output.WriteLine("names: " + string.Join(" | ", names));
            output.WriteLine("focus: " + string.Join(" | ", focus));
            Assert.True(renamed, "the property handler heard " + string.Join(" | ", names));
            Assert.Single(names, n => n == "new commit");
            Assert.DoesNotContain(focus, n => n.StartsWith("file", StringComparison.Ordinal) || n == "new commit" || n == "Files");
            // The element the event came from is the item, by its new name, with the keyboard focus.
            Assert.Contains(senders, s => s.Name == "new commit" && s.HasFocus);
        }
        finally { automation.UnregisterAllEvents(); }
    }

    /// <summary>The Fluent sample's navigation, as a client sees it: items that hold the focus, one
    /// selected, the selection moved by a click (which rebuilds the list), under a menu bar.</summary>
    private sealed class Nav : HultUI.Core.Components.Component
    {
        public readonly string[] Names = ["Home", "Personalization", "Gallery"];
        public int Current;
        public readonly MenuBar Bar = new(
        [
            new("&File", [new ContextMenuItem { Label = "Open" }]),
            new("&View", [new ContextMenuItem { Label = "Zoom" }]),
            new("&Help", [new ContextMenuItem { Label = "About" }]),
        ]);

        public override Element Build() => Flex.Column().FlexGrow(1).Children(
        [
            Bar.Mount(),
            .. Names.Select((name, i) =>
            {
                var item = Flex.Row().Height(30)
                    .AccessibleRole(HultUI.Core.Accessibility.AccessibilityRole.ListItem).AccessibleName(name)
                    .AccessibleSelected(i == Current)
                    .OnClick(_ => { Current = i; MarkDirty(); })
                    .Children(Text.New(name));
                item.IsFocusable = true;
                return (Element)item;
            }),
        ]);
    }

    /// <summary>
    /// Narrator and NVDA read a navigation item's click from its press, "not selected", and never its
    /// selection, on the release (2026-11-16); and after F10, Right, Right and Escape they read the menu
    /// bar where the focus had been. A real client hears the click once, at the release, on the item,
    /// selected; and Escape on the item again, never on the bar. Press and release go onto SDL's queue
    /// for this window (never the system's input), a frame apart as a hand makes them.
    /// </summary>
    [UiFact]
    public unsafe void AClickOnASelectableItem_ReachesAClient_AtTheRelease_Selected_AndEscapeFromTheBarGoesBackToIt()
    {
        using var guard = ForegroundGuard.Begin();
        using var app = new Application(new SdlWindowingPlatform(), "reader focus", 500, 300);
        var nav = new Nav();
        var root = nav.Mount();
        root.On<HultUI.Core.Events.KeyDownEvent>(e => { if (nav.Bar.HandleKey(e)) e.StopPropagation = true; });
        app.SetRoot(root);
        app.RunFrame(TimeSpan.FromMilliseconds(16));

        using var automation = new UIA3Automation();
        var heard = new ConcurrentQueue<string>();
        automation.RegisterFocusChangedEvent(element =>
        {
            try
            {
                var selected = element.Patterns.SelectionItem.PatternOrDefault?.IsSelected.ValueOrDefault;
                heard.Enqueue($"{element.Name}{(selected is { } s ? s ? ", selected" : ", not selected" : "")}"
                    + (element.Properties.HasKeyboardFocus.ValueOrDefault ? "" : " (no keyboard focus)"));
            }
            catch (Exception ex) { heard.Enqueue($"<{ex.GetType().Name}>"); }
        });
        string[] ours = ["Home", "Personalization", "Gallery", "File", "View", "Help", "Menu bar"];
        List<string> Heard()
        {
            var list = new List<string>();
            while (heard.TryDequeue(out var h))
                if (ours.Any(o => h.StartsWith(o, StringComparison.Ordinal))) list.Add(h);
            return list;
        }
        var window = WindowId();
        void Push(SDL.SDL_Event e)
        {
            Assert.True(SDL.SDL3.SDL_PushEvent(&e));
            Pump(app, TimeSpan.FromMilliseconds(120));
        }
        void Key(SDL.SDL_Scancode code)
        {
            foreach (bool down in new[] { true, false })
            {
                var e = new SDL.SDL_Event();
                e.key.type = down ? SDL.SDL_EventType.SDL_EVENT_KEY_DOWN : SDL.SDL_EventType.SDL_EVENT_KEY_UP;
                e.key.windowID = window;
                e.key.scancode = code;
                e.key.key = SDL.SDL3.SDL_GetKeyFromScancode(code, SDL.SDL_Keymod.SDL_KMOD_NONE, true);
                e.key.down = down;
                Push(e);
            }
        }
        try
        {
            Pump(app, TimeSpan.FromMilliseconds(300));
            var items = ((Element)root).Children[0].Children;
            items[1].Focus();   // Home
            Pump(app, TimeSpan.FromMilliseconds(500));
            Heard();

            // The press: the focus moves, a client hears nothing yet.
            var box = items[2].Layout.AbsoluteBorderBox;   // Personalization
            // SDL's coordinates to the window's layout units (SDL3Window.InputScale), undone.
            float scale = ((SDL3Window)typeof(Window).GetProperty("PlatformWindow",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(app.PrimaryWindow)!).InputScale;
            var press = new SDL.SDL_Event();
            press.button.type = SDL.SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN;
            press.button.windowID = window;
            press.button.button = 1;   // SDL_BUTTON_LEFT
            press.button.down = true;
            press.button.clicks = 1;
            press.button.x = (box.X + box.Width / 2) / scale;
            press.button.y = (box.Y + box.Height / 2) / scale;
            Push(press);
            Pump(app, TimeSpan.FromMilliseconds(300));
            Assert.Same(items[2], app.PrimaryWindow.FocusedElement);
            Assert.Empty(Heard());

            var release = press;
            release.button.type = SDL.SDL_EventType.SDL_EVENT_MOUSE_BUTTON_UP;
            release.button.down = false;
            Push(release);
            Pump(app, TimeSpan.FromSeconds(3), () => !heard.IsEmpty);
            Pump(app, TimeSpan.FromMilliseconds(300));
            Assert.Equal(1, nav.Current);
            var click = Heard();
            output.WriteLine("click: " + string.Join(" | ", click));
            Assert.Equal(["Personalization, selected"], click);

            Key(SDL.SDL_Scancode.SDL_SCANCODE_F10);
            Key(SDL.SDL_Scancode.SDL_SCANCODE_RIGHT);
            Key(SDL.SDL_Scancode.SDL_SCANCODE_RIGHT);
            Key(SDL.SDL_Scancode.SDL_SCANCODE_ESCAPE);
            Pump(app, TimeSpan.FromMilliseconds(500));
            var keys = Heard();
            output.WriteLine("keys: " + string.Join(" | ", keys));
            Assert.Equal(["File", "View", "Help", "Personalization, selected"], keys);
        }
        finally { automation.UnregisterAllEvents(); }
    }

    private static unsafe SDL.SDL_WindowID WindowId()
    {
        int count;
        var windows = SDL.SDL3.SDL_GetWindows(&count);
        try
        {
            Assert.Equal(1, count);
            return SDL.SDL3.SDL_GetWindowID(windows[0]);
        }
        finally { SDL.SDL3.SDL_free(windows); }
    }

    /// <summary>Runs the window's frames, and its messages with them, for <paramref name="time"/> or
    /// until <paramref name="until"/> holds.</summary>
    private static bool Pump(Application app, TimeSpan time, Func<bool>? until = null)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < time)
        {
            app.RunFrame(TimeSpan.FromMilliseconds(16));
            if (until?.Invoke() == true) return true;
            Thread.Sleep(10);
        }
        return until?.Invoke() ?? true;
    }
}
