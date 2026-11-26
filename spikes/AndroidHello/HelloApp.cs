using System.Diagnostics;
using HultUI;
using HultUI.Core.Builders;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using Log = Android.Util.Log;
// Android's implicit usings bring Android.App and Android.Widget, which clash with these.
using Application = HultUI.Application;
using ScrollView = HultUI.Core.Builders.ScrollView;

namespace AndroidHello;

/// <summary>
/// The spike's app: a counter button, a text field, a paragraph that has to wrap (ICU's line
/// breaker), and a long scroll view. Everything it learns goes to logcat under the tag
/// <see cref="Tag"/>, and HultUI's start-up trace goes to files/startup.txt in the app's folder.
/// </summary>
internal static class HelloApp
{
    public const string Tag = "HultUISpike";

    /// <summary>HULTUI_RENDERER for this run (an intent extra), or null for the default.</summary>
    public static string? Renderer;

    /// <summary>Whether to rebind the GL context on returning to the foreground.</summary>
    public static bool Rebind = true;

    /// <summary>Milliseconds after returning to the foreground to draw again; 0 for never.</summary>
    public static int RedrawAfterMs;

    /// <summary>SDL_HINT_ANDROID_BLOCK_ON_PAUSE; false keeps the UI thread running in the background.</summary>
    public static bool BlockOnPause = true;

    /// <summary>The 1 s ticker. Off for the redraw checks: its frame each second would hide a lost one.</summary>
    public static bool Ticker = true;

    /// <summary>Which app to run: null for this one, "fluent" for the Fluent sample's UI.</summary>
    public static string? Sample;

    /// <summary>The video sample's file (<c>-e file</c>).</summary>
    public static string? VideoFile;

    /// <summary>
    /// The Keystore store behind CredentialStore.Default, on the device: a round trip, a delete,
    /// and an entry kept across launches (written on the first run, read back on the next). Each
    /// result goes to logcat as "credentials: …"; the app then ends.
    /// </summary>
    private static void TestCredentials()
    {
        var store = HultUI.CredentialStore.Default;
        Info($"credentials: store {store.GetType().Name}");
        bool wrote = store.Write("spike|server", "token", "s3cret-ÅÄÖ-✓");
        string? read = store.Read("spike|server", "token");
        Info($"credentials: write {wrote}, read back {(read == "s3cret-ÅÄÖ-✓" ? "same" : $"DIFFERENT ({read})")}");
        bool deleted = store.Delete("spike|server", "token");
        Info($"credentials: delete {deleted}, read after delete {(store.Read("spike|server", "token") is null ? "null" : "STILL THERE")}");
        string? kept = store.Read("spike|kept", "token");
        Info($"credentials: kept from an earlier launch: {kept ?? "(none yet)"}");
        store.Write("spike|kept", "token", "kept since " + DateTime.UtcNow.ToString("O"));
        string files = Android.App.Application.Context.FilesDir!.AbsolutePath;
        string stored = File.Exists(Path.Combine(files, "hultui-credentials"))
            ? File.ReadAllText(Path.Combine(files, "hultui-credentials")) : "";
        Info($"credentials: the file holds the secret in the clear: {stored.Contains("kept since")}");
    }

