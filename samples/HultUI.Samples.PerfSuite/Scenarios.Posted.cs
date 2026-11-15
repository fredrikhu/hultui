using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Styling;
using HultUI.Headless;

namespace PerfSuite;

static partial class Scenarios
{
    /// <summary>
    /// Work posted to the UI thread, in <c>Application.Run</c>'s own loop on the virtual clock
    /// (<c>HeadlessSession.RunLoop</c>): a list component whose <c>Build</c> reads a signal that every
    /// post bumps, as a framework user's list was rebuilt on each change a loader posted (their
    /// report, plan 17 round 15). A build moves the virtual clock by what it costs, and the posts
    /// due meanwhile are made then, as a background thread's would be. Each row reports the rebuilds
    /// per frame drawn, and beside it the frames drawn, the most rebuilds in one, the longest frame
    /// (the virtual time between two frames drawn, the first counted from the first post) and when
    /// the last change was drawn. All of a step's time shows under build.
    /// </summary>
    private static IEnumerable<Scenario> PostedScenarios()
    {
        yield return Posted("loop/500 posts before a frame, 20 ms builds, real loop",
            "A burst of 500 posts, each bumping a signal a 40-row list component's Build reads, made before a frame; a build moves the virtual clock by 20 ms. Application.Run's own loop on the virtual clock until the last change is drawn (all under build); reports rebuilds per frame drawn, with the frames, the most rebuilds in one, the longest frame and when the last change was drawn",
            posts: 500, every: TimeSpan.Zero, buildCost: TimeSpan.FromMilliseconds(20));

        yield return Posted("loop/a post every 50 ms for 2 s, 60 ms builds, real loop",
            "A loader posting a change every 50 ms for 2 s, each bumping a signal a 40-row list component's Build reads, while a build moves the virtual clock by 60 ms (builds slower than the posts, as the report's list was). Application.Run's own loop on the virtual clock until the last change is drawn (all under build); reports rebuilds per frame drawn, with the frames, the most rebuilds in one, the longest frame and when the last change was drawn",
            posts: 40, every: TimeSpan.FromMilliseconds(50), buildCost: TimeSpan.FromMilliseconds(60));

        yield return Posted("loop/a post every 5 ms for 2 s, 20 ms builds, real loop",
            "A background thread posting a change every 5 ms for 2 s, each bumping a signal a 40-row list component's Build reads, while a build moves the virtual clock by 20 ms. Application.Run's own loop on the virtual clock until the last change is drawn (all under build); reports rebuilds per frame drawn, with the frames, the most rebuilds in one, the longest frame and when the last change was drawn",
            posts: 400, every: TimeSpan.FromMilliseconds(5), buildCost: TimeSpan.FromMilliseconds(20));
    }

    private static Scenario Posted(string name, string description, int posts, TimeSpan every, TimeSpan buildCost) =>
        new(name, description, ctx =>
        {
            var list = new BumpedList(ctx.Session, ctx.Rng, buildCost);
            ctx.Mount(new ComponentHost(list));
            var stream = new PostStream(ctx.Session, list);
            list.Stream = stream;
            return new Op
            {
                Run = _ =>
                {
                    list.StartRun();
                    stream.Start(posts, every);
                    ctx.Session.RunLoop(() => stream.Done && list.Applied == posts && list.DrawnRev == list.Rev.Value,
                        eventEvery: every > TimeSpan.Zero ? every : null,
                        onEvent: every > TimeSpan.Zero ? stream.PostDue : null,
                        limit: TimeSpan.FromSeconds(60));
                },
                Frames = 0,
                Figure = () => list.RunBuilds / (double)Math.Max(1, list.RunFrames),
                FigureUnit = "rebuilds/frame",
                Detail = () => $"{list.RunFrames} frames, at most {list.MostInAFrame} rebuilds in one, longest {list.LongestFrameMs:0} ms, last change drawn at {list.DoneMs:0} ms",
                Check = _ => list.Applied != posts ? $"{list.Applied} of {posts} posts ran"
                    : list.DrawnRev != list.Rev.Value ? $"the list was drawn at {list.DrawnRev}, not {list.Rev.Value}" : null,
            };
        })
        { Width = 480, Height = 360 };

