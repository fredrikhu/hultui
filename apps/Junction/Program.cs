using HultUI;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using Junction;
using Junction.Theme;

/// <summary>
/// The entry point is explicit, and STA, rather than top-level statements.
///
/// <para>Windows hands a .NET main thread the MTA apartment by default, and OLE refuses to
/// initialise on one — <c>OleInitialize</c> answers RPC_E_CHANGED_MODE. Everything that runs
/// through OLE is then unavailable for the life of the process: dragging files out to another
/// application above all, but the shell's own dialogs and menus are happier here too. Every
/// Windows desktop framework marks its entry point this way; the attribute is inert on Linux
/// and macOS, where SDL owns the thread on its own terms.</para>
/// </summary>
internal static class JunctionProgram
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Settings load + git-root resolution touch no graphics state, so run them on a
        // worker while the main thread creates the window/GPU context (the bulk of
        // startup). SDL requires the window + event pump on the main thread, so the
        // graphics init itself can't move — but this GL-free work can, and it overlaps
        // almost entirely with the native init below.
        var settingsTask = System.Threading.Tasks.Task.Run(() =>
        {
            var settings = JunctionSettings.Load();
            IEnumerable<string> requestedPaths = args.Length > 0 ? args : settings.RepositoryPaths;
            var repoPaths = requestedPaths
                .Select(RepositoryPathResolver.FindGitRoot)
                .Where(path => path != null)
                .Select(path => path!)
                .Distinct()
                .ToList();

            if (repoPaths.Count == 0)
            {
                var fallback = RepositoryPathResolver.FindGitRoot(Environment.CurrentDirectory)
                               ?? RepositoryPathResolver.FindGitRoot(AppContext.BaseDirectory)
                               ?? ".";
                repoPaths.Add(fallback);
            }

            var activeIndex = args.Length > 0 ? 0 : settings.ActiveRepositoryIndex;
            settings.RepositoryPaths = repoPaths;
            settings.ActiveRepositoryIndex = activeIndex;
            if (args.Length > 0)
                settings.ActiveRepoPath = repoPaths[0];
            settings.Normalize();
            return (settings, repoPaths);
        });

        // The Application ctor creates the SDL window + GL context + Skia GRContext
        // synchronously, on the main thread, concurrently with the settings worker.
        using var app = new Application("Junction", 1440, 900, WindowStyle.Borderless);
        app.Theme = Nocturne.ToHultTheme();

        #if !DEBUG
        // No developer surfaces in a shipped build: the element inspector (Ctrl+Shift+I),
        // the performance HUD (F12), and trace recording (F11) all stay dark. Debug builds
        // keep them, and HULTUI_TRACE still drives the trace capture below either way.
        app.DevToolsHotkeysEnabled = false;
        #endif

        var window = app.PrimaryWindow;
        // Floor the window size so the chrome never collapses. Below the compact-chrome
        // breakpoint (1140 dip of content) JunctionApp switches the toolbar/breadcrumb to
        // icon-only, which stays legible down to this minimum.
        window.SetMinimumSize(760, 540);
        window.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "junction.png"));

        // Join the worker — usually already finished under the native init, so this
        // blocks for ~0. (Any exception on the worker surfaces here.)
        var (settings, repoPaths) = settingsTask.Result;

        Console.WriteLine($"Opening {repoPaths.Count} repository/repositories:");
        foreach (var path in repoPaths)
            Console.WriteLine($"  {path}");

        var junction = new JunctionApp(app, window, settings);
        window.Closed += junction.Dispose;
        app.SetRoot(junction.Mount());

        // Trace-capture hook: with HULTUI_TRACE_MS set, drive continuous frames for that
        // long, then close the window so the normal shutdown flushes HULTUI_TRACE. The
        // window is otherwise dirty-driven (an idle repo view renders a handful of cold
        // startup frames and then stops), which is useless for steady-state GL numbers;
        // a self-re-registering post-frame callback keeps NeedsRender true so Render /
        // Flush / Present are exercised every frame. This is how automation grabs a
        // steady-state GL trace without interaction (SDL3/Wayland doesn't turn
        // SIGINT/SIGTERM into a clean quit, so a signal can't do the flush).
        // Scripted-interaction hook, same job as HULTUI_TRACE_MS but for discrete actions:
        // JUNCTION_BENCH="Branches@4000,History@12000,row:5@20000,close@26000" runs each
        // step on the main thread at its absolute ms-since-launch. A view name switches
        // tabs, "row:N" selects the Nth branch row, "tree"/"flat" pick the ref presentation,
        // "expand:all"/"expand:none" open or close every branch folder, "close" ends the run (flushing
        // HULTUI_TRACE). Each step is stamped into the trace as a Meta "BenchStep" event,
        // so captures need no wall-clock correlation. Exists because there's no other way
        // to drive an unfocused window: SDL drops posted keyboard/click messages, and the
        // chrome tabs expose no UIA invoke pattern.
        if (Environment.GetEnvironmentVariable("JUNCTION_BENCH") is { Length: > 0 } benchScript)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            _ = System.Threading.Tasks.Task.Run(() =>
            {
                foreach (var part in benchScript.Split(','))
                {
                    var split = part.Split('@');
                    var step = split[0].Trim();
                    var wait = long.Parse(split[1]) - clock.ElapsedMilliseconds;
                    if (wait > 0) System.Threading.Thread.Sleep((int)wait);
                    Application.InvokeOnMainThread(() =>
                    {
                        window.Tracer.Annotate("BenchStep", step);
                        if (step == "close") window.Close();
                        else junction.BenchStep(step);
                    });
                }
            });
        }

        if (int.TryParse(Environment.GetEnvironmentVariable("HULTUI_TRACE_MS"), out var traceMs) && traceMs > 0)
        {
            var deadline = Environment.TickCount64 + traceMs;
            void KeepRendering()
            {
                if (Environment.TickCount64 >= deadline) { window.Close(); return; }
                window.RequestPostFrameCallback(KeepRendering);
            }
            window.RequestPostFrameCallback(KeepRendering);
        }

        app.Run();

    }
}
