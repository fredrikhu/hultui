using System;
using System.IO;
using System.Text.Json;
using HultUI.Core.DevTools;
using Xunit;

namespace HultUI.Core.Tests.DevTools;

/// <summary>
/// Where a trace is written, which used to be the one thing about the tracer that could
/// take the application down with it. The F11 hotkey saved to the literal
/// <c>/tmp/hultui-trace.json</c>; on Windows a leading slash is drive-<i>relative</i>, so
/// that named a <c>tmp</c> folder at the root of whichever drive the process was running
/// from, and the second F11 press threw DirectoryNotFoundException out of the input
/// handler.
/// </summary>
public class FrameTracerPathTests
{
    [Fact]
    public void DefaultPath_IsFullyQualified()
    {
        // The assertion that fails for "/tmp/hultui-trace.json" on Windows and passes on
        // Unix — which is exactly why the bug only ever showed up on one of them.
        Assert.True(Path.IsPathFullyQualified(FrameTracer.DefaultPath),
            $"the default trace path is not rooted: {FrameTracer.DefaultPath}");
    }

    [Fact]
    public void DefaultPath_SitsInTheTempDirectory()
    {
        Assert.Equal("hultui-trace.json", Path.GetFileName(FrameTracer.DefaultPath));
        Assert.Equal(
            Path.GetFullPath(Path.GetTempPath()),
            Path.GetFullPath(Path.GetDirectoryName(FrameTracer.DefaultPath)!) + Path.DirectorySeparatorChar);
    }

    [Fact]
    public void StopRecording_CreatesTheDirectoryItWasPointedAt()
    {
        // HULTUI_TRACE can name somewhere that does not exist yet. Losing the trace at the
        // write, after the run it was recording is over, is the worst moment to fail.
        var root = Path.Combine(Path.GetTempPath(), "hultui-trace-test-" + Guid.NewGuid().ToString("N")[..8]);
        var path = Path.Combine(root, "nested", "deeper", "trace.json");
        try
        {
            var tracer = new FrameTracer();
            tracer.StartRecording();
            tracer.Annotate("Graphics", "test");
            tracer.StopRecording(path);

            Assert.True(File.Exists(path), "the trace was not written");
            // Chrome Trace Event Format: a JSON array. Parsing it is what says the file is
            // usable rather than merely present.
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
            Assert.False(tracer.IsRecording);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }
}
