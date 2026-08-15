using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Reactivity;
using HultUI.Core.Threading;
using Xunit;

namespace HultUI.Core.Tests.Threading;

/// <summary>
/// The guard is a global, so each test binds and the fixture unbinds afterwards —
/// otherwise a binding leaks into the next test, which xunit may well run on a
/// different pool thread. <see cref="UIThread.Enforce"/> is set explicitly rather than
/// relying on the Debug default, so the suite means the same thing under <c>-c Release</c>.
/// </summary>
[Collection("Reactive")]
public class UIThreadTests : IDisposable
{
    private readonly bool _wasEnforcing = UIThread.Enforce;

    public UIThreadTests() => UIThread.Enforce = true;

    public void Dispose()
    {
        UIThread.Unbind();
        UIThread.Enforce = _wasEnforcing;
    }

    /// <summary>Runs <paramref name="action"/> on a fresh thread and returns what it threw.</summary>
    private static Exception? RunOffThread(Action action)
    {
        Exception? caught = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { caught = ex; }
        });
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "off-thread work did not finish");
        return caught;
    }

    [Fact]
    public void Unbound_AllowsAnyThread()
    {
        UIThread.Unbind();
        var signal = new Signal<int>(0);

        // A library or unit test that never starts an Application has no UI thread,
        // so there is nothing to violate.
        Assert.Null(RunOffThread(() => signal.Value = 1));
        Assert.Equal(1, signal.Value);
    }

    [Fact]
    public void Bound_AllowsTheBindingThread()
    {
        UIThread.Bind();
        var signal = new Signal<int>(0);

        signal.Value = 1;

        Assert.Equal(1, signal.Value);
        Assert.True(UIThread.IsCurrent);
        Assert.Equal(Environment.CurrentManagedThreadId, UIThread.BoundThreadId);
    }

    [Fact]
    public void SignalWrite_FromOtherThread_Throws()
    {
        UIThread.Bind();
        var signal = new Signal<int>(0);

        var ex = RunOffThread(() => signal.Value = 1);

        var invalid = Assert.IsType<InvalidOperationException>(ex);
        Assert.Contains("Signal<Int32>.Value", invalid.Message);
        Assert.Contains("InvokeOnMainThread", invalid.Message);
        Assert.Equal(0, signal.Value);
    }

    [Fact]
    public void SignalWrite_OfAnUnchangedValue_FromOtherThread_StillThrows()
    {
        UIThread.Bind();
        var signal = new Signal<int>(7);

        // The write is a no-op by value but still a threading bug; a guard that only
        // fired when the value differed would be as intermittent as the race it replaces.
        Assert.IsType<InvalidOperationException>(RunOffThread(() => signal.Value = 7));
    }

    [Fact]
    public void ElementMutation_FromOtherThread_Throws()
    {
        UIThread.Bind();
        var box = Box.New();

        var layout = Assert.IsType<InvalidOperationException>(RunOffThread(box.MarkLayoutDirty));
        Assert.Contains("Element.MarkLayoutDirty", layout.Message);

        var paint = Assert.IsType<InvalidOperationException>(RunOffThread(box.MarkPaintDirty));
        Assert.Contains("Element.MarkPaintDirty", paint.Message);
    }

    [Fact]
    public void AddingAChild_FromOtherThread_Throws()
    {
        UIThread.Bind();
        var parent = Box.New();

        // Structural mutation routes through MarkChildrenDirty rather than the
        // public Mark*Dirty pair, so it needs its own coverage.
        Assert.IsType<InvalidOperationException>(
            RunOffThread(() => parent.AddChild(Box.New())));
    }

    [Fact]
    public void ComponentRebuild_FromOtherThread_Throws()
    {
        UIThread.Bind();
        var component = new Counter();
        var host = new ComponentHost(component);
        Assert.NotNull(host);

        // MarkDirty() reaches the tree without touching a signal, so the reactive
        // guard would not see it.
        Assert.IsType<InvalidOperationException>(RunOffThread(component.Rebuild));
    }

    [Fact]
    public void EnforceFalse_LetsTheViolationThrough()
    {
        UIThread.Bind();
        UIThread.Enforce = false;
        var signal = new Signal<int>(0);

        Assert.Null(RunOffThread(() => signal.Value = 1));
    }

    [Fact]
    public void Unbind_ReturnsTheGuardToInert()
    {
        UIThread.Bind();
        UIThread.Unbind();
        var signal = new Signal<int>(0);

        Assert.Null(UIThread.BoundThreadId);
        Assert.Null(RunOffThread(() => signal.Value = 1));
    }

    [Fact]
    public void Message_NamesBothThreads()
    {
        UIThread.Bind();
        int uiThreadId = Environment.CurrentManagedThreadId;
        var signal = new Signal<int>(0);
        int offThreadId = 0;

        var ex = RunOffThread(() =>
        {
            offThreadId = Environment.CurrentManagedThreadId;
            signal.Value = 1;
        });

        Assert.NotNull(ex);
        Assert.Contains(uiThreadId.ToString(), ex.Message);
        Assert.Contains(offThreadId.ToString(), ex.Message);
    }

    private sealed class Counter : Component
    {
        public void Rebuild() => MarkDirty();
        public override Element Build() => Box.New();
    }
}
