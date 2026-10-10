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
