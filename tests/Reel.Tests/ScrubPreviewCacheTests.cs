using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Media;
using Xunit;

namespace Reel.Tests;

/// <summary>
/// The policy behind the scrubber's stills: how many are made for a film, which one answers
/// for a position, and what happens while most of them do not exist yet.
///
/// <para>The decoding is not here. Making a still means a second mpv, a seek and a PNG, none
/// of which a headless test has — so the frame source is a fake that hands back a byte per
/// frame, and what is pinned is everything around it.</para>
/// </summary>
public class ScrubPreviewCacheTests
{
    /// <summary>A stand-in for mpv: records what was asked for, answers instantly, and can be
    /// told to fail either step.</summary>
    private sealed class FakeSource(bool opens = true) : IScrubFrameSource
    {
        public readonly List<TimeSpan> Grabbed = [];
        public bool Disposed;
        public Func<TimeSpan, byte[]?> Answer = _ => [1];

        public Task<bool> OpenAsync(string path, CancellationToken cancellationToken) =>
            Task.FromResult(opens);

        public Task<byte[]?> GrabAsync(TimeSpan position, CancellationToken cancellationToken)
        {
            lock (Grabbed) Grabbed.Add(position);
            return Task.FromResult(Answer(position));
        }

        public void Dispose() => Disposed = true;
    }

    private sealed class FakeImage : IImageSource
    {
        public Size IntrinsicSize => new(352, 198);
        public bool IsLoaded => true;
        public string? AlternateText => null;
    }

    private static ScrubPreviewCache Cache(FakeSource source) => new(source, _ => new FakeImage());

    private static int GrabbedCount(FakeSource source)
    {
        lock (source.Grabbed) return source.Grabbed.Count;
    }

    /// <summary>The fake answers at once, so a run is done in microseconds once its Task.Run is
    /// scheduled; what can take seconds is that scheduling, on a two-core CI runner with the other
    /// test classes busy (five seconds was missed there once). The deadline only bounds a failure.</summary>
    private static void WaitFor(Func<bool> done, int ms = 30_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(ms);
        while (!done() && DateTime.UtcNow < deadline) Thread.Sleep(5);
        Assert.True(done(), "the still run did not finish in time");
    }

    /// <summary>
    /// A film gets the cap spread evenly across it; a clip too short for that gets the
    /// closest spacing still worth having, rather than 120 stills a fifth of a second apart.
    /// </summary>
    [Theory]
    [InlineData(0.5, 2)]        // a 30-second clip: the floor
    [InlineData(4, 2)]          // four minutes: 240s / 120 is exactly the floor
    [InlineData(20, 10)]        // twenty minutes: evenly spread
    [InlineData(120, 60)]       // a two-hour film: a still a minute, and it covers all of it
    [InlineData(540, 270)]      // nine hours: still 120 stills, just further apart
    public void TheSpacingSuitsTheLength(double minutes, double expectedSeconds)
    {
        var interval = ScrubPreviewCache.IntervalFor(TimeSpan.FromMinutes(minutes));
        Assert.Equal(expectedSeconds, interval.TotalSeconds, 3);
    }

    /// <summary>However long the film, the set stays small enough to hold in memory.</summary>
    [Fact]
    public void ALongFilmStillGetsNoMoreThanTheCap()
    {
        var source = new FakeSource();
        using var cache = Cache(source);
        cache.Start("epic.mkv", TimeSpan.FromHours(9));

        Assert.True(cache.Count <= ScrubPreviewCache.MaxFrames);
        WaitFor(() => cache.Ready == cache.Count);
        Assert.Equal(ScrubPreviewCache.MaxFrames, cache.Ready);
    }

    /// <summary>The still for a position is the one made nearest it, and the card's label
    /// says the time that still is actually of — not where the pointer happens to be.</summary>
    [Fact]
    public void TheFrameForAPositionIsTheOneMadeNearestIt()
    {
        var source = new FakeSource();
        using var cache = Cache(source);
        cache.Start("film.mkv", TimeSpan.FromMinutes(40));   // 20s apart
        WaitFor(() => cache.Ready == cache.Count);

        Assert.Equal(TimeSpan.FromSeconds(20), cache.Interval);

        // 0:29 is nearer the 0:20 still than the 0:40 one.
        var frame = cache.Frame(TimeSpan.FromSeconds(29));
        Assert.NotNull(frame);
        Assert.Equal(TimeSpan.FromSeconds(20), frame!.Value.Time);

        // …and 0:31 the other way.
        Assert.Equal(TimeSpan.FromSeconds(40), cache.Frame(TimeSpan.FromSeconds(31))!.Value.Time);

        // The whole image is the frame: sprite-sheet cropping is the server's problem.
        Assert.Null(frame.Value.Source);
    }

