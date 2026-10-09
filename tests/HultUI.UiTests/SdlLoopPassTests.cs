using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Platform.SDL3;
using Xunit;
using Xunit.Abstractions;

namespace HultUI.UiTests;

/// <summary>
/// A pass of the application loop in a real SDL window. The headless suite's
/// FrameAllocationTests drive the same loop on the headless platform, whose pump and wait do
/// nothing, so neither what the SDL pump allocates nor how long the SDL wait parks shows up
/// there (plan 17, runtime-sdl-pump-closure).
/// </summary>
[Collection("ui")] // one Application per process, and SDL is process-global
public sealed class SdlLoopPassTests(ITestOutputHelper output)
{
    private static readonly TimeSpan Frame = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60);

    [UiFact]
    public void AnIdlePass_AllocatesNothing()
    {
        var platform = new SdlWindowingPlatform();
        using var app = new Application(platform, "idle pass", 300, 200);
        app.SetRoot(Flex.Column().Padding(8).Children(TextElement.New("Nothing changes here"), Button.New("Button")));
        for (int i = 0; i < 4; i++) app.RunFrame(Frame, forceRender: true);
        for (int i = 0; i < 200; i++) { _ = app.IsRunning; app.RunFrame(Frame); }

        // Counted per pass rather than in total: an event the OS sends the window meanwhile (the
        // pointer crossing it) is handled in the pass and may allocate, which is not what this
        // is about. A pass that allocates for itself allocates in every pass.
        const int passes = 500;
        int allocatingPasses = 0;
        long allocated = 0;
        bool running = true;
        for (int i = 0; i < passes; i++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            running &= app.IsRunning;
            app.RunFrame(Frame);
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            if (bytes > 0) { allocatingPasses++; allocated += bytes; }
        }

        output.WriteLine($"{allocatingPasses} of {passes} idle passes allocated, {allocated} B in all");
        Assert.True(running);
        Assert.True(allocatingPasses <= passes / 10,
            $"{allocatingPasses} of {passes} idle passes allocated ({allocated} B in all)");
    }
}
