using HultUI.Core.Reactivity;
using HultUI.Testing;
using Xunit;

namespace HultUI.Core.Tests.Reactivity;

/// <summary>
/// The reactive scope — which Effect or Computed a read subscribes — belongs to the thread
/// running it (plan 17, reactivity-scope-threadstatic). It used to be an <c>AsyncLocal</c>,
/// which flowed into anything the callback started: a <c>Task.Run</c> inside it, the rest of
/// an <c>async</c> lambda after an <c>await</c>. A read there subscribed the effect from another
/// thread, a data race the UI-thread guard does not see (no Application, or the guard off).
/// And setting it built a new ExecutionContext on every run: 72 bytes an effect run, 160 with
/// another AsyncLocal live.
/// </summary>
[Collection("Reactive")]
public class ReactiveScopeThreadTests
{
    [Fact]
    public void AnEffectRun_AllocatesNothing()
    {
        var source = new Signal<int>(0);
        int seen = 0;
        using var effect = new Effect(() => seen = source.Value);
        // Another AsyncLocal live, as CultureInfo.CurrentCulture set anywhere makes one: with
        // the scope an AsyncLocal too, every set copied both into a new context.
        var other = new AsyncLocal<int> { Value = 1 };
        for (int i = 0; i < 100; i++) source.Value++;

        double perRun = Allocations.PerCall(1000, () => source.Value++);

        Assert.Equal(source.Peek(), seen);
        Assert.True(perRun == 0, $"a write that re-ran one effect allocated {perRun:0.0} B");
        GC.KeepAlive(other);
    }

    [Fact]
    public void ARead_OnAnotherThreadInsideAnEffect_DoesNotSubscribeIt()
    {
        var outside = new Signal<int>(0);
        int runs = 0;
        using var effect = new Effect(() =>
        {
            runs++;
            _ = Task.Run(() => outside.Value).Result;
        });

        outside.Value = 1;

        Assert.Equal(1, runs);
    }

    [Fact]
    public void ARead_AfterAnAwaitInsideAnEffect_DoesNotSubscribeIt()
    {
        var later = new Signal<int>(0);
        var gate = new TaskCompletionSource();
        Task? rest = null;
        int runs = 0;
        using var effect = new Effect(() =>
        {
            runs++;
            rest ??= ReadAfter(gate.Task, later);
        });

        gate.SetResult();
        rest!.Wait();
        later.Value = 1;

        Assert.Equal(1, runs);

        static async Task ReadAfter(Task gate, Signal<int> signal)
        {
            await gate.ConfigureAwait(false);
            _ = signal.Value;
        }
    }

    [Fact]
    public void Nesting_StillRestoresTheOuterScope()
    {
        var a = new Signal<int>(0);
        var b = new Signal<int>(0);
        int outerRuns = 0, innerRuns = 0;
        Effect? inner = null;
        using var outer = new Effect(() =>
        {
            outerRuns++;
            inner ??= new Effect(() => { innerRuns++; _ = b.Value; });
            _ = a.Value;   // read after the inner effect ran: must subscribe the outer one
        });

        a.Value = 1;
        b.Value = 1;

        Assert.Equal(2, outerRuns);
        Assert.Equal(2, innerRuns);
        inner!.Dispose();
    }
}
