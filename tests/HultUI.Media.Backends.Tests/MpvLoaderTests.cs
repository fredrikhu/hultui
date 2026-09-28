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
}