    /// <summary>The Fluent sample, unchanged, as samples/HultUI.Samples.Fluent/Program.cs runs it.</summary>
    /// <summary>
    /// Plan 20 phase 6's second check (scripts/talkback-check.ps1 -Sample a11y): a text area to read by
    /// character, word and line; a button whose long press opens a context menu and a dialog to dismiss;
    /// and a list of 3,000 items, which the tree serves on demand.
    /// </summary>
    private static void RunA11y(Stopwatch clock)
    {
        using var app = new Application("HultUI a11y", 400, 800);
        var window = app.PrimaryWindow;
        var area = TextArea.New("alpha bravo charlie\nsecond line here").Height(96f).FlexShrink(0);
        area.AccessibleName = "Notes";
        area.Select(0, 0);
        // A one-line field, which offers its text to a reader too (TALKBACK.md §10).
        var field = TextInput.New("delta echo").FlexShrink(0);
        field.AccessibleName = "Title";
        var menuButton = Box.New().Padding(new Thickness(12, 20, 12, 20)).Background(Color.FromHex("#4F46E5"))
            .CornerRadius(10f).TextColor(Color.White).FlexShrink(0).Children(Text.New("Options").FontSize(18f));
        menuButton.Role = HultUI.Core.Accessibility.AccessibilityRole.Button;
        HultUI.Core.Elements.ContextMenuItem[] menu =
        [
            new() { Label = "Rename", Action = () => Info("menu: rename") },
            new() { Label = "Delete", Action = () => Info("menu: delete") },
        ];
        menuButton.ContextMenu(() =>
        {
            Info("menu: opening");
            return menu;
        });
        menuButton.OnClick(e =>
        {
            Info("dialog: open");
            _ = HultUI.Core.Dialogs.Dialog.Confirm(window, "Delete the file?", "It cannot be undone.")
                .ContinueWith(t => Info($"dialog: closed {t.Result}"), TaskScheduler.FromCurrentSynchronizationContext());
        });
        var items = Enumerable.Range(0, 3000).Select(i => $"Item {i}").ToList();
        var list = HultUI.Core.Elements.VirtualListElement<string>.New().ItemHeight(48f).FlexGrow(1)
            .RenderWith(t => Flex.Row().Padding(new Thickness(12, 16, 12, 16)).Children(Text.New(t).FontSize(16f).TextColor(Color.White)))
            .Items(items);
        list.OnItemActivate = item => Info($"activated {item}");
        var root = Flex.Column(gap: 12).Padding(16).Children(
            Text.New("Accessibility check").FontSize(24f).FontWeight(FontWeight.Bold).FlexShrink(0),
            area, field, menuButton, list);
        // Where touches land: TalkBack passes a double tap held through as a press at its focus.
        root.OnPointerDown(e => Info($"press {e.PointerType} at {e.Position.X:0},{e.Position.Y:0}"));
        menuButton.OnPointerDown(_ => Info("press on Options"));
        app.SetRoot(root);
        using var report = new Timer(_ => Application.InvokeOnMainThread(() =>
            Info($"geometry area {Dims(area.WindowBorderBox)} button {Dims(menuButton.WindowBorderBox)} list {Dims(list.WindowBorderBox)}")),
            null, 2000, Timeout.Infinite);
        using var caret = new Timer(_ => Application.InvokeOnMainThread(() =>
            Info($"caret {area.CursorPosition} selection {area.SelectionStart}..{area.SelectionEnd} field caret {field.CursorPosition}")), null, 3000, 1000);
        Info($"a11y sample made at {clock.ElapsedMilliseconds} ms");
        app.Run();
    }

