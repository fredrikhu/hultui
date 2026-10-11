using HultUI.Platform.SDL3;
using Xunit;
using static HultUI.Platform.SDL3.VulkanSwapchainChoice;

namespace HultUI.Platform.SDL3.Tests;

/// <summary>
/// Which kind of Win32 modal size/move loop a window is in. A drag runs inside DefWindowProc's
/// handling of the WM_SYSCOMMAND that starts it, and the loop's WM_ENTERSIZEMOVE and
/// WM_EXITSIZEMOVE are sent from inside that call, so the messages arrive nested like this.
/// </summary>
public class SizeMoveTrackerTests
{
    private const nuint ScSize = 0xF000, ScMove = 0xF010;
    private const nuint WmszRight = 2, HtCaption = 2;

    private static SizeMoveLoop Loop(SizeMoveTracker tracker, nuint command)
    {
        var before = tracker.SysCommand(command);
        var loop = tracker.Entered();
        Assert.Equal(loop, tracker.Loop);
        tracker.Exited();
        tracker.SysCommandHandled(before);
        Assert.Equal(SizeMoveLoop.None, tracker.Loop);
        return loop;
    }

    [Fact]
    public void ATitleBarDrag_IsAMove_AndAnEdgeDrag_IsASize()
    {
        var tracker = new SizeMoveTracker();
        // A mouse drag adds the hit-test code in the low four bits.
        Assert.Equal(SizeMoveLoop.Moving, Loop(tracker, ScMove | HtCaption));
        Assert.Equal(SizeMoveLoop.Sizing, Loop(tracker, ScSize | WmszRight));
        // From the keyboard (Alt+Space, Move / Size): the bare commands.
        Assert.Equal(SizeMoveLoop.Moving, Loop(tracker, ScMove));
        Assert.Equal(SizeMoveLoop.Sizing, Loop(tracker, ScSize));
    }

    [Fact]
    public void ALoopNoCommandStarted_CountsAsSizing()
    {
        var tracker = new SizeMoveTracker();
        Assert.Equal(SizeMoveLoop.Sizing, tracker.Entered());
        tracker.Exited();
        Assert.Equal(SizeMoveLoop.None, tracker.Loop);
    }

    [Fact]
    public void AMoveIsForgotten_OnceItsCommandIsHandled()
    {
        var tracker = new SizeMoveTracker();
        Assert.Equal(SizeMoveLoop.Moving, Loop(tracker, ScMove | HtCaption));
        // The next loop, with no command of its own, is not taken for a move.
        Assert.Equal(SizeMoveLoop.Sizing, tracker.Entered());
        tracker.Exited();
        // Nor after another command that starts no loop (SC_MAXIMIZE).
        var before = tracker.SysCommand(0xF030);
        tracker.SysCommandHandled(before);
        Assert.Equal(SizeMoveLoop.Sizing, tracker.Entered());
    }
}
