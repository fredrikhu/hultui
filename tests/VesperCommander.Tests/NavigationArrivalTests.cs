using System.IO;
using System.Reflection;
using System.Threading;
using HultUI.Core.Events;
using HultUI.Headless;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// A navigation moves the pane when the next folder's rows land: the folder, the cursor, the
/// marks, the history and the pane's tab strip, trail, header and footer, in one build and one
/// frame. Until then the pane shows, and the keys act on, the folder it is leaving. It used to move
/// the pane at once and again when the rows landed, and a frame between the two showed the new
/// folder's name over the old folder's rows (plan 17 round 14: in a real window 14 of 20
/// navigations drew both). A folder that is slow to answer, or may not answer, still moves the pane
/// at once, and the connecting block follows at the delay; a local folder still out at the delay
/// moves the pane then, with the block.
///
/// <para>The loads run on the thread pool here, as in a real window, so a frame can fall while a
/// listing is out (a headless session's own queue lands every listing before the next frame), and
/// each listing waits at a gate until the test lets it go.</para>
/// </summary>
public class NavigationArrivalTests
{
    private sealed class Gated : System.IDisposable
    {
        public readonly DiffViewTests.Fixture T = new();
        private readonly ManualResetEventSlim _gate = new(true);
        public string Sub => Path.Combine(T.Left, "sub");

        public Gated()
        {
            Directory.CreateDirectory(Sub);
            for (int i = 0; i < 6; i++)
            {
                T.WriteLeft($"f{i}.txt", "x\n");
                File.WriteAllText(Path.Combine(Sub, $"s{i}.txt"), "y\n");
            }
            T.WriteRight("r.txt", "z\n");
            T.Open();
            T.App.FreeLabelSource = _ => "1 GB free";
            // Every listing waits at the gate (null: then the disk is read as always).
            FileSystem.EnumerateOverride = p =>
            {
                if (!p.StartsWith(@"\\", System.StringComparison.Ordinal)) { _gate.Wait(); return null!; }
                _gate.Wait();
                return [];
            };
            typeof(HultUI.AsyncLoader<int>).Assembly.GetType("HultUI.AsyncWork")!
                .GetMethod("SetScheduler", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [null]);
            // A key first, so the keyboard's focus ring is already showing.
            T.Session.Window.PressKey(KeyCode.F13);
            Frames(6);
        }

        public void Hold() => _gate.Reset();
        public void Release() => _gate.Set();

        public int Frames(int pumps)
        {
            int drawn = 0;
            for (int i = 0; i < pumps; i++)
            {
                if (T.Session.PumpFrame(System.TimeSpan.FromMilliseconds(16))) drawn++;
                Thread.Sleep(2);
            }
            return drawn;
        }

        public string? Shown(char side) => T.Field<string?[]>("_shownDir")[side == 'L' ? 0 : 1];
        public int Builds => T.Field<int>("_builds");
        public PaneState State(char side) => (PaneState)T.Invoke("PaneStateOf", side)!;

        /// <summary>Frames drawn until the pane shows <paramref name="dir"/>'s rows, and six more.</summary>
        public int FramesUntilShown(string dir)
        {
            int drawn = 0;
            var until = System.DateTime.UtcNow + System.TimeSpan.FromSeconds(10);
            while (!string.Equals(Shown('L'), dir, System.StringComparison.OrdinalIgnoreCase) && System.DateTime.UtcNow < until)
                drawn += Frames(1);
            Assert.Equal(dir, Shown('L'), ignoreCase: true);
            return drawn + Frames(6);
        }

        public void Dispose()
        {
            _gate.Set();
            FileSystem.EnumerateOverride = null;
            T.Dispose();
        }
    }

    [Fact]
    public void ANavigation_DrawsOneFrame_WithTheNextFoldersRows_AndBuildsTheWindowOnce()
    {
        using var g = new Gated();
        var t = g.T;
        t.PutCursorOn('L', "sub");
        g.Frames(6);
        // Any other key still draws the window, as an ordinary handler's does: the root's handler is
        // passive and asks for that frame itself.
        t.Session.Window.PressKey(KeyCode.F13);
        Assert.Equal(1, g.Frames(6));
        var steps = new (string Name, KeyCode Key, KeyModifiers Mods, string From, string To)[]
        {
            ("Enter", KeyCode.Enter, KeyModifiers.None, t.Left, g.Sub),
            ("Backspace", KeyCode.Backspace, KeyModifiers.None, g.Sub, t.Left),
            ("Alt+Left", KeyCode.Left, KeyModifiers.Alt, t.Left, g.Sub),
            ("Alt+Right", KeyCode.Right, KeyModifiers.Alt, g.Sub, t.Left),
        };
        foreach (var (name, key, mods, from, to) in steps)
        {
            int builds = g.Builds, parts = t.App.PartBuilds;
            g.Hold();
            t.Session.Window.PressKey(key, mods);
            // The rows are out: nothing built, nothing drawn, the pane on the folder it is leaving.
            Assert.Equal(0, g.Frames(6));
            Assert.Equal(builds, g.Builds);
            Assert.Equal(parts, t.App.PartBuilds);
            Assert.Equal(from, t.Dir('L'), ignoreCase: true);
            Assert.Equal(PaneState.Ok, g.State('L'));

            g.Release();
            Assert.True(g.FramesUntilShown(to) == 1, $"{name} did not draw one frame");
            Assert.Equal(to, t.Dir('L'), ignoreCase: true);
            Assert.Equal(builds + 1, g.Builds);
            // Backspace lands on the folder it came out of.
            if (key == KeyCode.Backspace) Assert.Equal("sub", t.Rows('L')[t.Cursor('L')].Name);
            CursorNoBuildTests.AssertIsAFullBuild(t, "after " + name);
        }
    }

    /// <summary>A local folder still out at the connecting delay moves the pane then, and says it
    /// is connecting, as a slow folder always has; a share moves the pane at once, as it did, and
    /// says so at the delay.</summary>
    [Fact]
    public void ASlowFolder_StillShowsWhatItIsWaitingFor()
    {
        using var g = new Gated();
        var t = g.T;
        t.PutCursorOn('L', "sub");
        g.Frames(6);

        g.Hold();
        t.Session.Window.PressKey(KeyCode.Enter);
        g.Frames(3);
        Assert.Equal(t.Left, t.Dir('L'), ignoreCase: true);
        t.Session.AdvanceTime(System.TimeSpan.FromMilliseconds(400));
        Assert.Equal(PaneState.Connecting, g.State('L'));
        // The delay's nudge moves the pane, the block with it.
        var until = System.DateTime.UtcNow + System.TimeSpan.FromSeconds(5);
        while (!string.Equals(t.Dir('L'), g.Sub, System.StringComparison.OrdinalIgnoreCase) && System.DateTime.UtcNow < until) g.Frames(1);
        Assert.Equal(g.Sub, t.Dir('L'), ignoreCase: true);
        g.Frames(2);
        Assert.Contains("Connecting to", t.Session.Window.DumpAccessibilityTree());
        g.Release();
        g.FramesUntilShown(g.Sub);
        Assert.Equal(PaneState.Ok, g.State('L'));

        // A share: the pane is there at once, before its rows.
        g.Hold();
        string share = @"\\vesper-test\share";
        t.Invoke("NavigateTo", 'L', share, null, true);
        Assert.Equal(share, t.Dir('L'));
        t.Session.AdvanceTime(System.TimeSpan.FromMilliseconds(400));
        Assert.Equal(PaneState.Connecting, g.State('L'));
        g.Release();
        g.FramesUntilShown(share);
    }
}
