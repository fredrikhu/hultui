using System.Diagnostics;
using System.IO.Pipes;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// <see cref="SingleInstance"/> over the real lock file and named pipe. Every test claims an app
/// id of its own: a fixed one would hand these launches to a Vesper or Reel left running on the
/// machine, and be handed theirs.
/// </summary>
public sealed class SingleInstanceTests
{
    private static string NewAppId() => "HultUITest-" + Guid.NewGuid().ToString("N");

    [Fact]
    public void TheFirstLaunchClaimsTheApp_AndALaterOneFindsIt()
    {
        var id = NewAppId();
        using var first = SingleInstance.Claim(id);
        using var later = SingleInstance.Claim(id);
        using var other = SingleInstance.Claim(NewAppId());

        Assert.True(first.IsFirst);
        Assert.False(later.IsFirst);
        Assert.True(other.IsFirst);
    }

    [Fact]
    public void WhenTheFirstCopyEnds_TheNextLaunchIsFirst()
    {
        var id = NewAppId();
        var first = SingleInstance.Claim(id);
        first.Dispose();

        using var next = SingleInstance.Claim(id);
        Assert.True(next.IsFirst);
    }

    [Fact]
    public void AForwardedLaunch_ArrivesOnTheUIThread_WithWhereItWasStarted()
    {
        using var session = HeadlessSession.Create();
        var id = NewAppId();
        using var first = SingleInstance.Claim(id);
        var got = new List<(SecondLaunch Launch, int Thread)>();
        first.Listen(launch => got.Add((launch, Environment.CurrentManagedThreadId)));

        using var later = SingleInstance.Claim(id);
        Assert.True(later.Forward(["film.mkv", "--from-the-start"]));
        session.PumpUntil(() => got.Count == 1);

        var (launch, thread) = got[0];
        Assert.Equal(Environment.CurrentManagedThreadId, thread);
        Assert.Equal(["film.mkv", "--from-the-start"], launch.Args);
        Assert.Equal(Environment.CurrentDirectory, launch.WorkingDirectory);
        Assert.Equal(Path.Combine(Environment.CurrentDirectory, "film.mkv"), launch.FullPath("film.mkv"));
    }

    [Fact]
    public void LaunchesThatArriveBeforeTheAppListens_AreKept_InOrder()
    {
        // A file manager starts one process per selected file at once; all but the first land
        // while the first is still building its window. The apps' own copies listened only
        // after that, and a launch that waited longer than half a second was dropped.
        using var session = HeadlessSession.Create();
        var id = NewAppId();
        using var first = SingleInstance.Claim(id);

        foreach (var name in new[] { "one.mkv", "two.mkv", "three.mkv" })
        {
            using var later = SingleInstance.Claim(id);
            Assert.True(later.Forward([name]), $"{name} was not taken before the app listened");
        }

        var got = new List<string>();
        first.Listen(launch => got.Add(launch.Args[0]));
        session.PumpUntil(() => got.Count == 3);
        Assert.Equal(["one.mkv", "two.mkv", "three.mkv"], got);
    }

    [Fact]
    public void ABurstOfLaunches_AllArrive()
    {
        using var session = HeadlessSession.Create();
        var id = NewAppId();
        using var first = SingleInstance.Claim(id);
        var got = new HashSet<string>();
        first.Listen(launch => got.Add(launch.Args[0]));

        var names = Enumerable.Range(0, 12).Select(i => $"episode-{i:00}.mkv").ToArray();
        var sends = names.Select(name => Task.Run(() =>
        {
            using var later = SingleInstance.Claim(id);
            return later.Forward([name]);
        })).ToArray();

        session.PumpUntil(() => sends.All(t => t.IsCompleted) && got.Count == names.Length, TimeSpan.FromSeconds(15));
        Assert.All(sends, t => Assert.True(t.Result));
        Assert.Equal(names.ToHashSet(), got);
    }

    [Fact]
    public void ACopyThatDoesNotAnswer_IsReported_SoTheLaunchCanOpenItsOwnWindow()
    {
        // The apps' copies returned and exited whatever happened; the request was gone.
        var id = NewAppId();
        using var hung = SingleInstance.Claim(id, listen: false);
        using var later = SingleInstance.Claim(id);

        var clock = Stopwatch.StartNew();
        Assert.False(later.ForwardWithin(["film.mkv"], TimeSpan.FromMilliseconds(300)));
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(3), $"took {clock.Elapsed} to give up");
    }

    [Fact]
    public void SomethingThatIsNotALaunch_DoesNotStopTheListening()
    {
        using var session = HeadlessSession.Create();
        var id = NewAppId();
        using var first = SingleInstance.Claim(id);
        var got = new List<SecondLaunch>();
        first.Listen(got.Add);

        using (var junk = new NamedPipeClientStream(".", first.PipeName, PipeDirection.InOut,
                   PipeOptions.CurrentUserOnly))
        {
            junk.Connect(2000);
            // A length past the cap. The listener hangs up on it, which the writer may see.
            try { junk.Write([0xFF, 0xFF, 0xFF, 0x7F, 1, 2, 3]); } catch (IOException) { }
        }
        using (var cut = new NamedPipeClientStream(".", first.PipeName, PipeDirection.InOut,
                   PipeOptions.CurrentUserOnly))
        {
            cut.Connect(2000);
            try { cut.Write(SingleInstance.Encode(["x"], "/")[..6]); } catch (IOException) { }   // gives up mid-message
        }

        using var later = SingleInstance.Claim(id);
        Assert.True(later.Forward(["still.mkv"]));
        session.PumpUntil(() => got.Count == 1);
        Assert.Equal("still.mkv", got[0].Args[0]);
    }

    [Fact]
    public void TheMessage_CarriesAnyText()
    {
        string[] args = ["", "två ord", "東京/映画.mkv", "line\nbreak", new string('x', 5000)];
        var message = SingleInstance.Encode(args, @"C:\Users\någon");
        Assert.Equal(message.Length - 4, BitConverter.ToInt32(message));

        var launch = SingleInstance.Decode(message[4..]);
        Assert.Equal(args, launch.Args);
        Assert.Equal(@"C:\Users\någon", launch.WorkingDirectory);

        var future = (byte[])message[4..].Clone();
        future[0] = 2;
        Assert.Throws<InvalidDataException>(() => SingleInstance.Decode(future));
    }

    [Fact]
    public void FullPath_ResolvesAgainstTheLaunch_AndLeavesWhatItCannotResolve()
    {
        var dir = Path.Combine(Path.GetTempPath(), "where-it-was-started");
        var launch = new SecondLaunch([], dir);

        Assert.Equal(Path.Combine(dir, "a.mkv"), launch.FullPath("a.mkv"));
        Assert.Equal(Path.GetFullPath(Path.Combine(dir, "..", "b.mkv")), launch.FullPath(Path.Combine("..", "b.mkv")));
        Assert.Equal(Path.Combine(Path.GetTempPath(), "c.mkv"), launch.FullPath(Path.Combine(Path.GetTempPath(), "c.mkv")));
        Assert.Equal("a.mkv", new SecondLaunch([], "not/a/full/path").FullPath("a.mkv"));
    }
}
