using HultUI;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Reactivity;
using HultUI.Core.Threading;
using Xunit;

namespace HultUI.Headless.Tests;

// Inside a HultUI.* namespace bare `Text` binds to the HultUI.Text namespace.
using Text = HultUI.Core.Builders.Text;

/// <summary>
/// The unit tests in HultUI.Core.Tests bind <see cref="UIThread"/> by hand. These cover
/// the wiring an app actually relies on: that constructing an Application claims the
/// thread, that disposing it lets go, and that the guard fires through a real component
/// on a real session rather than only against a bare Signal.
/// </summary>
public class UIThreadAffinityTests : IDisposable
{
    private readonly bool _wasEnforcing = UIThread.Enforce;

    public UIThreadAffinityTests() => UIThread.Enforce = true;

    public void Dispose() => UIThread.Enforce = _wasEnforcing;

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
    public void CreatingASession_BindsTheUIThread()
    {
        Assert.Null(UIThread.BoundThreadId);

        using var session = HeadlessSession.Create();

        Assert.Equal(Environment.CurrentManagedThreadId, UIThread.BoundThreadId);
    }

    [Fact]
    public void DisposingTheSession_UnbindsTheUIThread()
    {
        using (HeadlessSession.Create())
        {
            Assert.True(UIThread.BoundThreadId.HasValue);
        }

        // Without this, the next test in the assembly — which xunit may run on a
        // different pool thread — would fail on its own legitimate UI thread.
        Assert.Null(UIThread.BoundThreadId);
    }

    [Fact]
    public void ReactivePropertyWrite_FromABackgroundThread_Throws()
    {
        using var session = HeadlessSession.Create();
        var counter = new Counter();
        session.Window.SetRoot(new ComponentHost(counter));
        session.PumpFrame();

        var ex = RunOffThread(() => counter.Count = 5);

        var invalid = Assert.IsType<InvalidOperationException>(ex);
        Assert.Contains("InvokeOnMainThread", invalid.Message);
    }

    [Fact]
    public void ReactivePropertyWrite_MarshalledBack_Succeeds()
    {
        using var session = HeadlessSession.Create();
        var counter = new Counter();
        session.Window.SetRoot(new ComponentHost(counter));
        session.PumpFrame();

        var done = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            Application.InvokeOnMainThread(() => counter.Count = 5);
            done.Set();
        });
        thread.Start();
        Assert.True(done.Wait(TimeSpan.FromSeconds(10)));

        // The queued action runs on the run loop's next pass, which PumpFrame drives.
        session.PumpFrame();

        Assert.Equal(5, counter.Count);
        Assert.NotNull(session.Window.GetByText("Count: 5"));
    }

    /// <summary>
    /// Hand-written rather than <c>[Reactive]</c>: this assembly does not reference the
    /// source generator, and the generated property is this Signal either way.
    /// </summary>
    private sealed class Counter : Component
    {
        private readonly Signal<int> _count = new(0);

        public int Count
        {
            get => _count.Value;
            set => _count.Value = value;
        }

        public override Element Build() => Text.New($"Count: {Count}");
    }
}
