using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Platform.Accessibility.Linux;
using Xunit;

namespace HultUI.Platform.Accessibility.Linux.Tests;

/// <summary>The tests that set <see cref="AccessibilityClients"/>, which is one for the process:
/// one at a time.</summary>
[CollectionDefinition(Name)]
public sealed class AccessibilityClientsCollection
{
    public const string Name = "AccessibilityClients";
}

/// <summary><c>org.a11y.Status</c> as a test switches it.</summary>
internal sealed class FakeStatus(bool on) : IAtSpiStatus
{
    private Action<bool>? _changed;
    public bool On { get; private set; } = on;

    public bool ReadStatus() => On;
    public void WatchStatus(Action<bool> changed) => _changed += changed;

    public void Set(bool on)
    {
        On = on;
        _changed?.Invoke(on);
    }
}

/// <summary>
/// The bridge is active only while <c>org.a11y.Status</c> says assistive technology is on: the
/// bus is reachable in every desktop session, and an active bridge rebuilt the tree and sent
/// AT-SPI every change with no reader running. <see cref="AccessibilityClients.Force"/>
/// (<c>HULTUI_ACCESSIBILITY</c>) overrides it, for a client that never sets the status.
/// </summary>
[Collection(AccessibilityClientsCollection.Name)]
public class AtSpiStatusTests
{
    private static string Id(Element element) => element.Id.Value.ToString();

    [Fact]
    public void TheBridge_IsActive_WhileTheStatusSaysAssistiveTechnologyIsOn()
    {
        var forced = AccessibilityClients.Force;
        AccessibilityClients.Force = null;
        try
        {
            var status = new FakeStatus(on: false);
            using var bridge = new LinuxAccessibilityBridge(new SignalRecorder(), status: status);
            Assert.False(bridge.IsActive);
            Assert.False(AccessibilityClients.Active);

            status.Set(true);
            Assert.True(bridge.IsActive);
            Assert.True(AccessibilityClients.Active);

            status.Set(false);
            Assert.False(bridge.IsActive);
            Assert.False(AccessibilityClients.Active);
        }
        finally
        {
            AccessibilityClients.Report(false);
            AccessibilityClients.Force = forced;
        }
    }

    [Fact]
    public void TheOverride_TurnsItOnOrOff_WhateverTheStatusSays()
    {
        var forced = AccessibilityClients.Force;
        try
        {
            var status = new FakeStatus(on: false);
            using var bridge = new LinuxAccessibilityBridge(new SignalRecorder(), status: status);
            AccessibilityClients.Force = true;
            Assert.True(bridge.IsActive);
            status.Set(true);
            AccessibilityClients.Force = false;
            Assert.False(bridge.IsActive);
            AccessibilityClients.Force = null;
            Assert.True(bridge.IsActive);

            // A test's bridge with no status is always on, so other tests can drive it.
            using var plain = new LinuxAccessibilityBridge(new SignalRecorder());
            AccessibilityClients.Force = false;
            Assert.True(plain.IsActive);
        }
        finally
        {
            AccessibilityClients.Report(false);
            AccessibilityClients.Force = forced;
        }
    }

    /// <summary>
    /// <c>org.a11y.Status</c> as the bus answers it: each read takes the value at its start, and the
    /// reads a test names stall until it lets them go, as a slow D-Bus round trip does.
    /// </summary>
    private sealed class SlowBus
    {
        private readonly object _gate = new();
        private readonly Dictionary<int, ManualResetEventSlim> _stalls = [];
        private int _reads;

        public volatile bool Value;
        public readonly List<bool> Told = [];

        /// <summary>Read <paramref name="number"/> (1-based) waits for <see cref="Release"/>.</summary>
        public ManualResetEventSlim Stall(int number) => _stalls[number] = new ManualResetEventSlim();

        /// <summary>Set when read n has begun.</summary>
        public readonly ManualResetEventSlim[] Began = [.. Enumerable.Range(0, 8).Select(_ => new ManualResetEventSlim())];

        public bool Read()
        {
            bool value = Value;
            int n = Interlocked.Increment(ref _reads);
            if (n < Began.Length) Began[n].Set();
            if (_stalls.TryGetValue(n, out var stall)) Assert.True(stall.Wait(TimeSpan.FromSeconds(10)), $"read {n} was never let go");
            return value;
        }

        public void Tell(bool on)
        {
            lock (_gate) Told.Add(on);
        }

        public int Reads => Volatile.Read(ref _reads);

        public bool[] ToldNow()
        {
            lock (_gate) return [.. Told];
        }
    }

