using HultUI.Core.Builders;
using HultUI.Core.Platform;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// One application per process, enforced.
///
/// <para><see cref="Application.Current"/>, <c>InvokeOnMainThread</c> and the UI-thread
/// binding are static, so a second application takes them from the first. That used to
/// be a silent rebind whose symptom appeared far away — work posted with
/// <c>InvokeOnMainThread</c> going to a queue nothing drained — and a WinForms host
/// makes it easy to reach for by accident, since nothing stops a caller constructing an
/// application beside the one <c>HultUIApplicationHost</c> owns.</para>
/// </summary>
public class SingleApplicationTests
{
    [Fact]
    public void ASecondApplication_Throws_RatherThanTakingOver()
    {
        using var session = HeadlessSession.Create();
        var first = session.Application;

        var error = Assert.Throws<InvalidOperationException>(
            () => new Application(new StubPlatform()));

        Assert.Contains("already running", error.Message);

        // And the first one is untouched: the point is that it keeps working, not
        // merely that the second one failed.
        session.Window.SetRoot(Box.New());
        Assert.True(session.PumpFrame());
        Assert.Same(first, session.Application);
    }

    [Fact]
    public void DisposingReleasesTheClaim()
    {
        using (var first = HeadlessSession.Create())
            first.Window.SetRoot(Box.New());

        // Creating one, disposing it, and creating another is ordinary — a restart, or
        // one test after the next.
        using var second = HeadlessSession.Create();
        second.Window.SetRoot(Box.New());
        Assert.True(second.PumpFrame());
    }

    [Fact]
    public void AFailedConstruction_DoesNotPoisonTheProcess()
    {
        // The claim is taken before the first window is created, and only Dispose
        // releases it — but a constructor that throws leaves nothing to dispose. If it
        // did not release the claim itself, one failed start (no display, no GL context)
        // would make every later attempt report an application that does not exist.
        Assert.Throws<InvalidOperationException>(() => new Application(new FailingPlatform()));

        using var session = HeadlessSession.Create();
        session.Window.SetRoot(Box.New());
        Assert.True(session.PumpFrame());
    }
}

/// <summary>A platform whose CreateWindow is never reached — the constructor throws first.</summary>
internal sealed class StubPlatform : IWindowingPlatform
{
    public TimeProvider TimeProvider => TimeProvider.System;
    public IPlatformWindow CreateWindow(string title, int width, int height, WindowStyle style)
        => throw new InvalidOperationException("should not be reached");
    public bool PumpEvents(Action<IPlatformWindow>? onWindowClose) => true;
    public void WaitForEvents(int timeoutMs) { }
    public void Wake() { }
    public void Dispose() { }
}

/// <summary>A platform that cannot make a window — a display-less machine, in miniature.</summary>
internal sealed class FailingPlatform : IWindowingPlatform
{
    public TimeProvider TimeProvider => TimeProvider.System;
    public IPlatformWindow CreateWindow(string title, int width, int height, WindowStyle style)
        => throw new InvalidOperationException("no window for you");
    public bool PumpEvents(Action<IPlatformWindow>? onWindowClose) => true;
    public void WaitForEvents(int timeoutMs) { }
    public void Wake() { }
    public void Dispose() { }
}
