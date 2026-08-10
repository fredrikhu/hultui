using System.IO.Compression;

namespace VesperCommander;

public enum ArchiveStatus { Ok, Empty, Corrupt, NeedsSevenZip, Encrypted, NotFound }

/// <summary>A listing of one level inside an archive, plus its status and roll-up sizes.
/// <paramref name="Unsafe"/> counts members left out because their stored path was not one we
/// would write (see <see cref="ArchivePath.SafeMemberPath"/>) — surfaced rather than silently
/// dropped, because an archive containing one is telling you something.</summary>
public sealed record ArchiveListing(
    ArchiveStatus Status, IReadOnlyList<FileEntry> Entries, long TotalSize = 0, long TotalPacked = 0,
    int Unsafe = 0);

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
        // Resolve any nested-archive hops to a real on-disk archive; entry paths stay virtual.
        if (Resolve(archiveFile, internalPath, password, sevenZip) is not var (disk, inner))
            return new(ArchiveStatus.Corrupt, []);
        string ext = Path.GetExtension(disk).ToLowerInvariant();
        bool builtin = ext is ".zip" && !NeedsSevenZip(ext, sevenZip);
        try
        {
            return builtin
                ? ListZip(disk, inner, archiveFile, internalPath)
                : sevenZip ? SevenZip.List(disk, inner, password)
                           : new(ArchiveStatus.NeedsSevenZip, []);
        }
        catch (InvalidDataException) { return new(ArchiveStatus.Corrupt, []); }
        catch (Exception) { return new(ArchiveStatus.Corrupt, []); }
    }

    private static bool NeedsSevenZip(string ext, bool sevenZip) =>
        ext is not ".zip";   // only plain zip is built-in-readable here

    // diskArchive/diskInternal locate the bytes; virtArchive/virtInternal build the entry
    // FullPaths (the virtual chain, so nested archives read seamlessly in the breadcrumb).
    private static ArchiveListing ListZip(string diskArchive, string diskInternal, string virtArchive, string virtInternal)
    {
        using var zip = ZipFile.OpenRead(diskArchive);
        string prefix = diskInternal.Length == 0 ? "" : diskInternal.Replace('\\', '/').TrimEnd('/') + "/";
        var dirs = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new List<FileEntry>();
        long totalSize = 0, totalPacked = 0;
        int unsafeCount = 0;

        foreach (var entry in zip.Entries)
        {
            // Before anything else: a member whose stored path escapes the archive is dropped
            // here, so it never reaches a listing, a mark, or an extraction.
            if (ArchivePath.SafeMemberPath(entry.FullName) is not { } full) { unsafeCount++; continue; }
            if (prefix.Length > 0 && !full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            string rest = full[prefix.Length..];
            if (rest.Length == 0) continue;
            int slash = rest.IndexOf('/');
            if (slash >= 0) { dirs.Add(rest[..slash]); continue; }
            if (full.EndsWith('/')) continue;   // a directory marker entry

            string inner = virtInternal.Length == 0 ? rest : virtInternal + Path.DirectorySeparatorChar + rest;
            files.Add(new FileEntry(rest, ArchivePath.Combine(virtArchive, inner),
                IsDir: false, entry.Length, entry.LastWriteTime.DateTime, PackedSize: entry.CompressedLength));
            totalSize += entry.Length;
            totalPacked += entry.CompressedLength;
        }

        var result = new List<FileEntry>
        {
            new("..", ArchivePath.ParentOf(virtArchive, virtInternal), IsDir: true, 0, DateTime.Now, IsParent: true),
        };
        foreach (var d in dirs)
        {
            string inner = virtInternal.Length == 0 ? d : virtInternal + Path.DirectorySeparatorChar + d;
            result.Add(new FileEntry(d, ArchivePath.Combine(virtArchive, inner), IsDir: true, 0, DateTime.Now));
        }
        result.AddRange(files);

        var status = dirs.Count == 0 && files.Count == 0 ? ArchiveStatus.Empty : ArchiveStatus.Ok;
        return new ArchiveListing(status, result, totalSize, totalPacked, unsafeCount);
    }

    /// <summary>Open a member's bytes for reading (extract / preview). Caller disposes.</summary>
    public static Stream? OpenEntry(string archiveFile, string internalPath, string? password, bool sevenZip)
    {
        if (Resolve(archiveFile, internalPath, password, sevenZip) is not var (disk, inner)) return null;
        return OpenEntryDirect(disk, inner, password, sevenZip);
    }

    private static Stream? OpenEntryDirect(string archiveFile, string internalPath, string? password, bool sevenZip)
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

    // ── Nested archives (archive inside an archive) ──────────────────────
    // The internal path may cross into a member that is itself an archive (outer.zip →
    // inner.zip → …). Resolve() walks the path, extracting each nested archive member to a
    // temp file, and returns the real on-disk archive + the remaining internal path. Entry
    // FullPaths stay virtual (the outer chain), so the breadcrumb and navigation are seamless
    // for zip-in-zip. Extractions are cached for the session and cleaned up on exit.
    //
    // Both panes list on their own background threads, and either can land here — so the
    // cache is guarded. A lock rather than a ConcurrentDictionary because the extraction
    // itself must not be duplicated: two threads that both miss on the same member each copy
    // it out to a temp file of their own, and the one that loses the race to the dictionary
    // leaves its copy in %TEMP% until the app exits. Serializing costs a wait on the rare
    // occasion both panes open the same nested archive at once, on threads that are already
    // off the UI.
    private static readonly object _nestedLock = new();
    private static readonly Dictionary<string, string> _nestedTemp = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Where nested archives are unpacked for the session.</summary>
    internal static string NestedTempDir => Path.Combine(Path.GetTempPath(), "vesper-nested");

    private static (string disk, string inner)? Resolve(string archiveFile, string internalPath, string? password, bool sevenZip)
    {
        string current = archiveFile;
        var segs = internalPath.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        var acc = new List<string>();
        foreach (var seg in segs)
        {
            acc.Add(seg);
            if (IsArchiveFile(seg))
            {
                var temp = ExtractNested(current, string.Join('\\', acc), password, sevenZip);
                if (temp == null) return null;   // couldn't extract the nested archive
                current = temp;
                acc.Clear();
            }
        }
        return (current, string.Join('\\', acc));
    }

    private static string? ExtractNested(string diskArchive, string memberInternal, string? password, bool sevenZip)
    {
        string key = diskArchive + "|" + memberInternal;
        lock (_nestedLock)
        {
            if (_nestedTemp.TryGetValue(key, out var cached) && File.Exists(cached)) return cached;
            try
            {
                using var s = OpenEntryDirect(diskArchive, memberInternal, password, sevenZip);
                if (s == null) return null;
                Directory.CreateDirectory(NestedTempDir);
                var temp = Path.Combine(NestedTempDir, Guid.NewGuid().ToString("N") + Path.GetExtension(memberInternal));
                using (var fs = File.Create(temp)) s.CopyTo(fs);
                _nestedTemp[key] = temp;
                return temp;
            }
            catch { return null; }
        }
    }

    /// <summary>Delete the temp files created for nested-archive browsing (app exit).</summary>
    public static void CleanupTemp()
    {
        lock (_nestedLock)
        {
            try { if (Directory.Exists(NestedTempDir)) Directory.Delete(NestedTempDir, true); }
            catch { }
            _nestedTemp.Clear();
        }
    }

    /// <summary>
    /// Create <paramref name="archivePath"/> from <paramref name="items"/> (source path →
    /// entry name). Built-in zip, or 7-Zip for other formats / encryption. Returns an error
    /// message or null.
    ///
    /// <para><paramref name="addToExisting"/> is the Pack dialog's "Add to it": the new items
    /// join what is already in the archive. Without it the archive is replaced. The built-in
    /// zip writer used to replace either way, so the button labelled "Add to it" deleted the
    /// very archive it offered to add to — <c>ZipArchiveMode.Create</c> starts an empty
    /// archive, and nothing told it not to.</para>
    /// </summary>
    public static string? Create(IReadOnlyList<(string Source, string Entry)> items, string archivePath,
        CompressionLevel level, string? password, bool sevenZip, bool addToExisting = false)
    {
        string ext = Path.GetExtension(archivePath).ToLowerInvariant();
        bool append = addToExisting && File.Exists(archivePath);
        try
        {
            // Replacing: clear the old archive first. Done for both writers, because 7-Zip's
            // "a" command appends by default — so without this, "create" would mean replace
            // through one writer and merge through the other.
            if (!append && File.Exists(archivePath)) File.Delete(archivePath);

            if (ext is ".zip" && password is null && !sevenZip)
            {
                using var zip = ZipFile.Open(archivePath, append ? ZipArchiveMode.Update : ZipArchiveMode.Create);
                foreach (var (src, entry) in items)
                {
                    if (!File.Exists(src)) continue;
                    var name = entry.Replace('\\', '/');
                    // Update keeps what is already there, so a same-named entry has to be
                    // removed explicitly or the archive ends up holding two of it.
                    if (append) zip.GetEntry(name)?.Delete();
                    zip.CreateEntryFromFile(src, name, level);
                }
                return null;
            }
            return sevenZip
                ? SevenZip.Create(items, archivePath, level, password)   // "a" appends to what survives above
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
