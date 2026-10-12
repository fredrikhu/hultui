using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;

namespace PerfSuite;

static partial class Scenarios
{
    /// <summary>
    /// What a window costs while it waits: <c>Application.Run</c>'s own loop on the virtual clock
    /// (<c>HeadlessSession.RunLoop</c>) over an idle window, a minimized one changed while
    /// minimized, a toast's life and a tooltip's show delay. Each reports the loop's passes per
    /// second of virtual time, which is how often it woke: the loop parked 16 ms at a time until
    /// plan 17's round 3, ~60 passes a second whatever happened, and now waits for its next
    /// deadline or a wake. All of a step's time shows under build.
    /// </summary>
    private static IEnumerable<Scenario> LoopScenarios()
    {
        yield return new Scenario("loop/idle window, 5 s, real loop",
            "A window of 300 rows with nothing to do: Application.Run's own loop on the virtual clock for 5 s (all under build); reports the loop's passes per second",
            ctx =>
            {
                ctx.Mount(ScrollView.New().FlexGrow(1).Children(Ui.Rows(ctx.Rng, 300)));
                var loop = new LoopCount(ctx);
                return new Op
                {
                    Run = _ => loop.RunFor(TimeSpan.FromSeconds(5)),
                    Frames = 0,
                    Figure = () => loop.PassesPerSecond,
                    FigureUnit = "passes/s",
                    Check = _ => loop.Seconds < 5 ? $"the loop ran {loop.Seconds:0.00} s of 5" : null,
                };
            });

        yield return new Scenario("loop/minimized window changed, 5 s, real loop",
            "A minimized window of 300 rows that gains a row while minimized, as Junction's watcher refreshes a list: Application.Run's own loop on the virtual clock for 5 s (all under build); reports the loop's passes per second",
            ctx =>
            {
                var rows = Ui.Rows(ctx.Rng, 300);
                ctx.Mount(ScrollView.New().FlexGrow(1).Children(rows));
                var loop = new LoopCount(ctx);
                Element? added = null;
                return new Op
                {
                    Reset = _ =>
                    {
                        ctx.Window.Minimized = false;
                        if (added is not null) rows.RemoveChild(added);
                        added = null;
                    },
                    Run = _ =>
                    {
                        ctx.Window.Minimized = true;
                        rows.AddChild(added = Text.New("added while minimized"));
                        loop.RunFor(TimeSpan.FromSeconds(5));
                    },
                    Frames = 0,
                    Figure = () => loop.PassesPerSecond,
                    FigureUnit = "passes/s",
                    Check = _ => loop.Seconds < 5 ? $"the loop ran {loop.Seconds:0.00} s of 5" : null,
                };
            });

        yield return new Scenario("loop/toast held 2 s, real loop",
            "A toast over 300 rows, faded in, held 2 s and faded out: Application.Run's own loop on the virtual clock from the flash until it has gone (all under build); reports the loop's passes per second of its life",
            ctx =>
            {
                var screen = new ToastScreen(Ui.Rows(ctx.Rng, 300));
                ctx.Mount(screen.Mount());
                var loop = new LoopCount(ctx);
                return new Op
                {
                    Run = _ =>
                    {
                        screen.Toast.Flash("Saved", seconds: 2f);
                        loop.RunUntil(() => !screen.Toast.IsShowing, TimeSpan.FromSeconds(5));
                    },
                    Frames = 0,
                    Figure = () => loop.PassesPerSecond,
                    FigureUnit = "passes/s",
                    Check = _ => screen.Toast.IsShowing ? "the toast did not go"
                        : loop.Seconds is < 2.1 or > 2.5 ? $"the toast lived {loop.Seconds:0.00} s" : null,
                };
            });

        yield return new Scenario("loop/tooltip delay, real loop",
            "The pointer comes to rest on a button with a tooltip beside 300 rows: Application.Run's own loop on the virtual clock until the tooltip is shown (all under build); reports the loop's passes per second of the show delay",
            ctx =>
            {
                var owner = Box.New().Width(Dimension.Px(160)).Height(Dimension.Px(40)).Background(Ui.Accent);
                owner.TooltipText = "Opens the file in a new tab";
                int frames = 0;
                ctx.Mount(Flex.Column().FlexGrow(1).Children(
                    Flex.Row().Padding(12).FlexShrink(0).Children(owner),
                    ScrollView.New().FlexGrow(1).Children(Ui.Rows(ctx.Rng, 300)),
                    Canvas.New(_ => frames++).Width(Dimension.Px(1)).Height(Dimension.Px(1))));
                var loop = new LoopCount(ctx);
                bool shown = false;
                return new Op
                {
                    Reset = _ =>
                    {
                        // Away, and long enough for the tooltip to go.
                        ctx.Window.MoveMouse(400, 300);
                        ctx.Session.PumpFrame(TimeSpan.FromMilliseconds(200));
                        ctx.Session.PumpFrame();
                    },
                    Run = _ =>
                    {
                        var box = owner.WindowBorderBox;
                        ctx.Window.MoveMouse(box.X + box.Width / 2, box.Y + box.Height / 2);
                        int before = frames;
                        loop.RunUntil(() => ctx.Window.OverlayContents.Count > 0, TimeSpan.FromSeconds(3));
                        shown = ctx.Window.OverlayContents.Count > 0 && frames > before;
                    },
                    Frames = 0,
                    Figure = () => loop.PassesPerSecond,
                    FigureUnit = "passes/s",
                    Check = _ => !shown ? "the tooltip was not shown and drawn"
                        : loop.Seconds is < 0.45 or > 0.6 ? $"the tooltip came after {loop.Seconds:0.000} s" : null,
                };
            });
    }

    /// <summary>Runs the session's loop and counts its passes (every one but the pass that finds
    /// the run over) over the virtual time it ran.</summary>
    private sealed class LoopCount(Ctx ctx)
    {
        private int _passes;
        public double Seconds { get; private set; }
        public double PassesPerSecond => Seconds > 0 ? _passes / Seconds : 0;

        /// <summary>For <paramref name="span"/> of virtual time. An idle loop's last wait runs past
        /// it, to the end of the run (1 s more), with no pass in between.</summary>
        public void RunFor(TimeSpan span)
        {
            var clock = ctx.Session.Time;
            long start = clock.GetTimestamp();
            _passes = 0;
            ctx.Session.RunLoop(() =>
            {
                if (clock.GetElapsedTime(start) >= span) return true;
                _passes++;
                return false;
            }, limit: span + TimeSpan.FromSeconds(1));
            Seconds = span.TotalSeconds;
        }

        /// <summary>Until <paramref name="done"/>: the virtual time to the pass that found it.</summary>
        public void RunUntil(Func<bool> done, TimeSpan limit)
        {
            var clock = ctx.Session.Time;
            long start = clock.GetTimestamp();
            _passes = 0;
            Seconds = 0;
            ctx.Session.RunLoop(() =>
            {
                if (done())
                {
                    Seconds = clock.GetElapsedTime(start).TotalSeconds;
                    return true;
                }
                _passes++;
                return false;
            }, limit: limit);
        }
    }

    /// <summary>Rows with a toast on top, rebuilt as the toast comes and goes (ToastHost's owner).</summary>
    private sealed class ToastScreen(Element rows) : Component
    {
        public ToastHost Toast => _toast ??= new ToastHost(MarkDirty);
        private ToastHost? _toast;

        public override Element Build()
        {
            var root = Flex.Column().FlexGrow(1).Children(ScrollView.New().FlexGrow(1).Children(rows));
            if (Toast.Build() is { } strip) root.AddChild(strip);
            return root;
        }
    }
}
