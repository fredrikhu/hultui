using System.Text;

namespace HultUI.Media.Testing;

/// <summary>
/// Writes a small uncompressed video file for backend tests: YUV4MPEG2, which every
/// FFmpeg-based decoder reads and which needs no encoder to produce. Each frame is
/// flat grey, brighter as the clip goes on; with <paramref name="splitHalves"/> the
/// top half is bright and the bottom dark instead, so a test can tell which way up
/// a frame came out.
/// </summary>
public static class TestClip
{
    /// <returns>The path written.</returns>
    public static string WriteY4m(string path, int width = 64, int height = 36, int fps = 10, int frames = 20,
        bool splitHalves = false)
    {
        if (width % 2 != 0 || height % 2 != 0)
            throw new ArgumentException("4:2:0 needs even dimensions.");

        using var file = File.Create(path);
        file.Write(Encoding.ASCII.GetBytes($"YUV4MPEG2 W{width} H{height} F{fps}:1 Ip C420jpeg\n"));
        var luma = new byte[width * height];
        var chroma = new byte[width * height / 4];
        Array.Fill(chroma, (byte)128);
        for (int i = 0; i < frames; i++)
        {
            if (splitHalves)
            {
                Array.Fill(luma, (byte)235, 0, width * height / 2);
                Array.Fill(luma, (byte)16, width * height / 2, width * height / 2);
            }
            else
            {
                Array.Fill(luma, (byte)Math.Min(235, 16 + i * 10));
            }
            file.Write("FRAME\n"u8);
            file.Write(luma);
            file.Write(chroma);
            file.Write(chroma);
        }
        return path;
    }

    /// <summary>A clip in the temp directory with a unique name; the caller deletes it.</summary>
    public static string WriteTempY4m(int width = 64, int height = 36, int fps = 10, int frames = 20, bool splitHalves = false)
        => WriteY4m(Path.Combine(Path.GetTempPath(), $"hultui-clip-{Guid.NewGuid():N}.y4m"), width, height, fps, frames, splitHalves);
}
