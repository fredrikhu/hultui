using System.Diagnostics;
using System.IO.Compression;

namespace VesperCommander;

/// <summary>Thin shell over an external <c>7z</c> executable for the formats and encryption
/// the BCL can't handle. Configured via the 7-Zip path setting (falls back to <c>7z</c> on
/// PATH). Everything is best-effort — when 7-Zip isn't present, callers see
/// <see cref="ArchiveStatus.NeedsSevenZip"/> / null and the UI disables accordingly.</summary>
public static class SevenZip
{
    /// <summary>Set from the SevenZipPath setting at startup; empty ⇒ try "7z" on PATH.</summary>
    public static string ExePath = "";

    private static string? Resolve()
    {
        if (!string.IsNullOrWhiteSpace(ExePath) && File.Exists(ExePath)) return ExePath;
        // Bare "7z" resolves via PATH under UseShellExecute=false when present.
        return OperatingSystem.IsWindows() ? "7z.exe" : "7z";
    }

    public static bool Available()
    {
        try
        {
            var psi = new ProcessStartInfo(Resolve()!) { RedirectStandardOutput = true, UseShellExecute = false };
            using var p = Process.Start(psi);
            if (p is null) return false;
            p.WaitForExit(2000);
            return true;
        }
        catch { return false; }
    }

    public static ArchiveListing List(string archiveFile, string internalPath, string? password)
    {
        var (ok, stdout, wrongPassword) = Run(["l", "-slt", PwdArg(password), archiveFile], readAll: true);
        if (wrongPassword) return new(ArchiveStatus.Encrypted, []);
        if (!ok) return new(ArchiveStatus.Corrupt, []);

        // Parse -slt blocks into (relative path, size, packed, modified, isDir).
        var members = ParseListing(stdout);
        string prefix = internalPath.Length == 0 ? "" : internalPath.Replace('\\', '/').TrimEnd('/') + "/";
        var dirs = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new List<FileEntry>();
        long totalSize = 0, totalPacked = 0;

        foreach (var m in members)
        {
            string full = m.Path.Replace('\\', '/');
            if (prefix.Length > 0 && !full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            string rest = full[prefix.Length..];
            if (rest.Length == 0) continue;
            int slash = rest.IndexOf('/');
            if (slash >= 0) { dirs.Add(rest[..slash]); continue; }
            if (m.IsDir) continue;

            string inner = internalPath.Length == 0 ? rest : internalPath + Path.DirectorySeparatorChar + rest;
            files.Add(new FileEntry(rest, ArchivePath.Combine(archiveFile, inner),
                IsDir: false, m.Size, m.Modified, PackedSize: m.Packed));
            totalSize += m.Size;
            totalPacked += m.Packed;
        }

        var result = new List<FileEntry>
        {
            new("..", ArchivePath.ParentOf(archiveFile, internalPath), IsDir: true, 0, DateTime.Now, IsParent: true),
        };
        foreach (var d in dirs)
        {
            string inner = internalPath.Length == 0 ? d : internalPath + Path.DirectorySeparatorChar + d;
            result.Add(new FileEntry(d, ArchivePath.Combine(archiveFile, inner), IsDir: true, 0, DateTime.Now));
        }
        result.AddRange(files);
        var status = dirs.Count == 0 && files.Count == 0 ? ArchiveStatus.Empty : ArchiveStatus.Ok;
        return new ArchiveListing(status, result, totalSize, totalPacked);
    }

    /// <summary>Extract one member to a stream (<c>7z x -so</c>, member bytes on stdout).</summary>
    public static Stream? OpenEntry(string archiveFile, string internalPath, string? password)
    {
        try
        {
            var psi = Psi(["x", "-so", PwdArg(password), archiveFile, internalPath.Replace('\\', '/')]);
            psi.RedirectStandardOutput = true;
            var p = Process.Start(psi);
            return p is null ? null : new ProcessStdoutStream(p);
        }
        catch { return null; }
    }

    public static string? Create(IReadOnlyList<(string Source, string Entry)> items, string archivePath,
        CompressionLevel level, string? password)
    {
        // 7-Zip packs by source path; entry-name remapping isn't expressed on the CLI, so
        // v1 adds the sources as-is (flat/relative names preserved by 7z).
        var args = new List<string> { "a", $"-mx={Level(level)}", PwdArg(password), archivePath };
        args.AddRange(items.Select(i => i.Source));
        var (ok, _, _) = Run(args.Where(a => a.Length > 0).ToArray(), readAll: false);
        return ok ? null : "7-Zip failed to create the archive";
    }

    // ── plumbing ─────────────────────────────────────────────────────────
    private static string PwdArg(string? password) => string.IsNullOrEmpty(password) ? "" : "-p" + password;
    private static int Level(CompressionLevel l) => l switch
    {
        CompressionLevel.NoCompression => 0,
        CompressionLevel.Fastest => 1,
        CompressionLevel.SmallestSize => 9,
        _ => 6,
    };

    private static ProcessStartInfo Psi(string[] args)
    {
        var psi = new ProcessStartInfo(Resolve()!) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in args) if (a.Length > 0) psi.ArgumentList.Add(a);
        return psi;
    }

    private static (bool ok, string stdout, bool wrongPassword) Run(string[] args, bool readAll)
    {
        try
        {
            var psi = Psi(args);
            psi.RedirectStandardOutput = readAll;
            psi.RedirectStandardError = true;
            using var p = Process.Start(psi);
            if (p is null) return (false, "", false);
            string outText = readAll ? p.StandardOutput.ReadToEnd() : "";
            string errText = p.StandardError.ReadToEnd();
            p.WaitForExit();
            bool wrong = errText.Contains("Wrong password", StringComparison.OrdinalIgnoreCase)
                      || errText.Contains("password", StringComparison.OrdinalIgnoreCase) && p.ExitCode != 0;
            return (p.ExitCode == 0, outText, wrong);
        }
        catch { return (false, "", false); }
    }

    private readonly record struct Member(string Path, long Size, long Packed, DateTime Modified, bool IsDir);

    private static List<Member> ParseListing(string slt)
    {
        var list = new List<Member>();
        string? path = null; long size = 0, packed = 0; DateTime mod = default; bool isDir = false;
        void Flush() { if (path != null) list.Add(new(path, size, packed, mod, isDir)); path = null; size = packed = 0; mod = default; isDir = false; }
        foreach (var raw in slt.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            int eq = line.IndexOf('=');
            if (eq < 0) { if (line.StartsWith("----")) Flush(); continue; }
            var key = line[..eq].Trim();
            var val = line[(eq + 1)..].Trim();
            switch (key)
            {
                case "Path": Flush(); path = val; break;
                case "Size": long.TryParse(val, out size); break;
                case "Packed Size": long.TryParse(val, out packed); break;
                case "Folder": isDir = val == "+"; break;
                case "Attributes": if (val.StartsWith('D')) isDir = true; break;
                case "Modified": DateTime.TryParse(val, out mod); break;
            }
        }
        Flush();
        return list;
    }

    /// <summary>Reads a child process's stdout and reaps it on dispose.</summary>
    private sealed class ProcessStdoutStream(Process p) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => p.StandardOutput.BaseStream.Read(buffer, offset, count);
        public override void Flush() { }
        public override long Seek(long o, SeekOrigin r) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
        public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) { try { if (!p.HasExited) p.Kill(); } catch { } p.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
