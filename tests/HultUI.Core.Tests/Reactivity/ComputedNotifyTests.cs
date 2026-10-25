using HultUI.Core.Reactivity;
using HultUI.Testing;
using Xunit;

namespace HultUI.Core.Tests.Reactivity;

/// <summary>
/// A computed tells its subscribers through a rented snapshot, as a signal does: a write
/// upstream allocates nothing, and a subscriber can still come, go, or read the computed again
/// while the notify is walking the others. It copied its subscribers into a new list each time,
/// 64 bytes, twice for a write that changed its value (the dirty mark, then the new value).
/// </summary>
[Collection("Reactive")]
public class ComputedNotifyTests
{
    [Fact]
    public void AWriteUpstream_AllocatesNothing()
    {
        var source = new Signal<int>(0);
        var doubled = new Computed<int>(() => source.Value * 2);
        int seen = 0, next = 0;
        using var effect = new Effect(() => seen = doubled.Value);

        long bytes = Allocations.Least(() =>
        {
            for (int i = 0; i < 100; i++) source.Value = ++next;
        });

        Assert.Equal(0, bytes);
        Assert.Equal(2 * next, seen);
    }

    [Fact]
    public void AWriteUpstream_WithSeveralReaders_AllocatesNothing()
    {
        var source = new Signal<int>(0);
        var doubled = new Computed<int>(() => source.Value * 2);
        int a = 0, b = 0, c = 0, next = 0;
        using var first = new Effect(() => a = doubled.Value);
        using var second = new Effect(() => b = doubled.Value);
        using var third = new Effect(() => c = doubled.Value);

        long bytes = Allocations.Least(() =>
        {
            for (int i = 0; i < 100; i++) source.Value = ++next;
        });

        Assert.Equal(0, bytes);
        Assert.Equal((2 * next, 2 * next, 2 * next), (a, b, c));
    }

    [Fact]
    public void EveryReader_RunsOncePerWrite_ThoughTheFirstReadsTheComputedAgainMidNotify()
    {
        // The first effect runs at once (no batch), reads the computed, which recomputes and
        // notifies again from inside the walk that notified it: the inner notify has a buffer of
        // its own, and the second effect, told twice, runs once.
        var source = new Signal<int>(0);
        var doubled = new Computed<int>(() => source.Value * 2);
        int a = 0, b = 0, last = 0;
        using var first = new Effect(() => { _ = doubled.Value; a++; });
        using var second = new Effect(() => { last = doubled.Value; b++; });
        a = 0; b = 0;

        for (int i = 1; i <= 5; i++) source.Value = i;

        Assert.Equal(5, a);
        Assert.Equal(5, b);
        Assert.Equal(10, last);
    }

    [Fact]
    public void AReaderDisposedByAnotherMidNotify_DoesNotRun_AndTheWriteCompletes()
    {
        // Disposing unsubscribes from the computed while the notify walks its readers: the walk
        // goes on over its snapshot, and the disposed effect does not run.
        var source = new Signal<int>(0);
        var doubled = new Computed<int>(() => source.Value * 2);
        int a = 0, b = 0;
        Effect? second = null;
        using var first = new Effect(() => { _ = doubled.Value; a++; second?.Dispose(); });
        second = new Effect(() => { _ = doubled.Value; b++; });
        a = 0; b = 0;

        source.Value = 1;
        source.Value = 2;

        Assert.Equal(2, a);
        Assert.Equal(0, b);
        Assert.Equal(4, doubled.Value);
    }

    [Fact]
    public void AReaderMadeMidNotify_IsToldOfTheNextWrite_NotThisOne()
    {
        // A reader that subscribes while the notify walks the others is not in this write's
        // snapshot: it has just read the new value as it was made, and hears the next write.
        var source = new Signal<int>(0);
        var doubled = new Computed<int>(() => source.Value * 2);
        int lateRuns = 0, lateSeen = -1;
        Effect? late = null;
        using var maker = new Effect(() =>
        {
            if (doubled.Value == 2 && late is null)
                late = new Effect(() => { lateSeen = doubled.Value; lateRuns++; });
        });

        source.Value = 1;
        Assert.Equal(1, lateRuns);
        Assert.Equal(2, lateSeen);

        source.Value = 2;
        Assert.Equal(2, lateRuns);
        Assert.Equal(4, lateSeen);
        late!.Dispose();
    }

    [Fact]
    public void AReaderThatUnsubscribesItselfMidNotify_LeavesTheOthersTold()
    {
        // An effect that stops reading the computed when it runs unsubscribes from inside the
        // walk; the readers after it in the snapshot are still told.
        var source = new Signal<int>(0);
        var doubled = new Computed<int>(() => source.Value * 2);
        int quitterRuns = 0, others = 0;
        using var quitter = new Effect(() =>
        {
            quitterRuns++;
            if (quitterRuns == 1) _ = doubled.Value;   // reads it only on its first run
        });
        using var b = new Effect(() => { _ = doubled.Value; others++; });
        using var c = new Effect(() => { _ = doubled.Value; others++; });
        others = 0;

        source.Value = 1;
        source.Value = 2;

        Assert.Equal(2, quitterRuns);   // told of the first write, then no longer a reader
        Assert.Equal(4, others);
    }
}
