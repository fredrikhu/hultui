using HultUI.Core.Builders;
using HultUI.Core.Primitives;
using SkiaSharp;
using Xunit;

namespace HultUI.Headless.Tests;

using Text = HultUI.Core.Builders.Text;

/// <summary>
/// <see cref="Window.SaveFrame"/> writes the frame HultUI drew, from its own surface, at device
/// pixels: what a test or a tool gets instead of a screen grab that can come back half drawn.
/// </summary>
public sealed class SaveFrameTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hultui-saveframe-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (DirectoryNotFoundException) { }
    }

    private static HeadlessSession Session()
    {
        // 150 %: the scale the half-scale screen grabs were taken at.
        var session = HeadlessSession.Create(320, 200, scale: 1.5f);
        session.Window.SetRoot(Flex.Column(gap: 8).Padding(new Thickness(12)).Children(
            Box.New().Width(120).Height(40).Background(Color.FromHex("#4F46E5")).CornerRadius(8f),
            Text.New("A frame saved whole"),
            Box.New().Width(60).Height(30).Background(Color.FromHex("#EF4444"))));
        session.PumpFrames(2);
        return session;
    }

    private static void AssertSamePixels(SKImage expected, SKBitmap actual)
    {
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        using var want = new SKBitmap(new SKImageInfo(expected.Width, expected.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        Assert.True(expected.ReadPixels(want.Info, want.GetPixels(), want.RowBytes, 0, 0));
        using var got = new SKBitmap(want.Info);
        Assert.True(actual.CopyTo(got, SKColorType.Rgba8888));
        for (int y = 0; y < want.Height; y++)
            for (int x = 0; x < want.Width; x++)
                Assert.True(want.GetPixel(x, y) == got.GetPixel(x, y),
                    $"pixel ({x},{y}): drew {want.GetPixel(x, y)}, saved {got.GetPixel(x, y)}");
    }

    [Fact]
    public void SavedFrame_IsTheHeadlessRenderersOwnOutput_PixelForPixel_AtDevicePixels()
    {
        using var session = Session();
        string path = Path.Combine(_dir, "frame.png");

        // Nothing is dirty: the save asks for the frame itself.
        session.PumpUntilCompleted(session.Window.Window.SaveFrame(path));

        using var saved = SKBitmap.Decode(path);
        Assert.Equal(480, saved.Width);
        Assert.Equal(300, saved.Height);
        using var drawn = session.Window.CapturePixels();
        AssertSamePixels(drawn, saved);
        Assert.False(File.Exists(path + ".partial"));
    }

    [Fact]
    public void SaveFrame_FromAnotherThread_IsWrittenByTheUIThreadsNextFrame()
    {
        using var session = Session();
        string path = Path.Combine(_dir, "other-thread.png");
        Task? save = null;
        var other = new Thread(() => save = session.Window.Window.SaveFrame(path));
        other.Start();
        other.Join();

        session.PumpUntilCompleted(save!);

        using var saved = SKBitmap.Decode(path);
        using var drawn = session.Window.CapturePixels();
        AssertSamePixels(drawn, saved);
    }

    [Fact]
    public void TwoSavesBeforeAFrame_AreBothWrittenFromIt()
    {
        using var session = Session();
        var first = session.Window.Window.SaveFrame(Path.Combine(_dir, "a.png"));
        var second = session.Window.Window.SaveFrame(Path.Combine(_dir, "b.png"));
        session.PumpUntilCompleted(Task.WhenAll(first, second));
        Assert.Equal(File.ReadAllBytes(Path.Combine(_dir, "a.png")), File.ReadAllBytes(Path.Combine(_dir, "b.png")));
    }

    [Fact]
    public void ASaveTheWindowNeverDrew_FailsWhenTheWindowCloses()
    {
        var session = Session();
        session.Window.Minimized = true;
        var save = session.Window.Window.SaveFrame(Path.Combine(_dir, "never.png"));
        session.PumpFrames(3);
        Assert.False(save.IsCompleted, "a minimized window is not drawn, so its frame waits");
        session.Dispose();
        Assert.True(save.IsFaulted);
        Assert.IsType<ObjectDisposedException>(save.Exception!.GetBaseException());
    }

    [Fact]
    public void ASaveFromAnotherThread_FailsWhenTheApplicationGoesBeforeItArrives()
    {
        var session = Session();
        var window = session.Window.Window;
        Task? before = null;
        var other = new Thread(() => before = window.SaveFrame(Path.Combine(_dir, "before.png")));
        other.Start();
        other.Join();
        session.Dispose();   // the posted save never reaches the UI thread

        Task? after = null;
        other = new Thread(() => after = window.SaveFrame(Path.Combine(_dir, "after.png")));
        other.Start();
        other.Join();

        foreach (var save in new[] { before!, after! })
        {
            Assert.True(save.IsFaulted);
            Assert.IsType<ObjectDisposedException>(save.Exception!.GetBaseException());
        }
    }

    [Fact]
    public void WithSessionsSideBySide_ARequestIsServedByTheSessionWithTheWindow()
    {
        using var a = Session();
        using var b = Session();
        b.CreateWindow("B").SetRoot(Box.New().Background(Color.FromHex("#10B981")));
        b.PumpFrames(2);
        using var watchA = FrameRequests.Start(a.Application, _dir);
        using var watchB = FrameRequests.Start(b.Application, _dir);

        string staged = Path.Combine(_dir, "x.tmp");
        File.WriteAllText(staged, "B");
        File.Move(staged, Path.Combine(_dir, "x.request"));

        string png = Path.Combine(_dir, "x.png");
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!File.Exists(png) && DateTime.UtcNow < until)
        {
            a.PumpFrame();   // A has no window B: it leaves the request alone
            b.PumpFrame();
        }
        Assert.True(File.Exists(png));
        Assert.False(File.Exists(Path.Combine(_dir, "x.error")));
    }

    [Fact]
    public void ARequestFileFromATool_WritesThePrimaryWindowsFrame_WithoutInput()
    {
        using var session = Session();
        using var watch = FrameRequests.Start(session.Application, _dir);
        Assert.NotNull(watch);

        // Written under another name and renamed, as the hook asks, so it is never read half written.
        string staged = Path.Combine(_dir, "desk.tmp");
        File.WriteAllText(staged, "");
        File.Move(staged, Path.Combine(_dir, "desk.request"));

        string png = Path.Combine(_dir, "desk.png");
        session.PumpUntil(() => File.Exists(png), TimeSpan.FromSeconds(10));
        Assert.False(File.Exists(Path.Combine(_dir, "desk.request")));
        using var saved = SKBitmap.Decode(png);
        using var drawn = session.Window.CapturePixels();
        AssertSamePixels(drawn, saved);
    }

    [Fact]
    public void ARequestStillHeldOpen_IsServedOnceReleased_NotRefused()
    {
        using var session = Session();
        using var watch = FrameRequests.Start(session.Application, _dir);

        // Written in place and still open, as its writer or a virus scanner looking at the new
        // file holds it: once refused as "being used by another process".
        string request = Path.Combine(_dir, "held.request");
        string png = Path.Combine(_dir, "held.png");
        string error = Path.Combine(_dir, "held.error");
        using (var held = new FileStream(request, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
        {
            var until = DateTime.UtcNow + TimeSpan.FromMilliseconds(600);
            while (DateTime.UtcNow < until)
            {
                session.PumpFrame();
                Thread.Sleep(10);
            }
            Assert.False(File.Exists(error), File.Exists(error) ? File.ReadAllText(error) : null);
            Assert.False(File.Exists(png));
        }

        session.PumpUntil(() => File.Exists(png), TimeSpan.FromSeconds(10));
        Assert.False(File.Exists(error));
        Assert.False(File.Exists(request));
    }

    [Fact]
    public void ARequestForAWindowThatIsNotThere_SaysSo()
    {
        using var session = Session();
        using var watch = FrameRequests.Start(session.Application, _dir);

        string staged = Path.Combine(_dir, "missing.tmp");
        File.WriteAllText(staged, "No such window");
        File.Move(staged, Path.Combine(_dir, "missing.request"));

        string error = Path.Combine(_dir, "missing.error");
        string? said = null;
        session.PumpUntil(() =>
        {
            // A scanner can hold a file it just saw arrive; a tool waits the same way.
            try { said = File.ReadAllText(error); }
            catch (IOException) { }
            return said is not null;
        }, TimeSpan.FromSeconds(10));
        Assert.Contains("No such window", said);
        Assert.False(File.Exists(Path.Combine(_dir, "missing.png")));
    }
}
