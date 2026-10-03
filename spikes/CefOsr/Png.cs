using System.IO.Compression;

namespace CefOsr;

/// <summary>Just enough PNG to look at a frame: 8-bit RGBA, from CEF's BGRA.</summary>
internal static class Png
{
    public static unsafe void SaveBgra(string path, nint bgra, int width, int height, int stride = 0)
    {
        if (stride == 0) stride = width * 4;
        var raw = new byte[(width * 4 + 1) * height];
        var src = (byte*)bgra;
        for (int y = 0, o = 0; y < height; y++)
        {
            raw[o++] = 0;   // filter: none
            var row = src + (long)y * stride;
            for (int x = 0; x < width; x++, o += 4)
            {
                raw[o] = row[x * 4 + 2];
                raw[o + 1] = row[x * 4 + 1];
                raw[o + 2] = row[x * 4];
                raw[o + 3] = row[x * 4 + 3];
            }
        }
        using var f = File.Create(path);
        f.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10]);
        var ihdr = new byte[13];
        BE(ihdr, 0, (uint)width); BE(ihdr, 4, (uint)height);
        ihdr[8] = 8; ihdr[9] = 6;   // 8 bits, RGBA
        Chunk(f, "IHDR", ihdr);
        using (var ms = new MemoryStream())
        {
            using (var z = new ZLibStream(ms, CompressionLevel.Fastest, leaveOpen: true)) z.Write(raw);
            Chunk(f, "IDAT", ms.ToArray());
        }
        Chunk(f, "IEND", []);
    }

    static void BE(byte[] b, int o, uint v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }

    static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4]; BE(len, 0, (uint)data.Length); s.Write(len);
        var td = new byte[4 + data.Length];
        for (int i = 0; i < 4; i++) td[i] = (byte)type[i];
        data.CopyTo(td, 4);
        s.Write(td);
        var crc = new byte[4]; BE(crc, 0, Crc(td)); s.Write(crc);
    }

    static uint Crc(byte[] d)
    {
        uint c = 0xFFFFFFFF;
        foreach (var b in d)
        {
            c ^= b;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        }
        return c ^ 0xFFFFFFFF;
    }
}
