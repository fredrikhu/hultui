using HultUI.Core.Builders;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Headless.Tests;

// Inside a HultUI.* namespace bare `Text` binds to the HultUI.Text namespace.
using Text = HultUI.Core.Builders.Text;

/// <summary>
/// The damage probe reports what a damage-rect renderer would have repainted; these
/// pin its math on known trees so the workload numbers it produces can be trusted.
/// The probe is process-global — each test attaches and detaches around its frames.
/// </summary>
public class DamageProbeTests
{
    [Fact]
    public void AFirstFrameWithContentCornerToCorner_UnionsTheWindow()
    {
        // The probe reports marked CONTENT, not the cleared surface: a fresh tree
        // whose only leaf is one small label honestly reports that label. What makes
        // the union metric trustworthy on real scenes is that content spans them —
        // pinned here with leaves in opposite corners.
        var frames = new List<DamageProbe.DamageFrame>();
        DamageProbe.OnFrame = frames.Add;
        try
        {
            using var session = HeadlessSession.Create(800, 600);
            session.Window.SetRoot(Flex.Column(gap: 0).JustifyContent(JustifyContent.SpaceBetween)
                .Children(
                    Box.New().Width(40).Height(40).Background(new Color(200, 0, 0, 255)),
                    Flex.Row(gap: 0).JustifyContent(JustifyContent.FlexEnd).Children(
                        Box.New().Width(40).Height(40).Background(new Color(0, 0, 200, 255)))));
            session.PumpFrame();

            var frame = Assert.Single(frames);
            Assert.True(frame.UnionFraction > 0.9f,
                $"corner-to-corner union was {frame.UnionFraction:P0}, expected ~all of the window");
            Assert.True(frame.LayoutDirty);
        }
        finally { DamageProbe.OnFrame = null; }
    }

    [Fact]
    public void OneDirtyElement_ReportsItsOwnBoundsNotTheWindow()
    {
        var frames = new List<DamageProbe.DamageFrame>();
        try
        {
            using var session = HeadlessSession.Create(800, 600);
            var small = Box.New().Width(100).Height(50).Background(new Color(200, 0, 0, 255));
            var root = Box.New().Padding(new Thickness(20)).Children(
                small,
                Text.New("bystander"));
            session.Window.SetRoot(root);
            session.PumpFrame();   // settle the initial full frame

            DamageProbe.OnFrame = frames.Add;
            small.MarkPaintDirty();
            session.PumpFrame();

            var frame = Assert.Single(frames);
            Assert.Equal(1, frame.RectCount);
            // 100x50 of an 800x600 window: ~1% — the whole argument for damage rects,
            // when frames like this dominate a workload.
            Assert.Equal(100f * 50f, frame.RectArea, tolerance: 1f);
            Assert.Equal(frame.RectArea, frame.UnionArea, tolerance: 1f);
            Assert.False(frame.LayoutDirty);
        }
        finally { DamageProbe.OnFrame = null; }
    }

    [Fact]
    public void TwoDistantDirtyElements_SplitRectsButOneWideUnion()
    {
        var frames = new List<DamageProbe.DamageFrame>();
        try
        {
            using var session = HeadlessSession.Create(800, 600);
            var first = Box.New().Width(40).Height(40).Background(new Color(200, 0, 0, 255));
            var last = Box.New().Width(40).Height(40).Background(new Color(0, 0, 200, 255));
            session.Window.SetRoot(Flex.Row(gap: 0).JustifyContent(JustifyContent.SpaceBetween)
                .Children(first, last));
            session.PumpFrame();

            DamageProbe.OnFrame = frames.Add;
            first.MarkPaintDirty();
            last.MarkPaintDirty();
            session.PumpFrame();

            var frame = Assert.Single(frames);
            Assert.Equal(2, frame.RectCount);
            Assert.Equal(2 * 40f * 40f, frame.RectArea, tolerance: 1f);
            // The elements sit at opposite window edges, so the single bounding rect
            // spans the width — the gap a multi-rect implementation would not repaint.
            Assert.True(frame.UnionArea >= 5 * frame.RectArea,
                $"union {frame.UnionArea} should dwarf the summed rects {frame.RectArea}");
        }
        finally { DamageProbe.OnFrame = null; }
    }

    [Fact]
    public void AnIdleApp_ProducesNoFrames()
    {
        var frames = new List<DamageProbe.DamageFrame>();
        try
        {
            using var session = HeadlessSession.Create();
            session.Window.SetRoot(Box.New().Children(Text.New("static")));
            session.PumpFrame();

            DamageProbe.OnFrame = frames.Add;
            session.PumpFrames(30);

            Assert.Empty(frames);
        }
        finally { DamageProbe.OnFrame = null; }
    }
}