    /// <summary>
    /// The stills cover the whole film, not just its beginning. An earlier version clamped
    /// the spacing to twenty seconds, which with a cap of 120 meant everything after the
    /// fortieth minute had no still at all — and scrubbing there showed the frame from 0:40.
    /// </summary>
    [Fact]
    public void TheStillsReachTheEndOfALongFilm()
    {
        var source = new FakeSource();
        using var cache = Cache(source);
        var length = TimeSpan.FromHours(3);
        cache.Start("epic.mkv", length);
        WaitFor(() => cache.Ready == cache.Count);

        // The last still is within one interval of the end, wherever that is.
        var last = cache.Frame(length)!.Value.Time!.Value;
        Assert.True(length - last <= cache.Interval,
            $"the last still is at {last}, {length - last} short of the end");
    }

    /// <summary>Past either end, the nearest still is the first or the last one.</summary>
    [Fact]
    public void ScrubbingPastEitherEndClampsToAStillThatExists()
    {
        var source = new FakeSource();
        using var cache = Cache(source);
        cache.Start("film.mkv", TimeSpan.FromHours(2));
        WaitFor(() => cache.Ready == cache.Count);

        Assert.Equal(TimeSpan.Zero, cache.Frame(TimeSpan.FromSeconds(-30))!.Value.Time);
        Assert.Equal(cache.Interval * (cache.Count - 1),
            cache.Frame(TimeSpan.FromHours(5))!.Value.Time);
    }

    /// <summary>
    /// The run takes a while, so scrubbing into a part that has not been made yet shows the
    /// nearest still that has — an approximate picture beats an empty card.
    /// </summary>
    [Fact]
    public void BeforeTheRunFinishesTheNearestStillThatExistsIsShown()
    {
        var source = new FakeSource
        {
            // Only the first ten minutes have been reached so far.
            Answer = at => at <= TimeSpan.FromMinutes(10) ? [1] : null,
        };

        using var cache = Cache(source);
        cache.Start("film.mkv", TimeSpan.FromHours(2));
        WaitFor(() => GrabbedCount(source) == cache.Count);

        var frame = cache.Frame(TimeSpan.FromMinutes(90));
        Assert.NotNull(frame);
        Assert.Equal(TimeSpan.FromMinutes(10), frame!.Value.Time);
    }

    /// <summary>Nothing made yet is not a still to draw — the overlay asks on every frame,
    /// and null is how it is told there is nothing to show.</summary>
    [Fact]
    public void WithNothingMadeYetThereIsNoFrame()
    {
        var source = new FakeSource { Answer = _ => null };

        using var cache = Cache(source);
        cache.Start("film.mkv", TimeSpan.FromHours(2));
        WaitFor(() => GrabbedCount(source) == cache.Count);

        Assert.Null(cache.Frame(TimeSpan.FromMinutes(20)));
    }

    /// <summary>A file whose length is not known yet — a stream, or a frame too early — gets
    /// no run at all rather than a run of one still at zero.</summary>
    [Fact]
    public void AFileOfUnknownLengthGetsNoStills()
    {
        var source = new FakeSource();
        using var cache = Cache(source);
        cache.Start("live.ts", TimeSpan.Zero);

        Assert.Equal(0, cache.Count);
        Assert.Null(cache.Frame(TimeSpan.FromMinutes(1)));
        Assert.Empty(source.Grabbed);
    }

    /// <summary>Opening another file drops the stills of the one before: they are pictures of
    /// a film nobody is watching any more.</summary>
    [Fact]
    public void OpeningAnotherFileForgetsTheStillsOfTheLast()
    {
        var source = new FakeSource();
        using var cache = Cache(source);
        cache.Start("first.mkv", TimeSpan.FromHours(2));
        WaitFor(() => cache.Ready == cache.Count);
        Assert.NotNull(cache.Frame(TimeSpan.FromMinutes(20)));

        // Stop clears synchronously, so the moment the second run starts the first film's
        // frames are gone — whatever the new run has managed since.
        source.Answer = _ => null;
        cache.Start("second.mkv", TimeSpan.FromHours(2));
        Assert.Null(cache.Frame(TimeSpan.FromMinutes(20)));
    }

    /// <summary>A machine with no decoder says so by failing to open, and nothing else
    /// happens — no frames asked for, no exception.</summary>
    [Fact]
    public void ASourceThatCannotOpenAsksForNothing()
    {
        var source = new FakeSource(opens: false);
        using var cache = Cache(source);
        cache.Start("film.mkv", TimeSpan.FromHours(2));

        Thread.Sleep(100);
        Assert.Empty(source.Grabbed);
        Assert.Null(cache.Frame(TimeSpan.FromMinutes(20)));
    }

    /// <summary>Disposing takes the source with it — it owns an mpv handle and a temporary
    /// folder, and both would outlive the window otherwise.</summary>
    [Fact]
    public void DisposingClosesTheSource()
    {
        var source = new FakeSource();
        var cache = Cache(source);
        cache.Start("film.mkv", TimeSpan.FromHours(2));
        cache.Dispose();

        Assert.True(source.Disposed);
    }
}
