using System.Diagnostics;
using System.Runtime.InteropServices;
using Vortice.Vulkan;

namespace HdrInterop;

/// <summary>
/// Does one cycle of export, import and release leak an OS handle?
///
/// <para>The question phase 4 could not answer either way. The shared image is sized to the
/// <i>video</i>, not the window, so a player that opens one file exports exactly once — the
/// Linux run watched 159 swapchain recreations with the descriptor count flat and correctly
/// reported that as proving nothing, because <c>glImportMemoryFdEXT</c> had run a single
/// time. A resolution change is what re-exports, and those are rare in ordinary use and slow
/// to provoke through an app.</para>
///
/// <para>So the cycle is run here directly, a thousand times, against one device and one
/// image: export a handle for the same allocation, import it, release the GL objects. It is
/// not the renderer's code, but it is the renderer's sequence — including the part that is
/// asymmetric between platforms. <b>Windows does not take ownership of an
/// <c>OPAQUE_WIN32</c> handle, so the caller must close it; Linux hands the file descriptor
/// over and <c>glImportMemoryFdEXT</c> consumes it.</b> Getting that backwards leaks on one
/// platform and double-frees on the other, and neither shows up on the first import.</para>
///
/// <para><b>There is a positive control, and it is the point.</b> A flat handle count means
/// nothing unless the same measurement could have shown a rise, so the run ends with a batch
/// that deliberately omits the close on Windows. If that batch does not climb, the
/// measurement is blind and the clean result before it says nothing at all.</para>
/// </summary>
internal static unsafe class ChurnBench
{
    private const int Warmup = 50;
    private const int Cycles = 1000;
    private const int ControlCycles = 200;

    public static int Run()
    {
        Console.WriteLine("── does one export/import cycle leak a handle? ──");
        Console.WriteLine();

        if (!SyncBench.MakeGlContext(out string renderer)) return 1;
        Console.WriteLine($"  GL renderer: {renderer}");

        using var vk = VulkanImage.Create(320, 180, VkFormat.R16G16B16A16Sfloat);
        Console.WriteLine($"  image: 320x180 RGBA16F, {vk.AllocationSize} bytes");
        Console.WriteLine($"  five exports, one allocation:  {SampleHandles(vk, 5, fresh: false)}");
        Console.WriteLine($"  five exports, fresh allocations: {SampleHandles(vk, 5, fresh: true)}");
        Console.WriteLine();

        for (int i = 0; i < Warmup; i++) Cycle(vk, release: true);

        long before = Handles();
        var clock = Stopwatch.StartNew();
        for (int i = 0; i < Cycles; i++) Cycle(vk, release: true);
        clock.Stop();
        long after = Handles();

        Console.WriteLine($"  {Cycles} cycles, released properly");
        Console.WriteLine($"    handles {before} -> {after}   (delta {after - before:+#;-#;0})");
        Console.WriteLine($"    {clock.Elapsed.TotalMilliseconds / Cycles:0.000} ms a cycle");
        Console.WriteLine();

        // The control. Without it a flat line above is indistinguishable from a measurement
        // that cannot see anything — which is the failure this whole exercise keeps meeting.
        long controlBefore = Handles();
        for (int i = 0; i < ControlCycles; i++) Cycle(vk, release: false);
        long controlAfter = Handles();
        long controlDelta = controlAfter - controlBefore;

        Console.WriteLine($"  {ControlCycles} exports deliberately dropped (the control: " +
            (OperatingSystem.IsWindows() ? "handle never closed)" : "fd never imported)"));
        Console.WriteLine($"    handles {controlBefore} -> {controlAfter}   (delta {controlDelta:+#;-#;0})");
        Console.WriteLine();

        bool controlWorked = controlDelta > ControlCycles / 2;
        bool clean = Math.Abs(after - before) < Cycles / 100;

        Console.WriteLine(controlWorked
            ? "  The control climbed, so this measurement can see a leak."
            : "  THE CONTROL DID NOT CLIMB — the measurement is blind and the result above");
        if (!controlWorked) Console.WriteLine("  says nothing. Do not read it as a pass.");

        if (controlWorked)
            Console.WriteLine(clean
                ? $"  VERDICT: {Cycles} released cycles cost no handles. The cycle does not leak."
                : $"  VERDICT: released cycles leaked {after - before} handles over {Cycles}.");

        return controlWorked && clean ? 0 : 1;
    }

