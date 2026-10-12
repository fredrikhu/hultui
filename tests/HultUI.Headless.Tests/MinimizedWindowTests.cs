using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using Xunit;
using TextB = HultUI.Core.Builders.Text;

namespace HultUI.Headless.Tests;

/// <summary>
/// A minimized window is not drawn: its frames run up to the drawing (assistive technology's
/// actions, style, layout, the accessibility tree) and keep the paint dirt for the first frame
/// after the restore. What those frames do must happen once per change, not once per pass of
/// the loop, which keeps passing while the window is dirty and minimized (plan 17, round 3).
/// </summary>
public class MinimizedWindowTests
{
    /// <summary>A box that counts the accessibility trees built over it: the builder asks each
    /// node's element for its text source once, so as the root it is asked once per build.</summary>
    private sealed class CountingRoot : BoxElement
    {
        public int Builds;

        internal override IAccessibleTextSource? AccessibleTextSource
        {
            get
            {
                Builds++;
                return null;
            }
        }
    }

    private static bool Contains(AccessibilityNode node, string name)
    {
        if (node.Name == name) return true;
        foreach (var child in node.Children)
            if (Contains(child, name)) return true;
        return false;
    }

    [Fact]
    public void AChangeWhileMinimized_RebuildsTheAccessibilityTreeOnce_NotOnEveryPass()
    {
        using var session = HeadlessSession.Create(400, 300);
        var root = new CountingRoot();
        for (int i = 0; i < 20; i++) root.AddChild(TextB.New($"row {i}"));
        session.Window.SetRoot(root);
        var bridge = session.Window.InstallAccessibilityBridge();
        session.PumpFrames(2);
        session.Window.Minimized = true;
        session.PumpFrames(2);
        int builds = root.Builds;

        // Junction's file watcher refreshing a list while its window is minimized.
        root.AddChild(TextB.New("added while minimized"));
        for (int i = 0; i < 30; i++)
            Assert.False(session.PumpFrame(), "a minimized window drew a frame");

        // Built once, at the first pass, and told: a reader still hears the change.
        Assert.Equal(builds + 1, root.Builds);
        Assert.True(Contains(bridge.CurrentTree!, "added while minimized"));
        // The paint waits for the restore, which draws it and builds nothing more.
        Assert.NotEqual(DirtyFlags.None, root.DirtyFlags & DirtyFlags.Paint);
        session.Window.Minimized = false;
        Assert.True(session.PumpFrame(), "the restore drew nothing");
        Assert.Equal(DirtyFlags.None, root.DirtyFlags);
        Assert.Equal(builds + 1, root.Builds);
    }

    [Fact]
    public void EachChangeWhileMinimized_IsBuiltOnce()
    {
        using var session = HeadlessSession.Create(400, 300);
        var root = new CountingRoot();
        root.AddChild(TextB.New("first"));
        session.Window.SetRoot(root);
        var bridge = session.Window.InstallAccessibilityBridge();
        session.PumpFrames(2);
        session.Window.Minimized = true;
        int builds = root.Builds;

        for (int change = 1; change <= 3; change++)
        {
            root.AddChild(TextB.New($"change {change}"));
            session.PumpFrames(10);
            Assert.Equal(builds + change, root.Builds);
            Assert.True(Contains(bridge.CurrentTree!, $"change {change}"));
        }
    }

    // ── GPU image sources ──
    //
    // A minimized window still serves the GPU sources in its tree (a video: mpv waits for its
    // frames to be taken), and finding them is a walk of the whole tree on every pass the window
    // is dirty. It looks only while one can exist: a live GpuFrameSink, or an image holding a
    // GPU source of another kind.

    /// <summary>A producer that never has a frame; the sink in front of it is what gets served.</summary>
    private sealed class IdleRenderer : IGpuFrameRenderer
    {
        public GpuSinkMode? Decided;
        public void OnModeDecided(GpuSinkMode mode) => Decided = mode;
        public void PrepareGL(Func<string, nint> getProcAddress) { }
        public bool TryGetFrameSize(out int width, out int height)
        {
            width = height = 0;
            return false;
        }
        public bool RendersBottomUp => true;
        public bool Render(Func<string, nint> getProcAddress, in GpuFrameTarget target, int width, int height) => false;
        public void ReleaseGL() { }
    }

    /// <summary>An app's own GPU source: not a sink, so no count of live sinks sees it.</summary>
    private sealed class OwnGpuSource : IGpuImageSource
    {
        public int Served;
        public Size IntrinsicSize => new(4, 4);
        public bool IsLoaded => true;
        public string? AlternateText => null;
        public bool TryRenderFrame(IExternalGLSurface surface, out GpuFrame frame)
        {
            frame = default;
            return false;
        }
        public void ServiceUndrawn(IExternalGLSurface? surface) => Served++;
    }

    /// <summary>A minimized window of 500 rows and a box to dirty on every pass, as a looping
    /// tween does.</summary>
    private static (HeadlessSession Session, BoxElement Root, BoxElement Ticker) Busy()
    {
        var session = HeadlessSession.Create(400, 300);
        var ticker = Box.New().Width(Dimension.Px(10)).Height(Dimension.Px(10));
        var root = Flex.Column().Children(ticker);
        for (int i = 0; i < 500; i++) root.AddChild(TextB.New($"row {i}"));
        session.Window.SetRoot(root);
        session.PumpFrame();
        session.Window.Minimized = true;
        return (session, root, ticker);
    }

    private static void Tick(HeadlessSession session, BoxElement ticker, int passes)
    {
        for (int i = 0; i < passes; i++)
        {
            ticker.MarkPaintDirty();
            Assert.False(session.PumpFrame(), "a minimized window drew a frame");
        }
    }

    [Fact]
    public void AMinimizedWindow_LooksForGpuSources_OnlyWhileOneCanExist()
    {
        Assert.False(GpuFrameSink.MayNeedUndrawnService,
            "an earlier test left a GpuFrameSink undisposed, or an image holding a GPU source");
        var (session, root, ticker) = Busy();
        using var _ = session;
        var window = session.Window.Window;
        int walks = window.UndrawnSourceWalks;

        // Dirty on every pass, with nothing to find: no walk.
        Tick(session, ticker, 30);
        Assert.Equal(walks, window.UndrawnSourceWalks);

        // A player opened while minimized: looked for on every pass, found and served — with no
        // GL here it decides pixels, as its first draw would have.
        var renderer = new IdleRenderer();
        var sink = new GpuFrameSink(new PixelFrameSink(), renderer);
        var image = ImageElement.New(sink);
        try
        {
            root.AddChild(image);
            Tick(session, ticker, 3);
            Assert.Equal(walks + 3, window.UndrawnSourceWalks);
            Assert.Equal(GpuSinkMode.Pixels, renderer.Decided);
        }
        finally
        {
            sink.Dispose();
        }

        // Closed: no walk again.
        root.RemoveChild(image);
        walks = window.UndrawnSourceWalks;
        Tick(session, ticker, 30);
        Assert.Equal(walks, window.UndrawnSourceWalks);
    }

    [Fact]
    public void AGpuSourceOfAnotherKind_IsStillServedWhileMinimized()
    {
        var (session, root, ticker) = Busy();
        using var _ = session;
        var source = new OwnGpuSource();
        var image = ImageElement.New(source);
        try
        {
            root.AddChild(image);
            Tick(session, ticker, 3);
            Assert.Equal(3, source.Served);
        }
        finally
        {
            // An image holding one keeps every minimized window looking; let go of it.
            image.Source = null;
        }
        Assert.False(GpuFrameSink.MayNeedUndrawnService);
    }
}
