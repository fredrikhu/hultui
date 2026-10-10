using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
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
}
