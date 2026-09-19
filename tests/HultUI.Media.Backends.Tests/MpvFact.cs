using HultUI.Media.Mpv;
using Xunit;

// A HeadlessSession owns process-global state, so nothing here runs in parallel.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace HultUI.Media.Backends.Tests;

/// <summary>A fact that skips, visibly, when libmpv cannot be loaded on this machine.</summary>
public sealed class MpvFactAttribute : FactAttribute
{
    public MpvFactAttribute()
    {
        if (!MpvBackend.IsAvailable)
            Skip = "libmpv not found: put libmpv-2.dll (or the platform's name) beside the tests, on the "
                 + "library path, or point HULTUI_MPV_PATH at it.";
    }
}
