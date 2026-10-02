using HultUI.Media.Mpv;
using Xunit;

namespace HultUI.Media.Backends.Tests;

/// <summary>
/// The loader's search, without libmpv: a library file that exists and will not load must
/// say why, so that a missing dependency is not reported as a missing library.
/// </summary>
public class MpvLoaderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("hultui-mpv-loader").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void AFileThatWillNotLoad_IsReportedWithTheLoadersReason()
    {
        // Not a library at all: the loader refuses it the way it refuses one whose
        // dependencies are missing — found, and not loadable.
        var file = Path.Combine(_dir, "libmpv-test.bin");
        File.WriteAllBytes(file, [0x4D, 0x5A, 0x00, 0x01, 0x02, 0x03]);

        bool loaded = Mpv.Mpv.TryLoadFrom([], [_dir], ["libmpv-test.bin"], out var handle, out var error);

        Assert.False(loaded);
        Assert.Equal(0, handle);
        Assert.NotNull(error);
        Assert.Contains(file, error);
    }

    [Fact]
    public void NoFileAnywhere_IsNotFound_WithNoReasonToGive()
    {
        bool loaded = Mpv.Mpv.TryLoadFrom([], [_dir], ["libmpv-absent.bin"], out _, out var error);

        Assert.False(loaded);
        Assert.Null(error);
    }

    [Fact]
    public void UnderADistributionsOwnRid_ThePortableRidsFolderIsSearchedToo()
    {
        // Fedora's own .NET reports fedora.44-x64; the natives package's folder is linux-x64.
        // Built with the platform's separator, as the resolver builds them: this runs on Windows too.
        static string Native(string rid) => Path.Combine("/app", "runtimes", rid, "native");
        var dirs = Mpv.Mpv.CandidateDirectories(null, "/app", "fedora.44-x64", "linux-x64");
        Assert.Equal(["/app", Native("fedora.44-x64"), Native("linux-x64"), null], dirs);

        var portable = Mpv.Mpv.CandidateDirectories("/hint", "/app", "linux-x64", "linux-x64");
        Assert.Equal(["/hint", "/app", Native("linux-x64"), null], portable);
    }

    [Fact]
    public void OnThisMachine_ALibraryInThePortableRidsFolder_IsFound()
    {
        // Only meaningful where the runtime reports a RID of its own, as Fedora's does.
        var rid = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier;
        var portableRid = Mpv.Mpv.PortableRid();
        if (portableRid is null || portableRid == rid || !OperatingSystem.IsLinux()) return;

        var dir = Path.Combine(AppContext.BaseDirectory, "runtimes", portableRid, "native");
        var file = Path.Combine(dir, "libmpv.so.2");
        bool made = !File.Exists(file);
        if (!made) return;   // a real one is there: nothing to prove with a fake
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(file, [0x7F, (byte)'E', (byte)'L', (byte)'F', 0, 0]);
        try
        {
            // Not loadable, so the loader names it: it was looked at.
            Mpv.Mpv.TryLoad(out var handle, out var reason);
            Assert.True(handle != 0 || (reason?.Contains(file) ?? false), $"not looked at: {reason}");
        }
        finally { File.Delete(file); }
    }
}
