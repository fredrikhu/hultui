using System.IO;
using System.Reflection;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Headless;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// Landing on a file far down a long listing — what Go to path, "cd" and the find dialog's
/// jump all do — has to scroll the pane to it, and the arrow keys have to carry on from
/// there rather than snapping back to the top.
/// </summary>
public class CursorScrollTests
{
    private const int FileCount = 200;

    [Fact]
    public void LandingOnAFileFarDownTheListing_ScrollsToIt()
    {
        using var fx = new Fixture();
        fx.NavigateToFile("file-150.txt");

        Assert.Equal(fx.IndexOf("file-150.txt"), fx.Cursor);
        Assert.True(fx.ScrollOffsetY > 0,
            $"pane never scrolled: offset {fx.ScrollOffsetY} with the cursor on row {fx.Cursor}");
        Assert.True(fx.CursorIsVisible, "the cursor row is outside the viewport");
    }

    [Fact]
    public void ArrowUpAfterLanding_MovesOneRow_NotBackToTheTop()
    {
        using var fx = new Fixture();
        fx.NavigateToFile("file-150.txt");
        int landed = fx.Cursor;
        float landedOffset = fx.ScrollOffsetY;

        fx.Session.Window.KeyDown(KeyCode.Up);
        fx.Session.PumpFrames(2);

        Assert.Equal(landed - 1, fx.Cursor);
        Assert.True(fx.ScrollOffsetY > 0,
            $"arrow key scrolled back to the top (offset {fx.ScrollOffsetY}, was {landedOffset})");
        Assert.True(fx.CursorIsVisible, "the cursor row is outside the viewport");
    }

    /// <summary>Temp folder of 200 files + a hosted CommanderApp pinned at it.</summary>
    private sealed class Fixture : System.IDisposable
    {
        public readonly string Root;
        public readonly HeadlessSession Session;
        private readonly CommanderApp _app;

        public Fixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "vesper-scroll-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            for (int i = 0; i < FileCount; i++)
                File.WriteAllText(Path.Combine(Root, $"file-{i}.txt"), "x");

            Session = HeadlessSession.Create(1240, 820);
            _app = new CommanderApp(Session.Application, Session.Window.Window, Root);
            Session.Application.SetRoot(_app.Mount());
            Session.PumpFrame();
            Invoke("NavigateTo", 'L', Root, null, false);
            Session.PumpFrames(3);
        }

        /// <summary>What Go to path / the find dialog do: navigate to the folder asking to
        /// land the cursor on one named child.</summary>
        public void NavigateToFile(string name)
        {
            Invoke("NavigateTo", 'L', Root, name, true);
            Session.PumpFrames(4);
        }

        public int Cursor
            => (int)typeof(CommanderApp).GetMethod("Cursor", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(_app, new object[] { 'L' })!;

        public float RowHeight
            => (float)typeof(CommanderApp).GetProperty("RowHeight", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(_app)!;

        private VirtualListElement<FileEntry> VList
        {
            get
            {
                var refField = typeof(CommanderApp).GetField("_vlistL", BindingFlags.NonPublic | BindingFlags.Instance)!;
                var r = (HultUI.Core.Reactivity.Ref<VirtualListElement<FileEntry>>)refField.GetValue(_app)!;
                Assert.NotNull(r.Current);
                return r.Current!;
            }
        }

        public float ScrollOffsetY => VList.ScrollOffsetY;

        public bool CursorIsVisible
        {
            get
            {
                float top = Cursor * RowHeight, bottom = top + RowHeight;
                float view = VList.Layout.AbsoluteContentBox.Height;
                return top >= ScrollOffsetY - 0.5f && bottom <= ScrollOffsetY + view + 0.5f;
            }
        }

        public int IndexOf(string name)
        {
            var listing = (System.Collections.Generic.IReadOnlyList<FileEntry>)
                typeof(CommanderApp).GetMethod("Listing", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .Invoke(_app, new object[] { 'L' })!;
            for (int i = 0; i < listing.Count; i++)
                if (listing[i].Name == name) return i;
            return -1;
        }

        public void Invoke(string method, params object?[] args)
            => typeof(CommanderApp).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(_app, args);

        public void Dispose()
        {
            (_app as System.IDisposable)?.Dispose();
            Session.Dispose();
            try { Directory.Delete(Root, true); } catch { /* best-effort temp cleanup */ }
        }
    }
}
