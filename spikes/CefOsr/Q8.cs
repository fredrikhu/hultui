using System.Diagnostics;
using System.Runtime.InteropServices;
using CefOsr.Capi;
using static CefOsr.Native;

namespace CefOsr;

/// <summary>
/// Question 8: zero-copy. shared_texture_enabled, and what on_accelerated_paint hands over:
/// dmabuf planes (fd, stride, offset, modifier, format). Then (Q8Gl, Q8Vk) importing them.
/// </summary>
internal static unsafe partial class Q8
{
    public static int Run(Options o)
    {
        if (o.Rest.Contains("--gl")) return RunGl(o);
        if (o.Rest.Contains("--vk")) return RunVk(o);
        if (o.Rest.Contains("--bench")) return Bench(o);

        if (Host.Initialize([], o) != 1) return 1;
        Host.Pump(() => Host.ContextReady, 15000);
        int accel = 0, soft = 0;
        var inodes = new HashSet<ulong>();
        Host.AcceleratedPaint += (b, type, p) =>
        {
            var info = (cef_accelerated_paint_info_t*)p;
            if (accel++ < 2)
            {
                Log($"on_accelerated_paint {type}: {info->plane_count} plane(s), format {info->format}, modifier 0x{info->modifier:X}, " +
                    $"coded {info->extra.coded_size.width}x{info->extra.coded_size.height}, visible ({info->extra.visible_rect.x},{info->extra.visible_rect.y} " +
                    $"{info->extra.visible_rect.width}x{info->extra.visible_rect.height}), content {info->extra.content_rect.width}x{info->extra.content_rect.height}, " +
                    $"source {info->extra.source_size.width}x{info->extra.source_size.height}, timestamp {info->extra.timestamp}");
                var pl = &info->planes_0;
                for (int i = 0; i < info->plane_count; i++)
                    Log($"    plane {i}: fd {pl[i].fd}, stride {pl[i].stride}, offset {pl[i].offset}, size {pl[i].size}");
            }
            if (fstat(info->planes_0.fd, out var st) == 0) inodes.Add(st.st_ino);
        };
        Host.Paint += (b, type, w, h, buf, n, d) => soft++;
        Host.ViewWidth = 800; Host.ViewHeight = 600; Host.Scale = 1.5f;
        Host.CreateBrowser(Host.DataUrl(Q3.AnimatedPage), sharedTexture: true, sync: false);
        Host.Wait(3000);
        Log($"3 s animating: {accel} on_accelerated_paint, {soft} on_paint; {inodes.Count} distinct buffers (dmabuf inodes) in the pool");
        Host.Wait(500);
        Q1.Shutdown();
        return 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Stat
    {
        public ulong st_dev, st_ino, st_nlink;
        public uint st_mode, st_uid, st_gid, pad0;
        public ulong st_rdev;
        public long st_size, st_blksize, st_blocks;
        public long st_atime, st_atime_nsec, st_mtime, st_mtime_nsec, st_ctime, st_ctime_nsec;
        public long r0, r1, r2;
    }

    [DllImport("libc", SetLastError = true)] public static extern int fstat(int fd, out Stat st);
    [DllImport("libc", SetLastError = true)] public static extern int dup(int fd);
    [DllImport("libc", SetLastError = true)] public static extern int close(int fd);
}
