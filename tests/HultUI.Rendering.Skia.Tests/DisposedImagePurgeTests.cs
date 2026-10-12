using System.Diagnostics;
using SkiaSharp;
using Xunit;

namespace HultUI.Rendering.Skia.Tests;

/// <summary>
/// When the Vulkan surface frees the textures of disposed images. A GPU context keeps an
/// uploaded texture after its image is gone, as a spare for a later texture of that size: eight
/// 3840×2400 photos drawn and disposed left 176 MB of spares, which only the context's 256 MB
/// budget would ever push out. The real-window half is
/// <c>VulkanProbeTests.DisposedImages_LeaveNoTexturesBehind</c>.
/// </summary>
public class DisposedImagePurgeTests
{
    private static readonly long Second = Stopwatch.Frequency;

    [Fact]
    public void NothingDisposed_NothingIsDue()
    {
        var purge = new DisposedImagePurge(disposals: 7);
        Assert.False(purge.HasNew(7));
        Assert.False(purge.IsDue(7, now: 100 * Second));
    }

    [Fact]
    public void WhatWasDisposedBeforeTheSurfaceExisted_IsNotItsBusiness()
    {
        // The count is process-wide; a surface starts from where it stood when it was made.
        var purge = new DisposedImagePurge(disposals: 40);
        Assert.False(purge.IsDue(40, now: 0));
        Assert.True(purge.IsDue(41, now: 0));
    }

    [Fact]
    public void TheFirstDisposal_IsDueAfterTheNextFrame()
    {
        var purge = new DisposedImagePurge(0);
        Assert.True(purge.HasNew(1));
        Assert.True(purge.IsDue(1, now: 5 * Second));
    }

    [Fact]
    public void AfterAPurge_TheNextWaitsASecond_AndThenComes()
    {
        var purge = new DisposedImagePurge(0);
        purge.Purged(3, now: 10 * Second);
        Assert.False(purge.HasNew(3));

        // A poster grid evicting as it scrolls: a disposal on most frames.
        Assert.True(purge.HasNew(4));
        Assert.False(purge.IsDue(4, now: 10 * Second + Second / 2));
        Assert.False(purge.IsDue(9, now: 11 * Second - 1));
        // Not dropped: due on the first frame after the second is up.
        Assert.True(purge.IsDue(9, now: 11 * Second));
        purge.Purged(9, now: 11 * Second);
        Assert.False(purge.IsDue(9, now: 30 * Second));
    }
}

/// <summary>
/// The count the purge reads: a source disposed after it was drawn. One never drawn uploaded
/// nothing and is not counted. Alone in the process, because the count is process-wide and other
/// tests dispose drawn sources.
/// </summary>
[CollectionDefinition(nameof(ImageDisposalCount), DisableParallelization = true)]
public sealed class ImageDisposalCount;

[Collection(nameof(ImageDisposalCount))]
public class SkiaImageSourceDisposalCountTests
{
    [Fact]
    public void ADrawnSource_IsCountedOnce_HoweverOftenItIsDisposed()
    {
        var source = new SkiaImageSource(new SKBitmap(8, 8));
        Assert.NotNull(source.Image);   // what a draw does first
        int before = SkiaImageSource.DrawnDisposals;

        source.Dispose();
        source.Dispose();

        Assert.Equal(before + 1, SkiaImageSource.DrawnDisposals);
    }

    [Fact]
    public void ASourceNeverDrawn_IsNotCounted()
    {
        int before = SkiaImageSource.DrawnDisposals;

        new SkiaImageSource(new SKBitmap(8, 8)).Dispose();

        Assert.Equal(before, SkiaImageSource.DrawnDisposals);
    }
}
