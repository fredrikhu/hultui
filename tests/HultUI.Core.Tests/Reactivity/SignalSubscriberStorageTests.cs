using HultUI.Core.Reactivity;
using Xunit;

namespace HultUI.Core.Tests.Reactivity;

/// <summary>
/// Notification snapshots a rented buffer rather than allocating a List per write, so a
/// subscriber can come and go while a write is walking the set. These cover the shapes that
/// makes possible, and stand as the regression net for the 0/1/many storage attempt that was
/// reverted — see the note in Signal.cs — so a second attempt has them to answer to.
/// </summary>
[Collection("Reactive")]
public class SignalSubscriberStorageTests
{
    [Fact]
    public void OneSubscriber_IsNotified()
    {
        var signal = new Signal<int>(0);
        int runs = 0;
        using var effect = new Effect(() => { _ = signal.Value; runs++; });

        signal.Value = 1;

        Assert.Equal(2, runs);   // initial run, then the write
    }

    [Fact]
    public void ASecondSubscriber_DoesNotDisplaceTheFirst()
    {
        var signal = new Signal<int>(0);
        int a = 0, b = 0;
        using var first = new Effect(() => { _ = signal.Value; a++; });
        using var second = new Effect(() => { _ = signal.Value; b++; });

        signal.Value = 1;

        Assert.Equal(2, a);
        Assert.Equal(2, b);
    }

    [Fact]
    public void ManySubscribers_AreAllNotified()
    {
        var signal = new Signal<int>(0);
        int runs = 0;
        var effects = new List<Effect>();
        for (int i = 0; i < 20; i++)
            effects.Add(new Effect(() => { _ = signal.Value; runs++; }));

        runs = 0;
        signal.Value = 1;

        Assert.Equal(20, runs);
        foreach (var e in effects) e.Dispose();
    }

    [Fact]
    public void DisposingTheOnlySubscriber_StopsNotifications()
    {
        var signal = new Signal<int>(0);
        int runs = 0;
        var effect = new Effect(() => { _ = signal.Value; runs++; });
        effect.Dispose();

        runs = 0;
        signal.Value = 1;

        Assert.Equal(0, runs);
    }

    [Fact]
    public void DisposingOneOfTwo_LeavesTheOther()
    {
        var signal = new Signal<int>(0);
        int a = 0, b = 0;
        var first = new Effect(() => { _ = signal.Value; a++; });
        using var second = new Effect(() => { _ = signal.Value; b++; });
        first.Dispose();

        a = 0; b = 0;
        signal.Value = 1;

        Assert.Equal(0, a);
        Assert.Equal(1, b);
    }

    [Fact]
    public void AnEffectDisposedByAnother_DoesNotRun_AndTheWriteCompletes()
    {
        // Why the many-path still copies before iterating: an effect can dispose itself or
        // a sibling from inside its own run, which mutates the subscriber set while the
        // write is walking it. Iterating the set directly would throw here.
        //
        // The disposed effect does not run, and that is the scheduler's doing rather than
        // this storage's: NotifyDirty only schedules, and the first effect's flush disposes
        // the second before the queue reaches it. Unchanged by the 0/1/many rework —
        // recorded so a future change to either half has to notice it moved.
        var signal = new Signal<int>(0);
        int a = 0, b = 0;
        Effect? second = null;

        using var first = new Effect(() => { _ = signal.Value; a++; second?.Dispose(); });
        second = new Effect(() => { _ = signal.Value; b++; });

        a = 0; b = 0;
        signal.Value = 1;

        Assert.Equal(1, a);
        Assert.Equal(0, b);
        Assert.Equal(1, signal.Value);
    }

    [Fact]
    public void ASignalNobodyReads_NotifiesNothing()
    {
        var signal = new Signal<int>(0);
        signal.Value = 1;
        Assert.Equal(1, signal.Value);
    }

    [Fact]
    public void AResubscribeAfterEveryoneLeft_StillNotifies()
    {
        // The set is kept rather than dropped when it empties, so re-subscribing has to
        // keep working through the same instance.
        var signal = new Signal<int>(0);
        var a = new Effect(() => _ = signal.Value);
        var b = new Effect(() => _ = signal.Value);
        a.Dispose();
        b.Dispose();

        int runs = 0;
        using var revived = new Effect(() => { _ = signal.Value; runs++; });

        runs = 0;
        signal.Value = 1;

        Assert.Equal(1, runs);
    }
}
