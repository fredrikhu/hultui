using System.Runtime.CompilerServices;
using Xunit;

namespace HultUI.Platform.SDL3.Tests;

/// <summary>
/// <see cref="SdlVulkanHost.TryEnableInterop"/> is asked on every draw of a GPU source on a Vulkan
/// window (the surface's <c>SupportsExternalGL</c>), and once it has decided it answers from cache.
/// The answer from cache made the closure of the GPU check's uuid lambda on the way in: 24 bytes a
/// frame with a web view or a video on screen, on 8-bit and F16, JIT and NativeAOT (plan 17 round 9,
/// vk-interop-check-allocates).
/// </summary>
public class VulkanInteropCheckTests
{
    [Fact]
    public void TheInteropCheck_AnsweredFromCache_AllocatesNothing()
    {
        // No GPU: a host never set up has no device, so its first check refuses and keeps that.
        var host = (SdlVulkanHost)RuntimeHelpers.GetUninitializedObject(typeof(SdlVulkanHost));
        Assert.False(host.TryEnableInterop());
        Assert.False(host.TryEnableInterop());

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) host.TryEnableInterop();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated == 0, $"{allocated} bytes for 1,000 answers from cache");
    }
}