    /// <summary>
    /// One export, import and release of the same allocation.
    /// </summary>
    /// <param name="release">False runs the control instead: it exports a handle and drops
    /// it on the floor, which is a leak on either platform by that platform's own rule. If
    /// that does not raise the count, nothing this test reports can be believed.</param>
    private static void Cycle(VulkanImage vk, bool release)
    {
        // A fresh allocation each time, because that is what the renderer does on a size
        // change and because exporting one allocation twice gives the same handle back.
        vk.RecreateImage();

        // The control leaks the one thing the platform says belongs to the caller, and
        // nothing else, so a rise names that step rather than the cycle in general.
        if (!release)
        {
            if (OperatingSystem.IsWindows())
            {
                // The handle is ours to close: not closing it is the leak.
                vk.ExportWin32Handle();
            }
            else
            {
                // The fd is consumed by the import, so there is nothing to withhold from it.
                // Exporting one and never importing it leaks the same descriptor by the same
                // rule, and is the control this platform can actually run.
                vk.ExportFd();
            }
            return;
        }

        uint memory = Gl.CreateMemoryObject();
        if (OperatingSystem.IsWindows())
        {
            nint handle = vk.ExportWin32Handle();
            Gl.ImportWin32(memory, vk.AllocationSize, handle);
            // GL copies what it needs out of an OPAQUE_WIN32 handle rather than taking
            // ownership, so this side closes it. glImportMemoryFdEXT is the other way round
            // and consumes the descriptor, which is why there is no close below.
            CloseHandle(handle);
        }
        else
        {
            Gl.ImportFd(memory, vk.AllocationSize, vk.ExportFd());
        }

        uint texture = Gl.CreateTexture();
        Gl.SetOptimalTiling(texture);
        Gl.TextureStorageMem(texture, vk.Width, vk.Height, memory, 0);

        uint fbo = Gl.CreateFramebuffer();
        Gl.AttachColour(fbo, texture);

        Gl.DeleteFramebuffer(fbo);
        Gl.DeleteTexture(texture);
        Gl.DeleteMemoryObject(memory);
    }

    /// <summary>
    /// Kernel handles held by this process, asked of the OS rather than of .NET.
    ///
    /// <para><c>Process.HandleCount</c> goes through a performance-counter path that does not
    /// update per handle, which reads as a perfectly flat line whatever happens —
    /// indistinguishable from a pass. <c>GetProcessHandleCount</c> is the exact number.</para>
    /// </summary>
    private static long Handles()
    {
        if (OperatingSystem.IsWindows() && GetProcessHandleCount(GetCurrentProcess(), out uint count))
            return count;
        using var self = Process.GetCurrentProcess();
        self.Refresh();
        return self.HandleCount;
    }

    /// <summary>The first few exported handles, so that a flat count can be told from a driver
    /// handing back the same handle every time — which would also look like no leak.</summary>
    private static string SampleHandles(VulkanImage vk, int n, bool fresh)
    {
        if (!OperatingSystem.IsWindows()) return SampleFds(vk, n, fresh);
        var seen = new List<string>(n);
        for (int i = 0; i < n; i++)
        {
            if (fresh) vk.RecreateImage();
            nint h = vk.ExportWin32Handle();
            seen.Add($"0x{h:X}");
            CloseHandle(h);
        }
        return string.Join(" ", seen);
    }

    /// <summary>
    /// The descriptor side of the same question. An fd's <i>number</i> cannot answer it — close
    /// one and the next export reuses the number whatever the driver did — so every fd is held
    /// open until all are taken. Five different numbers held at once means five descriptors,
    /// each one owed a close or an import — unlike Windows, where one allocation gave one
    /// handle however often it was asked.
    ///
    /// <para>The device:inode is printed but cannot tell allocations apart on NVIDIA: every
    /// opaque fd it exports is an open of <c>/dev/nvidiactl</c>, so all of them — fresh
    /// allocations included — share one inode. Which allocation an fd names is driver state
    /// the kernel does not show.</para>
    /// </summary>
    private static string SampleFds(VulkanImage vk, int n, bool fresh)
    {
        var fds = new List<int>(n);
        var seen = new List<string>(n);
        try
        {
            for (int i = 0; i < n; i++)
            {
                if (fresh) vk.RecreateImage();
                int fd = vk.ExportFd();
                fds.Add(fd);
                string target = "?";
                try { target = File.ResolveLinkTarget($"/proc/self/fd/{fd}", false)?.ToString() ?? "?"; } catch (IOException) { }
                seen.Add($"fd{fd}={DevIno(fd)}");
                if (i == 0) seen.Insert(0, $"[{target}]");
            }
        }
        finally
        {
            foreach (int fd in fds) close(fd);
        }
        return string.Join(" ", seen);
    }

    /// <summary>"dev:ino" from fstat. x86-64 glibc layout only — st_dev then st_ino, both
    /// 64-bit, at the start of a 144-byte struct — which is the only Linux this spike runs on.</summary>
    private static unsafe string DevIno(int fd)
    {
        if (RuntimeInformation.ProcessArchitecture != Architecture.X64) return "(not x64)";
        var buf = stackalloc ulong[18];
        return fstat(fd, buf) == 0 ? $"{buf[0]:x}:{buf[1]}" : $"(fstat errno {Marshal.GetLastPInvokeError()})";
    }

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe int fstat(int fd, ulong* buf);

    [DllImport("libc", SetLastError = true)]
    private static extern int close(int fd);

    [DllImport("kernel32", SetLastError = true)]
    private static extern bool CloseHandle(nint handle);

    [DllImport("kernel32", SetLastError = true)]
    private static extern bool GetProcessHandleCount(nint process, out uint count);

    [DllImport("kernel32")]
    private static extern nint GetCurrentProcess();
}