    /// <summary>
    /// Plan 20 phase 5: video through libmpv on GL ES (native/mpv/build-libmpv-android.sh),
    /// scripts/video-check.ps1. Plays <see cref="VideoFile"/> in HultUI's player and logs the first
    /// frame and the frames drawn. With no file it asks for mpv's lavfi test pattern, which the
    /// trimmed build lacks (no libavdevice), so nothing opens.
    /// </summary>
    private static void RunVideo(Stopwatch clock)
    {
        using var app = new Application("HultUI video", 400, 800);
        var status = Text.New("").FontSize(14f).FlexShrink(0);
        if (!HultUI.Media.Mpv.MpvBackend.IsAvailable)
        {
            string why;
            try { System.Runtime.InteropServices.NativeLibrary.Load("libmpv.so"); why = "loads by name"; }
            catch (Exception e) { why = e.Message; }
            Info($"video: libmpv not available ({why})");
            app.SetRoot(Flex.Column().Padding(16).Children(Text.New("libmpv not found").FontSize(20f)));
            app.Run();
            return;
        }
        var player = new HultUI.Media.MediaPlayer(new HultUI.Media.Mpv.MpvBackend());
        var video = new HultUI.Media.VideoPlayer(player);
        int frames = 0;
        bool subtitlesOn = false;
        player.FrameArrived += () =>
        {
            if (frames++ == 0) Info($"video: first frame at {clock.ElapsedMilliseconds} ms");
            // A file with subtitles shows its first track (the ASS check): the player starts with none.
            if (!subtitlesOn && player.SubtitleTracks.Count > 0)
            {
                subtitlesOn = true;
                player.SelectSubtitleTrack(player.SubtitleTracks[0].Id);
                Info($"video: subtitles on, track {player.SubtitleTracks[0].Id}");
            }
        };
        app.SetRoot(Flex.Column(gap: 8).Padding(8).Children(status, video.Mount().FlexGrow(1)));
        string source = VideoFile ?? "av://lavfi:testsrc2=size=1280x720:rate=30";
        Info($"video: opening {source}");
        player.Open(VideoFile is not null ? HultUI.Media.MediaSource.FromFile(VideoFile) : HultUI.Media.MediaSource.FromUri(source), autoPlay: true);
        int last = 0;
        using var report = new Timer(_ => Application.InvokeOnMainThread(() =>
        {
            Info($"video: {player.State} at {player.Position.TotalSeconds:0.0} s of {player.Duration?.TotalSeconds ?? 0:0.0}, " +
                 $"{frames - last} frames in 2 s");
            status.Content = $"{player.State} {player.Position.TotalSeconds:0.0} s";
            last = frames;
        }), null, 2000, 2000);
        Info($"video sample made at {clock.ElapsedMilliseconds} ms");
        app.Run();
    }

    private static void RunFluent(Stopwatch clock)
    {
        using var app = new Application("Fluent Sample", 1280, 860, HultUI.Core.Styling.WindowStyle.Borderless);
        var ui = new HultUI.Samples.Fluent.FluentApp(app, app.PrimaryWindow);
        app.SetRoot(ui.Mount());
        Info($"Fluent made at {clock.ElapsedMilliseconds} ms");
        app.Run();
    }

    public static void Run()
    {
        var clock = Stopwatch.StartNew();
        string files = Android.App.Application.Context.FilesDir!.AbsolutePath;
        Environment.SetEnvironmentVariable("HULTUI_STARTUP", Path.Combine(files, "startup.txt"));
        Environment.SetEnvironmentVariable("HULTUI_FRAMELOG", "1");
        Environment.SetEnvironmentVariable("HULTUI_FRAMELOG_SECS", "2");
        if (Renderer is not null) Environment.SetEnvironmentVariable("HULTUI_RENDERER", Renderer);
        Info($"renderer={Renderer ?? "(default)"} rebind={Rebind} redraw={RedrawAfterMs} block={BlockOnPause}");
        // Read by SDL each time it pumps events while paused; set before SDL_Init to be safe.
        if (!BlockOnPause) SDL.SDL3.SDL_SetHint(SDL.SDL3.SDL_HINT_ANDROID_BLOCK_ON_PAUSE, "0");
        ProbeIcu();
        Info($"Main on thread {Environment.CurrentManagedThreadId}, runtime {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");

        try
        {
            // SDL_SetMainReady is HultUIActivity's now, before RunApp.
            if (Sample == "fluent") RunFluent(clock);
            else if (Sample == "a11y") RunA11y(clock);
            else if (Sample == "video") RunVideo(clock);
            else if (Sample == "credentials") TestCredentials();
            else RunApp(clock);
        }
        catch (Exception e)
        {
            Log.Error(Tag, "HultUI failed: " + e);
            throw;
        }
        Info("Run returned");
    }