    /// <summary>
    /// The posts of a producer on another thread, on the session's virtual clock: <see cref="PostDue"/>
    /// makes every post whose time has come (all of them at once for a burst), and is called wherever
    /// that clock moves, the loop's waits and the list's builds.
    /// </summary>
    private sealed class PostStream(HeadlessSession session, BumpedList list)
    {
        private long _start;
        private long _every;
        private int _count, _posted;
        private Action? _bump;

        public bool Done => _posted == _count;

        public void Start(int count, TimeSpan every)
        {
            _start = session.Time.GetTimestamp();
            _every = every.Ticks;
            _count = count;
            _posted = 0;
            _bump ??= list.Bump;
            PostDue();
        }

        public void PostDue()
        {
            long now = session.Time.GetTimestamp();
            while (_posted < _count && (_every == 0 || _start + (_posted + 1) * _every <= now))
            {
                if (_posted++ == 0) list.FirstPosted();
                HultUI.Application.InvokeOnMainThread(_bump!);
            }
        }
    }

    /// <summary>
    /// A list of 40 rows whose Build reads <see cref="Rev"/>, which each post bumps; a build moves
    /// the virtual clock by its cost. Counts its builds and the frames drawn during a run.
    /// </summary>
    private sealed class BumpedList : Component
    {
        private readonly HeadlessSession _session;
        private readonly TimeSpan _buildCost;
        private readonly string[] _labels;
        private int _builtRev, _sinceFrame;
        private long _runStart, _lastFrameAt;

        public BumpedList(HeadlessSession session, Random rng, TimeSpan buildCost)
        {
            _session = session;
            _buildCost = buildCost;
            _labels = new string[40];
            for (int i = 0; i < _labels.Length; i++) _labels[i] = Ui.Label(rng);
        }

        public Signal<int> Rev { get; } = new(0);
        public PostStream? Stream { get; set; }
        public int Applied { get; private set; }
        public int DrawnRev { get; private set; }
        public int RunBuilds { get; private set; }
        public int RunFrames { get; private set; }
        public int MostInAFrame { get; private set; }
        public double LongestFrameMs { get; private set; }
        public double DoneMs { get; private set; }

        /// <summary>A post: one change applied.</summary>
        public void Bump()
        {
            Applied++;
            Rev.Value++;
        }

        public void StartRun()
        {
            Applied = 0;
            RunBuilds = RunFrames = MostInAFrame = _sinceFrame = 0;
            LongestFrameMs = DoneMs = 0;
            _runStart = _lastFrameAt = _session.Time.GetTimestamp();
        }

        /// <summary>The run's first post: its first frame, and when its last change is drawn, are
        /// counted from here.</summary>
        public void FirstPosted() => _runStart = _lastFrameAt = _session.Time.GetTimestamp();

        public override Element Build()
        {
            int rev = Rev.Value;
            _builtRev = rev;
            RunBuilds++;
            _sinceFrame++;
            if (_buildCost > TimeSpan.Zero)
            {
                _session.AdvanceTime(_buildCost);
                Stream?.PostDue();
            }
            var rows = new Element[_labels.Length];
            for (int i = 0; i < rows.Length; i++) rows[i] = Ui.Row(i + 1, $"{_labels[i]} · {rev}");
            return Flex.Column().FlexGrow(1).Children(
                Text.New($"Revision {rev}").FontSize(14f).FontWeight(FontWeight.SemiBold).Padding(8).FlexShrink(0),
                ScrollView.New().FlexGrow(1).Children(Flex.Column().FlexShrink(0).Children(rows)),
                Canvas.New(_ => OnFrame()).Width(Dimension.Px(1)).Height(Dimension.Px(1)));
        }

        private void OnFrame()
        {
            long now = _session.Time.GetTimestamp();
            RunFrames++;
            MostInAFrame = Math.Max(MostInAFrame, _sinceFrame);
            _sinceFrame = 0;
            LongestFrameMs = Math.Max(LongestFrameMs, (now - _lastFrameAt) / (double)TimeSpan.TicksPerMillisecond);
            _lastFrameAt = now;
            if (_builtRev != DrawnRev) DoneMs = (now - _runStart) / (double)TimeSpan.TicksPerMillisecond;
            DrawnRev = _builtRev;
        }
    }
}
