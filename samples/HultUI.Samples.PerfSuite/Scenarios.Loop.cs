using HultUI.Core.Animation;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Styling;

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

        yield return new Scenario("loop/toast over a screen built anew, real loop",
            "A toast over a screen whose Build makes its ~300 elements again, as an app's does (Junction, Aperture and Meridian hand ToastHost their MarkDirty): Application.Run's own loop on the virtual clock from the flash until it has gone, held 1.7 s (all under build); reports the screen's builds per toast",
            ctx =>
            {
                var screen = new AppScreen(ctx.Rng);
                ctx.Mount(screen.Mount());
                var loop = new LoopCount(ctx);
                int before = 0;
                return new Op
                {
                    Run = _ =>
                    {
                        before = screen.Builds;
                        screen.Brightest = 0f;
                        screen.Toast.Flash("Fetched origin");
                        loop.RunUntil(() => !screen.Toast.IsShowing, TimeSpan.FromSeconds(5));
                    },
                    Frames = 0,
                    Figure = () => screen.Builds - before,
                    FigureUnit = "builds/toast",
                    Check = _ => screen.Toast.IsShowing ? "the toast did not go"
                        : screen.Brightest < 0.99f ? $"the toast was drawn at {screen.Brightest:0.00} at most"
                        : loop.Seconds is < 1.85 or > 1.95 ? $"the toast lived {loop.Seconds:0.000} s" : null,
                };
            });

        foreach (bool inPlace in new[] { false, true })
        {
            yield return new Scenario(inPlace
                    ? "loop/spinner on a screen built anew, turned in place, real loop"
                    : "loop/spinner on a screen built anew, rebuilt per step, real loop",
                inPlace
                    ? "The same screen with a turning spinner whose looping tween writes the live spinner's Transform through a Ref, as Junction's busy buttons now do: Application.Run's own loop on the virtual clock for 30 frames (all under build); reports the screen's builds per frame drawn"
                    : "A screen whose Build makes its ~300 elements again, with a turning spinner whose looping tween writes a phase field and calls the screen's MarkDirty, as Junction's busy buttons and toolbar fetch did: Application.Run's own loop on the virtual clock for 30 frames (all under build); reports the screen's builds per frame drawn",
                ctx =>
                {
                    var screen = new AppScreen(ctx.Rng) { Spinning = true };
                    ctx.Mount(screen.Mount());
                    int builds = 0, frames = 0, turnsDrawn = 0;
                    return new Op
                    {
                        Run = _ =>
                        {
                            int b0 = screen.Builds, f0 = screen.Frames, t0 = screen.TurnsDrawn;
                            var spin = Animate.Value(0f, 1f, inPlace ? screen.TurnInPlace : screen.TurnByRebuild)
                                .Duration(0.9f).Ease(Easing.Linear).Loop().Start();
                            ctx.Session.RunLoop(() => screen.Frames - f0 >= 30, limit: TimeSpan.FromSeconds(5));
                            spin.Cancel();
                            builds = screen.Builds - b0;
                            frames = screen.Frames - f0;
                            turnsDrawn = screen.TurnsDrawn - t0;
                        },
                        Frames = 0,
                        Figure = () => builds / (double)Math.Max(1, frames),
                        FigureUnit = "rebuilds/frame",
                        Check = _ => turnsDrawn < frames - 1 ? $"the spinner was drawn turned {turnsDrawn} times in {frames} frames"
                            : MathF.Abs(screen.SpinnerShown - screen.Turn) > 0.001f ? $"the spinner on screen is at {screen.SpinnerShown:0.000} of a turn, not {screen.Turn:0.000}"
                            : inPlace && builds > 0 ? $"{builds} rebuilds while it turned in place" : null,
                    };
                });
        }
    }

    /// <summary>
    /// A screen built the way an app builds its own: 48 rows of six elements and a header made
    /// again by every Build, about 300 elements, a toast from <see cref="ToastHost"/> handed this
    /// component's MarkDirty, and, when asked, a spinner in the header. Counts its builds and the
    /// frames drawn, and keeps the brightest the toast was drawn at.
    /// </summary>
    private sealed class AppScreen : Component
    {
        private readonly string[] _labels;
        private readonly Ref<BoxElement> _spinner = new();
        private ToastHost? _toast;
        private ToastElement? _shownToast;

        public AppScreen(Random rng)
        {
            _labels = new string[48];
            for (int i = 0; i < _labels.Length; i++) _labels[i] = Ui.Label(rng);
        }

        public ToastHost Toast => _toast ??= new ToastHost(MarkDirty);
        public int Builds { get; private set; }
        public int Frames { get; private set; }
        public float Brightest { get; set; }
        public bool Spinning { get; init; }
        /// <summary>Where the spinner is, 0..1 of a turn.</summary>
        public float Turn { get; private set; }
        /// <summary>The turn the spinner on screen was last drawn at.</summary>
        public float SpinnerShown { get; private set; }
        /// <summary>Frames that drew the spinner at a turn the frame before had not.</summary>
        public int TurnsDrawn { get; private set; }

        /// <summary>A step that writes the phase and rebuilds the screen.</summary>
        public void TurnByRebuild(float turn)
        {
            Turn = turn;
            MarkDirty();
        }

        /// <summary>A step that writes the phase and the spinner on screen: a repaint.</summary>
        public void TurnInPlace(float turn)
        {
            Turn = turn;
            if (_spinner.Current is { } spinner) spinner.Transform = Transform2D.Rotate(turn * 360f);
        }

        public override Element Build()
        {
            Builds++;
            var header = Flex.Row(gap: 8).Padding(10).FlexShrink(0).AlignItems(AlignItems.Center).Children(
                Text.New("Repository").FontSize(14f).FontWeight(FontWeight.SemiBold),
                Box.New().FlexGrow(1));
            if (Spinning)
                header.AddChild(Box.New().Width(Dimension.Px(14)).Height(Dimension.Px(14)).CornerRadius(7f)
                    .BorderColor(Ui.Accent).BorderWidth(2f).Transform(Transform2D.Rotate(Turn * 360f)).WithRef(_spinner));
            var rows = new Element[_labels.Length];
            for (int i = 0; i < rows.Length; i++) rows[i] = Ui.Row(i + 1, _labels[i]);
            var root = Flex.Column().FlexGrow(1).Children(
                header,
                ScrollView.New().FlexGrow(1).Children(Flex.Column().FlexShrink(0).Children(rows)),
                Canvas.New(_ => OnFrame()).Width(Dimension.Px(1)).Height(Dimension.Px(1)));
            if (Toast.Build() is { } strip) root.AddChild(strip);
            return root;
        }

        private void OnFrame()
        {
            Frames++;
            if (_spinner.Current?.Transform is { } turned && turned.RotationDegrees / 360f != SpinnerShown)
            {
                SpinnerShown = turned.RotationDegrees / 360f;
                TurnsDrawn++;
            }
            if (_toast is not { IsShowing: true }) { _shownToast = null; return; }
            _shownToast ??= FindToast(Mount());
            if (_shownToast is not null) Brightest = MathF.Max(Brightest, _shownToast.Opacity);
        }

        private static ToastElement? FindToast(Element element)
        {
            if (element is ToastElement toast) return toast;
            var children = element.Children;
            for (int i = 0; i < children.Count; i++)
                if (FindToast(children[i]) is { } found) return found;
            return null;
        }
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