    /// <summary>Waits until <paramref name="bus"/> has been told <paramref name="count"/> times and
    /// nothing more comes for a while.</summary>
    private static bool[] Settle(SlowBus bus, int count)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (bus.ToldNow().Length < count && DateTime.UtcNow < deadline) Thread.Sleep(5);
        Thread.Sleep(100);
        return bus.ToldNow();
    }

    [Fact]
    public void ABurstOfChanges_EndsOnTheStatusAsItIsNow_HoweverLongTheReadsTake()
    {
        // On, then off while the first read is still out. A read for each change, run side by
        // side, told off (the second read, quick) and then on (the first, slow): the bridge stayed
        // on with assistive technology off. One read at a time tells on and then reads again.
        var bus = new SlowBus { Value = true };
        var firstOut = bus.Stall(1);
        var reader = new AtSpiStatusReader(bus.Read, bus.Tell);

        reader.Changed();
        Assert.True(bus.Began[1].Wait(TimeSpan.FromSeconds(10)));
        bus.Value = false;
        reader.Changed();
        // Long enough for a read of its own to run and finish, if the change got one.
        if (bus.Began[2].Wait(TimeSpan.FromMilliseconds(300))) Settle(bus, 1);
        firstOut.Set();

        var told = Settle(bus, 2);
        Assert.Equal([true, false], told);
        Assert.Equal(2, bus.Reads);
    }

    [Fact]
    public void OnOffOn_FasterThanARead_LeavesItOn()
    {
        // The case of the survey: Orca restarts, or a settings panel flips the switch, quicker
        // than one round trip. Whatever the order the reads finish in, the last told is the last read,
        // and the last read begins after the last change.
        var bus = new SlowBus { Value = true };
        var firstOut = bus.Stall(1);
        var secondOut = bus.Stall(2);
        var reader = new AtSpiStatusReader(bus.Read, bus.Tell);

        reader.Changed();
        Assert.True(bus.Began[1].Wait(TimeSpan.FromSeconds(10)));
        bus.Value = false;
        reader.Changed();
        // A read of its own for the change to off would begin here, and stall.
        bus.Began[2].Wait(TimeSpan.FromMilliseconds(300));
        bus.Value = true;
        reader.Changed();
        // And one for the change to on, quick, told first.
        if (bus.Began[3].Wait(TimeSpan.FromMilliseconds(300))) Settle(bus, 1);
        firstOut.Set();
        Thread.Sleep(100);
        secondOut.Set();

        var told = Settle(bus, 2);
        Assert.True(told[^1], $"told {string.Join(", ", told)}: off while assistive technology is on");
        // The two changes during the first read are one more read, not two.
        Assert.Equal(2, bus.Reads);
    }

    [Fact]
    public void ChangesFarApart_AreReadOnceEach()
    {
        var bus = new SlowBus { Value = true };
        var reader = new AtSpiStatusReader(bus.Read, bus.Tell);
        reader.Changed();
        Settle(bus, 1);
        bus.Value = false;
        reader.Changed();
        Settle(bus, 2);
        bus.Value = true;
        reader.Changed();
        Assert.Equal([true, false, true], Settle(bus, 3));
        Assert.Equal(3, bus.Reads);
    }

    [Fact]
    public void ATreeFromNothing_AfterTheBridgeWasOff_RemovesWhatWentMeanwhile()
    {
        // The window drops its tree while the bridge is off and builds it afresh when it is on:
        // a diff from nothing. What the bridge published before and the new tree lacks went while
        // nobody was told.
        var a = TextElement.New("A");
        var b = TextElement.New("B");
        var root = BoxElement.New();
        root.AddChild(a);
        root.AddChild(b);
        var signals = new SignalRecorder();
        using var bridge = new LinuxAccessibilityBridge(signals);
        var first = AccessibilityTreeBuilder.Build(root);
        bridge.ApplyChanges(AccessibilityTreeDiffer.Diff(null, first), first);
        Assert.Equal(
        [
            "cache-add root parent=/org/a11y/atspi/null index=-1",
            $"cache-add {Id(a)} parent=root index=0",
            $"cache-add {Id(b)} parent=root index=1",
        ], signals.Lines);

        signals.Lines.Clear();
        root.RemoveChild(b);
        var again = AccessibilityTreeBuilder.Build(root);
        bridge.ApplyChanges(AccessibilityTreeDiffer.Diff(null, again), again);
        Assert.Equal(
        [
            $"cache-remove {Id(b)}",
            "cache-add root parent=/org/a11y/atspi/null index=-1",
            $"cache-add {Id(a)} parent=root index=0",
        ], signals.Lines);
    }
}