    private static void RunApp(Stopwatch clock)
    {
        int count = 0;
        var label = Text.New("Tapped 0 times").FontSize(18f);

        var button = Box.New().Padding(new Thickness(14, 24, 14, 24)).Background(Color.FromHex("#4F46E5"))
            .CornerRadius(10f).TextColor(Color.White).FontWeight(FontWeight.Bold)
            .Children(Text.New("Tap me").FontSize(18f));
        button.OnClick(_ =>
        {
            count++;
            label.Content = $"Tapped {count} time{(count == 1 ? "" : "s")}";
            Info($"click {count}");
            // Plan 20 phase 6: an announcement, which TalkBack speaks from a live region.
            button.Announce($"Counted {count}");
        });
        // Phase 3's check (scripts/touch-check.ps1): a long press, and where a press lands.
        button.OnLongPress(e => Info($"long press ({e.PointerType})"));
        button.OnPointerDown(e => Info($"press {e.PointerType} id {e.PointerId} primary {e.IsPrimary}"));
        button.OnPointerCancel(_ => Info("press cancelled"));

        // Phase 4's check (scripts/keyboard-check.ps1): words to select by touch, and a number field.
        var input = TextInput.New("alpha bravo charlie");
        input.Placeholder = "Type here";
        input.TextChanged = text => Info($"text: \"{text}\"");
        var number = TextInput.New().Kind(HultUI.Core.Platform.TextInputKind.Number);
        number.Placeholder = "A number";

        var rows = Enumerable.Range(1, 40).Select(i =>
            Box.New().Padding(new Thickness(12, 16, 12, 16))
                .Background(i % 2 == 0 ? Color.FromHex("#1F2937") : Color.FromHex("#111827"))
                .Children(Text.New($"Row {i}").FontSize(16f).TextColor(Color.White))).ToArray();

        var list = ScrollView.New().FlexGrow(1).Children(Flex.Column().FlexShrink(0).Children(rows));
        // A finger's scroll and fling, as the offset it reaches, once a frame at most.
        float loggedAt = -1;
        list.UserScrolled = () =>
        {
            float at = MathF.Round(list.ScrollOffsetY);
            if (at == loggedAt) return;
            loggedAt = at;
            Info($"scrolled {at:0}");
        };
        rows[0].OnClick(_ => Info("row 1 clicked"));

        var tickLabel = Text.New("tick 0").FontSize(13f).FlexShrink(0);
        var root = Flex.Column(gap: 12).Padding(16).Children(
            Text.New("HultUI on Android").FontSize(26f).FontWeight(FontWeight.Bold).FlexShrink(0),
            tickLabel,
            Text.New("This paragraph is long on purpose. If ICU's line breaker loaded, it wraps at "
                     + "word boundaries across several lines; if not, it runs off the right edge.")
                .FontSize(15f).FlexShrink(0),
            Flex.Row(gap: 16).AlignItems(AlignItems.Center).FlexShrink(0).Children(button, label),
            input.FlexShrink(0),
            number.FlexShrink(0),
            // An overlay for Back to close: tapped open, its list is an overlay of the window.
            Dropdown.New(["First choice", "Second choice", "Third choice"], 0).FlexShrink(0),
            list);

        // Logs Back without taking it, so it goes on to Escape's work and then the system's back.
        root.OnKeyDown(e =>
        {
            if (e.Key == HultUI.Core.Events.KeyCode.Back) Info("back reached the app's handler");
        });

        using var app = new Application("HultUI spike", 400, 800);
        app.SetRoot(root);
        var safeWindow = app.PrimaryWindow;
        Info($"safe area at start: {safeWindow.SafeArea}");
        safeWindow.SafeAreaChanged += () => Info($"safe area changed: {safeWindow.SafeArea}");
        safeWindow.KeyboardInsetChanged += () => Info($"keyboard inset {safeWindow.KeyboardInset:0}");
        Info($"Application made at {clock.ElapsedMilliseconds} ms");

        // The first presented frame's time is in files/startup.txt (HULTUI_STARTUP); this
        // reports what the window settled on a second in.
        var window = app.PrimaryWindow;
        using var report = new Timer(_ => Application.InvokeOnMainThread(() =>
        {
            Info($"after 2 s: layout {window.LayoutWidth}x{window.LayoutHeight} DIP, display scale {window.DisplayScale}");
            // Where the touch check aims, in layout units: the button and the list.
            Info($"geometry button {Dims(button.WindowBorderBox)} list {Dims(list.WindowBorderBox)}");
            Info($"fields text {Dims(input.WindowContentBox)} number {Dims(number.WindowContentBox)}");
            // The start-up trace, into logcat too: run-as cannot read it from a release build.
            string trace = Environment.GetEnvironmentVariable("HULTUI_STARTUP")!;
            if (File.Exists(trace))
                foreach (string line in File.ReadAllLines(trace)) Info(line);
        }), null, 2000, Timeout.Infinite);
        window.Resized += () => Info($"resized: {window.LayoutWidth}x{window.LayoutHeight} DIP, scale {window.DisplayScale}");
        SdlProbe.Install();
        // The background check: work posted to the UI thread every second, which also changes
        // the tree. With SDL's block on, the ticks stop in the background; with it off they
        // must go on, and the frame log must show no frames drawn there.
        // The field's selection, as it changes: what a long press and a handle chose.
        string selected = "", focusSeen = "other";
        using var selection = new Timer(_ => Application.InvokeOnMainThread(() =>
        {
            string focusNow = ReferenceEquals(window.FocusedElement, input) ? "text" : ReferenceEquals(window.FocusedElement, number) ? "number" : "other";
            if (focusNow != focusSeen) Info($"focus {focusSeen = focusNow}");
            if (input.SelectedText == selected) return;
            selected = input.SelectedText;
            Info($"selected \"{selected}\"");
        }), null, 250, 250);
        int ticks = 0;
        using var ticker = new Timer(_ => Application.InvokeOnMainThread(() =>
        {
            ticks++;
            tickLabel.Content = $"tick {ticks}";
            Info($"tick {ticks}");
        }), null, Ticker ? 1000 : Timeout.Infinite, Ticker ? 1000 : Timeout.Infinite);
        int resumes = 0;
        SdlProbe.OnForeground = () =>
        {
            if (Rebind) SdlProbe.RebindGL();
            if (RedrawAfterMs == 0 && !Ticker) return;   // the clean check: the app changes nothing on return
            label.Content = $"Resumed {++resumes}x, tapped {count}";
            // The experiment: the frame drawn right after DID_ENTER_FOREGROUND never shows
            // (the new surface is not ready for it); one drawn a little later does.
            if (RedrawAfterMs > 0)
                _ = Task.Delay(RedrawAfterMs).ContinueWith(_ => Application.InvokeOnMainThread(() =>
                    label.Content = $"Resumed {resumes}x (redrawn), tapped {count}"));
        };
        app.Run();
    }

