using System.Diagnostics;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Rendering;
using HultUI.Terminal;

namespace HultUI.Samples.Terminal;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        bool bench = args.Contains("--bench");
        using var app = bench
            ? new Application("HultUI - Terminal bench", 1500, 960)
            : new Application("HultUI - Terminal", 980, 640);
        app.Theme = HultUI.Core.Styling.Theme.Dark;
        // The bench measures drawing, so it runs the demo shell, whose output is the same every time.
        using var sample = new TerminalSampleApp(bench || args.Contains("--demo") ? SampleProgram.Demo : SampleProgram.Shell);
        var root = sample.Mount();
        if (args.Contains("--check"))
        {
            app.SetRoot(root);
            return Check(app, sample);
        }
        if (bench)
        {
            var probe = new FrameProbe(app);
            root = Flex.Column().FlexGrow(1).Children(root.FlexGrow(1), probe);
            Application.InvokeOnMainThread(() => _ = Bench(app, sample, probe));
        }
        app.SetRoot(root);
        Application.InvokeOnMainThread(() => sample.Terminal.Focus());
        app.Run();
        return 0;
    }

    /// <summary>
    /// <c>--bench</c>: the two workloads docs/design/TERMINAL.md asks step 2 to measure on a real
    /// window, in the app's own frame loop. A full-screen redraw where every cell changes every
    /// frame, in a new colour every eight columns (about 1 500 styled runs a frame at 200 × 60),
    /// and a scrolling flood of numbered lines. Prints the frames drawn a second, the frame's
    /// Render (recording the draw calls) and Flush (Skia rasterising them) times, the program's
    /// output the terminal took a second, and this process's CPU a second (the demo shell runs
    /// in it too, and writing the output is part of that).
    /// </summary>
    private static async Task Bench(Application app, TerminalSampleApp sample, FrameProbe probe)
    {
        var c = sample.Session.Value;
        while (c.Title != "Demo shell") await Task.Delay(50);
        await Task.Delay(500);

        c.Send("flood 2\r"); // warm the glyph and run caches
        await Task.Delay(3000);

        c.Send("flood 7\r");
        await Task.Delay(500);
        await Measure(app, sample, probe, $"full-screen redraw, {c.Columns}×{c.Rows}", 5);
        await Task.Delay(2000);

        // Long enough to outlast the window: the shell's pipe is bounded, so it writes only as
        // fast as the terminal reads, and the bytes it wrote are the terminal's throughput.
        c.Send("seq 10000000\r");
        await Task.Delay(500);
        await Measure(app, sample, probe, $"scrolling flood, {c.Columns}×{c.Rows}", 4);

        app.PrimaryWindow.Close();
    }

    private static async Task Measure(Application app, TerminalSampleApp sample, FrameProbe probe, string name, double seconds)
    {
        probe.Samples.Clear();
        var process = Process.GetCurrentProcess();
        var cpu0 = process.TotalProcessorTime;
        long bytes0 = sample.Demo!.BytesWritten;
        var clock = Stopwatch.StartNew();
        probe.Recording = true;
        await Task.Delay(TimeSpan.FromSeconds(seconds));
        probe.Recording = false;
        process.Refresh();
        double elapsed = clock.Elapsed.TotalSeconds;
        double cpu = (process.TotalProcessorTime - cpu0).TotalMilliseconds / elapsed;
        double mbps = (sample.Demo!.BytesWritten - bytes0) / elapsed / (1024 * 1024);

        var render = probe.Samples.Select(s => (double)s.Render).Order().ToArray();
        var flush = probe.Samples.Select(s => (double)s.Flush).Order().ToArray();
        Console.WriteLine($"{name}: {render.Length / elapsed:F0} frames/s; " +
            $"Render median {Pct(render, 0.5):F2} ms, p95 {Pct(render, 0.95):F2} ms; " +
            $"Flush median {Pct(flush, 0.5):F2} ms, p95 {Pct(flush, 0.95):F2} ms; " +
            $"{mbps:F1} MB/s of output; CPU {cpu:F0} ms/s at scale {app.PrimaryWindow.DisplayScale:0.##}");
    }

    /// <summary>
    /// <c>--check</c>: shows the window, starts the user's shell under a pseudoterminal, has it
    /// work something out and print it, then exit with 7, and exits 0 when all of that showed
    /// up on the screen and as the exit code — or 1 with what did not. For proving a build
    /// (NativeAOT included) works end to end without anyone looking at it.
    /// </summary>
    private static int Check(Application app, TerminalSampleApp sample)
    {
        var clock = Stopwatch.StartNew();
        var c = sample.Session.Value;
        bool Pump(Func<bool> done)
        {
            while (!done())
            {
                if (clock.Elapsed > TimeSpan.FromSeconds(30)) return false;
                app.RunFrame(TimeSpan.FromMilliseconds(16), forceRender: true);
                Thread.Sleep(1);
            }
            return true;
        }
        int Fail(string what)
        {
            Console.Error.WriteLine($"check failed after {clock.ElapsedMilliseconds} ms: {what}\n--- screen ---\n{c.ReadScreen()}");
            return 1;
        }

        if (sample.Shell is not { } shell) return Fail(sample.Problem.Value ?? "no shell started");
        var name = Path.GetFileNameWithoutExtension(shell.Program).ToLowerInvariant();
        var (command, answer) = name switch
        {
            "pwsh" or "powershell" => ("'hultui-' + (6*7)", "hultui-42"),
            "cmd" => ("set /a 6*7", "\n42"),
            "fish" => ("echo hultui-(math 6 \\* 7)", "hultui-42"),
            _ => ("echo hultui-$((6*7))", "hultui-42"),
        };

        if (!Pump(() => c.ReadScreen().Length > 0)) return Fail($"{name} printed nothing");
        if (!Pump(() => c.Columns != 80 || c.Rows != 24)) return Fail("the grid was never sized to the window");
        c.Send(command + "\r");
        if (!Pump(() => c.ReadScreen().Contains(answer))) return Fail($"{name} never answered '{command}'");
        c.Send("exit 7\r");
        if (!Pump(() => c.State == TerminalState.Exited)) return Fail($"{name} did not exit");
        if (c.ExitCode != 7) return Fail($"{name} exited with {c.ExitCode?.ToString() ?? "no code"}, not 7");

        Console.WriteLine($"check passed in {clock.ElapsedMilliseconds} ms: {shell.Program} ran under a " +
            $"{c.Columns}×{c.Rows} pseudoterminal, answered, and exited with 7");
        return 0;
    }

    private static double Pct(double[] sorted, double p) =>
        sorted.Length == 0 ? double.NaN : sorted[Math.Min(sorted.Length - 1, (int)(p * sorted.Length))];

    /// <summary>
    /// Draws nothing; reads the frame counters each frame. The framework draws the whole tree on
    /// every frame it draws, so this runs once a frame, and the counters it reads are the previous
    /// frame's (a span cannot report itself).
    /// </summary>
    private sealed class FrameProbe : Element
    {
        private readonly Application _app;

        public FrameProbe(Application app)
        {
            _app = app;
            Box.Width = 1;
            Box.Height = 1;
        }

        public List<(float Render, float Flush)> Samples { get; } = [];
        public bool Recording { get; set; }

        public override void Render(IRenderContext ctx)
        {
            if (Recording) Samples.Add((_app.Performance.RenderTimeMs, _app.Performance.FlushTimeMs));
        }
    }
}
