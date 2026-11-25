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
        });
        // Phase 3's check (scripts/touch-check.ps1): a long press, and where a press lands.
        button.OnLongPress(e => Info($"long press ({e.PointerType})"));
        button.OnPointerDown(e => Info($"press {e.PointerType} id {e.PointerId} primary {e.IsPrimary}"));
        button.OnPointerCancel(_ => Info("press cancelled"));

        var input = TextInput.New();
        input.Placeholder = "Type here";
        input.TextChanged = text => Info($"text: \"{text}\"");

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
        Info($"Application made at {clock.ElapsedMilliseconds} ms");

        // The first presented frame's time is in files/startup.txt (HULTUI_STARTUP); this
        // reports what the window settled on a second in.
        var window = app.PrimaryWindow;
        using var report = new Timer(_ => Application.InvokeOnMainThread(() =>
        {
            Info($"after 2 s: layout {window.LayoutWidth}x{window.LayoutHeight} DIP, display scale {window.DisplayScale}");
            // Where the touch check aims, in layout units: the button and the list.
            Info($"geometry button {Dims(button.WindowBorderBox)} list {Dims(list.WindowBorderBox)}");
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