    /// <summary>
    /// HultUI.Text's IcuLibrary tries "icu", then "libicuuc.so", then Linux folders. This repeats
    /// the first two (and the NDK's public "libicu.so") and reports which load and how the
    /// symbols are named, since the paragraph's wrapping alone does not say which one won.
    /// </summary>
    private static void ProbeIcu()
    {
        foreach (string name in new[] { "icu", "libicuuc.so", "libicu.so" })
        {
            if (!System.Runtime.InteropServices.NativeLibrary.TryLoad(name, out var handle))
            {
                Info($"icu probe: {name} does not load");
                continue;
            }
            string suffix = "(none found)";
            for (int v = 50; v < 100; v++)
                if (System.Runtime.InteropServices.NativeLibrary.TryGetExport(handle, $"u_errorName_{v}", out _)) { suffix = $"_{v}"; break; }
            bool plain = System.Runtime.InteropServices.NativeLibrary.TryGetExport(handle, "ubrk_open", out _);
            Info($"icu probe: {name} loads; versioned suffix {suffix}; unversioned ubrk_open {(plain ? "exported" : "absent")}");
        }
    }

    private static void Info(string message) => Log.Info(Tag, message);

    private static string Dims(Rect r) => FormattableString.Invariant($"{r.X:0.#},{r.Y:0.#},{r.Width:0.#},{r.Height:0.#}");
}
