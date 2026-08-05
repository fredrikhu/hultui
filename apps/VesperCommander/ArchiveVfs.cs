using System.IO.Compression;

namespace VesperCommander;

public enum ArchiveStatus { Ok, Empty, Corrupt, NeedsSevenZip, Encrypted, NotFound }

/// <summary>A listing of one level inside an archive, plus its status and roll-up sizes.</summary>
public sealed record ArchiveListing(
    ArchiveStatus Status, IReadOnlyList<FileEntry> Entries, long TotalSize = 0, long TotalPacked = 0);

/// <summary>What a backend can do with a given format.</summary>
public readonly record struct ArchiveCaps(bool Read, bool Write, bool Encrypt);

/// <summary>The archive virtual filesystem: lists a level inside an archive, opens a
/// member's bytes, and creates archives. Plain zip (and tar/gz read) go through the BCL
/// <see cref="ZipArchive"/>; 7z / rar / encryption route to <see cref="SevenZip"/> when it
/// is configured. All reads are best-effort — failures map to an <see cref="ArchiveStatus"/>.</summary>
public static class ArchiveVfs
{
    public static bool IsArchiveFile(string path) => ArchivePath.IsArchiveFile(path);

    /// <summary>Read/write/encrypt capability for an extension, given whether 7-Zip is
    /// available (the design's capability matrix).</summary>
    public static ArchiveCaps Caps(string ext, bool sevenZip)
    {
        ext = ext.ToLowerInvariant();
        bool zip = ext is ".zip";
        bool tarGz = ext is ".gz" or ".tar" or ".tgz" or ".bz2" or ".xz";
        if (sevenZip)
        {
            if (ext is ".rar") return new(Read: true, Write: false, Encrypt: false);   // rar read-only
            return new(Read: true, Write: true, Encrypt: true);
        }
        // Built-in only.
        if (zip) return new(Read: true, Write: true, Encrypt: false);
        if (tarGz) return new(Read: true, Write: false, Encrypt: false);
        return new(Read: false, Write: false, Encrypt: false);   // 7z / rar need 7-Zip
    }

    /// <summary>List the immediate children at <paramref name="internalPath"/> inside the
    /// archive (a leading ".." parent, then folders, then files).</summary>
    public static ArchiveListing ListEntries(string archiveFile, string internalPath, string? password, bool sevenZip)
    {
        if (!File.Exists(archiveFile)) return new(ArchiveStatus.NotFound, []);
        string ext = Path.GetExtension(archiveFile).ToLowerInvariant();
        bool builtin = ext is ".zip" && !NeedsSevenZip(ext, sevenZip);
        try
        {
            return builtin
                ? ListZip(archiveFile, internalPath)
                : sevenZip ? SevenZip.List(archiveFile, internalPath, password)
                           : new(ArchiveStatus.NeedsSevenZip, []);
        }
        catch (InvalidDataException) { return new(ArchiveStatus.Corrupt, []); }
        catch (Exception) { return new(ArchiveStatus.Corrupt, []); }
    }

    private static bool NeedsSevenZip(string ext, bool sevenZip) =>
        ext is not ".zip";   // only plain zip is built-in-readable here

    private static ArchiveListing ListZip(string archiveFile, string internalPath)
    {
        using var zip = ZipFile.OpenRead(archiveFile);
        string prefix = internalPath.Length == 0 ? "" : internalPath.Replace('\\', '/').TrimEnd('/') + "/";
        var dirs = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new List<FileEntry>();
        long totalSize = 0, totalPacked = 0;

        foreach (var entry in zip.Entries)
        {
            string full = entry.FullName.Replace('\\', '/');
            if (prefix.Length > 0 && !full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            string rest = full[prefix.Length..];
            if (rest.Length == 0) continue;
            int slash = rest.IndexOf('/');
            if (slash >= 0) { dirs.Add(rest[..slash]); continue; }
            if (entry.FullName.EndsWith("/")) continue;   // a directory marker entry

            string inner = internalPath.Length == 0 ? rest : internalPath + Path.DirectorySeparatorChar + rest;
            files.Add(new FileEntry(rest, ArchivePath.Combine(archiveFile, inner),
                IsDir: false, entry.Length, entry.LastWriteTime.DateTime, PackedSize: entry.CompressedLength));
            totalSize += entry.Length;
            totalPacked += entry.CompressedLength;
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

    /// <summary>Open a member's bytes for reading (extract / preview). Caller disposes.</summary>
    public static Stream? OpenEntry(string archiveFile, string internalPath, string? password, bool sevenZip)
    {
        string ext = Path.GetExtension(archiveFile).ToLowerInvariant();
        if (ext is ".zip" && !sevenZip)
        {
            var zip = ZipFile.OpenRead(archiveFile);
            var entry = zip.GetEntry(internalPath.Replace('\\', '/'));
            if (entry is null) { zip.Dispose(); return null; }
            return new ArchiveEntryStream(zip, entry.Open());
        }
        return sevenZip ? SevenZip.OpenEntry(archiveFile, internalPath, password) : null;
    }

    /// <summary>Create <paramref name="archivePath"/> from <paramref name="items"/>
    /// (source path → entry name). Built-in zip, or 7-Zip for other formats / encryption.
    /// Returns an error message or null.</summary>
    public static string? Create(IReadOnlyList<(string Source, string Entry)> items, string archivePath,
        CompressionLevel level, string? password, bool sevenZip)
    {
        string ext = Path.GetExtension(archivePath).ToLowerInvariant();
        try
        {
            if (ext is ".zip" && password is null && !sevenZip)
            {
                if (File.Exists(archivePath)) File.Delete(archivePath);
                using var zip = ZipFile.Open(archivePath, ZipArchiveMode.Create);
                foreach (var (src, entry) in items)
                    if (File.Exists(src)) zip.CreateEntryFromFile(src, entry.Replace('\\', '/'), level);
                return null;
            }
            return sevenZip
                ? SevenZip.Create(items, archivePath, level, password)
                : "This format needs 7-Zip — set it in Tools → Archiver";
        }
        catch (Exception ex) { return ex.Message; }
    }

    /// <summary>Wraps a member stream so the owning <see cref="ZipArchive"/> is disposed
    /// when the reader closes.</summary>
    private sealed class ArchiveEntryStream(ZipArchive owner, Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override void Flush() { }
        public override long Seek(long o, SeekOrigin r) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
        public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) { inner.Dispose(); owner.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
