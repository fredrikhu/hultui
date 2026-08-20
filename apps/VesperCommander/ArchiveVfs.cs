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

/// <summary>Progress and cancellation for a pack run, handed to
/// <see cref="ArchiveVfs.Create"/>. Every member is optional, and creation behaves exactly as
/// it did before when none is supplied — the engine has no opinion about who is watching.
///
/// <para>There is deliberately no "skip this entry". The built-in writer streams a source
/// straight into the archive, so abandoning one half-written would leave a truncated member
/// behind that lists and extracts as if it were whole. The Pack overlay therefore offers
/// Pause and Cancel and no per-entry skip, unlike the copy overlay it borrows its shape
/// from.</para></summary>
public sealed class PackProgress
{
    /// <summary>An entry is about to be written: its index in the items list, and its size
    /// in bytes so the caller can size a bar before any of it has moved.</summary>
    public Action<int, long>? EntryStart;

    /// <summary>Bytes just written for the entry in progress (a delta, not a running total).</summary>
    public Action<long>? Bytes;

    /// <summary>An entry finished. <c>false</c> means it was left out — its source had gone
    /// missing between the selection and the write.</summary>
    public Action<int, bool>? EntryDone;

    /// <summary>Whole-job completion, 0–100, for the backend that can only report that much:
    /// 7-Zip prints one percentage for the run rather than a byte count per member.</summary>
    public Action<double>? Percent;

    /// <summary>Polled between entries and between chunks within one. True stops the run,
    /// which returns <see cref="ArchiveVfs.CancelledError"/>.</summary>
    public Func<bool>? Cancelled;

    /// <summary>Blocks while the run is paused. Called at the same points as
    /// <see cref="Cancelled"/>, so a pause held during a large member takes effect inside it
    /// rather than at the next entry boundary.</summary>
    public Action? WaitWhilePaused;

    internal bool IsCancelled => Cancelled?.Invoke() == true;

    /// <summary>The pause-then-check pair every loop in the writers uses: hold here while
    /// paused, then report whether the run was cancelled (possibly while it waited).</summary>
    internal bool Stop()
    {
        WaitWhilePaused?.Invoke();
        return IsCancelled;
    }
}

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
        // The nested walk stops *before* the last segment here. When the member itself is an
        // archive — a zip inside a zip — what a read wants is that file's own bytes, not a
        // listing of what is in it. Hopping would unpack it to temp and then look for a member
        // called "", which is why extracting a zip out of a zip failed outright.
        if (Resolve(archiveFile, internalPath, password, sevenZip, hopLast: false) is not var (disk, inner))
            return null;
        return OpenEntryDirect(disk, inner, password, sevenZip);
    }

    private static Stream? OpenEntryDirect(string archiveFile, string internalPath, string? password, bool sevenZip)
    {
        string ext = Path.GetExtension(archiveFile).ToLowerInvariant();
        if (ext is ".zip" && !sevenZip)
        {
            var zip = ZipFile.OpenRead(archiveFile);
            var entry = FindMember(zip, internalPath);
            if (entry is null) { zip.Dispose(); return null; }
            return new ArchiveEntryStream(zip, entry.Open());
        }
        return sevenZip ? SevenZip.OpenEntry(archiveFile, internalPath, password) : null;
    }

    /// <summary>
    /// The member at <paramref name="internalPath"/>, found the way the listing built that path.
    ///
    /// <para><c>ZipArchive.GetEntry</c> matches the stored name character for character, and the
    /// paths this VFS hands around are not stored names — <see cref="ArchivePath.SafeMemberPath"/>
    /// has folded away "." segments and turned every separator into "/". So a zip that stores
    /// <c>./notes/a.txt</c> or the backslash-separated <c>notes\a.txt</c> (both of which real
    /// zippers write) listed fine and then failed to extract, one member at a time, reported as
    /// "archive member unreadable".</para>
    ///
    /// <para>Stored-name lookup is still tried first: it is the common case and goes through the
    /// archive's own index. The scan behind it compares normalized to normalized, ordinal before
    /// case-insensitive so that two members differing only in case still resolve to the right one.</para>
    /// </summary>
    private static ZipArchiveEntry? FindMember(ZipArchive zip, string internalPath)
    {
        string want = (ArchivePath.SafeMemberPath(internalPath) ?? internalPath.Replace('\\', '/')).TrimEnd('/');
        if (want.Length == 0) return null;
        if (zip.GetEntry(want) is { } direct) return direct;

        ZipArchiveEntry? insensitive = null;
        foreach (var e in zip.Entries)
        {
            if (ArchivePath.SafeMemberPath(e.FullName) is not { } full) continue;
            var norm = full.TrimEnd('/');
            if (string.Equals(norm, want, StringComparison.Ordinal)) return e;
            if (insensitive is null && string.Equals(norm, want, StringComparison.OrdinalIgnoreCase))
                insensitive = e;
        }
        return insensitive;
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

    /// <param name="hopLast">Whether a final segment that is itself an archive counts as a hop.
    /// True for listing — descending into <c>outer.zip\inner.zip</c> should show what is inside
    /// inner.zip. False for reading bytes, where that same path names a file to extract.</param>
    private static (string disk, string inner)? Resolve(string archiveFile, string internalPath,
        string? password, bool sevenZip, bool hopLast = true)
    {
        string current = archiveFile;
        var segs = internalPath.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        var acc = new List<string>();
        for (int i = 0; i < segs.Length; i++)
        {
            string seg = segs[i];
            acc.Add(seg);
            if (IsArchiveFile(seg) && (hopLast || i < segs.Length - 1))
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
    ///
    /// <para>Pass <paramref name="progress"/> to drive a progress overlay and to be able to
    /// stop the run; a cancelled run returns <see cref="CancelledError"/>, which the caller
    /// must tell apart from a real failure because the two want different cleanup.</para>
    /// </summary>
    public static string? Create(IReadOnlyList<(string Source, string Entry)> items, string archivePath,
        CompressionLevel level, string? password, bool sevenZip, bool addToExisting = false,
        PackProgress? progress = null)
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
                return CreateZip(items, archivePath, level, append, progress);

            return sevenZip
                ? SevenZip.Create(items, archivePath, level, password, progress)   // "a" appends to what survives above
                : "This format needs 7-Zip — set it in Tools → Archiver";
        }
        catch (Exception ex) { return ex.Message; }
    }

    /// <summary>What <see cref="Create"/> returns when <see cref="PackProgress.Cancelled"/>
    /// asked it to stop. Not a failure: the caller reports it as cancelled and, for an archive
    /// it had just created, deletes the part-written file.</summary>
    public const string CancelledError = "cancelled";

    /// <summary>The built-in zip writer. Sources are streamed in chunks rather than handed to
    /// <c>CreateEntryFromFile</c>, which does the whole file in one opaque call: the overlay
    /// needs the bytes counted as they go, and Cancel needs somewhere to take effect inside a
    /// member rather than only between them.</summary>
    private static string? CreateZip(IReadOnlyList<(string Source, string Entry)> items, string archivePath,
        CompressionLevel level, bool append, PackProgress? progress)
    {
        using (var zip = ZipFile.Open(archivePath, append ? ZipArchiveMode.Update : ZipArchiveMode.Create))
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (progress?.Stop() == true) return CancelledError;
                var (src, entry) = items[i];
                // A source that has gone missing since the selection is reported rather than
                // silently passed over: the queue row says so instead of sitting at "queued".
                if (!File.Exists(src)) { progress?.EntryDone?.Invoke(i, false); continue; }

                var info = new FileInfo(src);
                progress?.EntryStart?.Invoke(i, info.Length);

                var name = entry.Replace('\\', '/');
                // Update keeps what is already there, so a same-named entry has to be
                // removed explicitly or the archive ends up holding two of it.
                if (append) zip.GetEntry(name)?.Delete();
                var e = zip.CreateEntry(name, level);
                e.LastWriteTime = ZipStamp(info.LastWriteTime);

                using (var input = File.OpenRead(src))
                using (var output = e.Open())
                    if (!Pump(input, output, progress)) return CancelledError;

                progress?.EntryDone?.Invoke(i, true);
            }
        }
        return null;
    }

    /// <summary>Copy a source into its archive entry, counting the bytes and stopping when
    /// asked. False means the run was cancelled part-way through this member.</summary>
    private static bool Pump(Stream input, Stream output, PackProgress? progress)
    {
        var buffer = new byte[128 * 1024];
        int n;
        while ((n = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (progress?.Stop() == true) return false;
            output.Write(buffer, 0, n);
            progress?.Bytes?.Invoke(n);
        }
        return true;
    }

    /// <summary>Zip stores DOS timestamps, whose epoch is 1980 — assigning an mtime outside
    /// that range throws, which would fail the whole pack over a file with a silly date. The
    /// out-of-range stamp is clamped instead, as <c>CreateEntryFromFile</c> did for us before
    /// the entries were written by hand.</summary>
    private static DateTimeOffset ZipStamp(DateTime t) =>
        t.Year is < 1980 or > 2107 ? new DateTime(1980, 1, 1, 0, 0, 0) : t;

    /// <summary>Every file member in the archive, keyed by '/'-normalised stored path, with its
    /// size and mtime. What an add consults to find out whether it would replace something.
    ///
    /// <para>Not <see cref="ListEntries"/>, which returns one level: adding a folder brings
    /// members at depths the pane never showed, and each of those can collide too. Null when the
    /// archive can't be read.</para>
    ///
    /// <para><paramref name="sevenZip"/> is whether 7-Zip is <em>available</em>, not whether to
    /// use it — which backend reads is decided here from the format, so no caller can pair a
    /// plain zip with the wrong reader and get a listing that disagrees with the pane's.</para></summary>
    public static Dictionary<string, (long Size, DateTime Modified)>? AllMembers(
        string archiveFile, string? password, bool sevenZip)
    {
        if (!File.Exists(archiveFile)) return null;
        string ext = Path.GetExtension(archiveFile).ToLowerInvariant();
        try
        {
            if (ext is ".zip" && password is null)
            {
                using var zip = ZipFile.OpenRead(archiveFile);
                var map = new Dictionary<string, (long, DateTime)>(StringComparer.OrdinalIgnoreCase);
                foreach (var e in zip.Entries)
                {
                    if (ArchivePath.SafeMemberPath(e.FullName) is not { } full) continue;
                    if (full.EndsWith('/')) continue;               // a directory marker
                    map[full.TrimEnd('/')] = (e.Length, e.LastWriteTime.DateTime);
                }
                return map;
            }
            return sevenZip ? SevenZip.AllMembers(archiveFile, password) : null;
        }
        catch { return null; }
    }

    /// <summary>
    /// Add <paramref name="items"/> to an existing archive, storing them under
    /// <paramref name="internalPath"/> — the folder inside the archive the pane is showing.
    /// <c>Entry</c> is the stored path <em>relative to that folder</em>.
    ///
    /// <para>Same-named members are replaced, so conflicts are the caller's to resolve first;
    /// this is the write, not the decision.</para>
    ///
    /// <para>Backends differ in what they can be told. The BCL writer takes an entry name per
    /// source, so it stores straight into the right place. 7-Zip's command line has no way to
    /// remap a name while adding — it packs by source path — so its sources go in at the root
    /// and are then renamed into place with <c>7z rn</c>, which moves whole subtrees without
    /// rewriting bytes. That detour is refused rather than risked when a name it would land on
    /// at the root is already taken; see <paramref name="internalPath"/>.</para>
    /// </summary>
    /// <summary>Which writer an add to this archive will go through: 7-Zip when the format or an
    /// encryption password puts it beyond the BCL. Public because the UI has to agree with it —
    /// the two backends report progress differently (bytes per member vs one run percentage), so
    /// a caller that guesses wrong shows a bar that never fills.</summary>
    public static bool WritesThroughSevenZip(string archiveFile, string? password) =>
        Path.GetExtension(archiveFile).ToLowerInvariant() is not ".zip" || password is not null;

    /// <summary>
    /// Add an empty folder <paramref name="name"/> under <paramref name="internalPath"/>.
    /// Returns an error message or null.
    ///
    /// <para>A zip has no directories. What a folder is, in one, is either implied by a member
    /// path or spelled out by a zero-length entry whose name ends in "/" — and since an empty
    /// folder has no member to imply it, the marker is the only way to say it exists. The
    /// listing already reads one back as a folder, so nothing else has to change.</para>
    ///
    /// <para>7-Zip packs by source path and has no switch for "store an empty folder called X",
    /// so it gets one to pack: an empty directory built in a temp folder, handed to the same
    /// <see cref="AddMembers"/> path everything else goes through. Slower and clumsier than the
    /// zip marker, but it is the only way to say this through a command line.</para>
    /// </summary>
    public static string? CreateFolder(string archiveFile, string internalPath, string name,
        string? password, bool sevenZip)
    {
        if (!File.Exists(archiveFile)) return "That archive is no longer there";
        string ext = Path.GetExtension(archiveFile).ToLowerInvariant();
        if (!Caps(ext, sevenZip).Write)
            return ext is ".rar"
                ? "rar archives are read-only"
                : "Adding to this format needs 7-Zip — set it in Tools → Archiver";

        // The name is checked on its own before being joined, so a "../" in it is refused
        // rather than quietly resolving against the folder the pane is showing.
        if (SafeRelative(name) is not { } relative) return "That name can't be stored in an archive";

        string prefix = internalPath.Replace('\\', '/').Trim('/');
        string entry = prefix.Length == 0 ? relative : prefix + "/" + relative;

        var existing = AllMembers(archiveFile, password, sevenZip);
        if (existing != null && existing.Keys.Any(k =>
                k.Equals(entry, StringComparison.OrdinalIgnoreCase) ||
                k.StartsWith(entry + "/", StringComparison.OrdinalIgnoreCase)))
            return $"\"{name}\" is already in the archive";

        if (!WritesThroughSevenZip(archiveFile, password))
        {
            try
            {
                using var zip = ZipFile.Open(archiveFile, ZipArchiveMode.Update);
                if (FindMember(zip, entry) != null) return $"\"{name}\" is already in the archive";
                zip.CreateEntry(entry + "/");
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        // Build the folder on disk and let the normal add carry it in. A nested name ("a/b")
        // becomes a nested empty tree whose *root* is the source, so 7-Zip stores both levels.
        string root = relative.Split('/')[0];
        string staging = Path.Combine(Path.GetTempPath(), "vesper-mkdir", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(staging, relative));
            return AddMembers(archiveFile, internalPath, [(Path.Combine(staging, root), root)],
                password, sevenZip);
        }
        catch (Exception ex) { return ex.Message; }
        finally { try { Directory.Delete(staging, true); } catch { /* temp, best effort */ } }
    }

    /// <summary>A user-typed folder name reduced to a relative archive path, or null when it is
    /// one we won't store. Separators are allowed so "a/b" makes both levels, the way it does on
    /// disk; a trailing one is dropped, since "notes/" plainly means the folder "notes".
    ///
    /// <para>A <em>leading</em> one is refused rather than dropped. Trimming it would turn
    /// "/rooted" into "rooted" and create it in the folder the pane happens to be showing —
    /// silently answering a different question than the one typed. Same for a drive prefix and
    /// for "..", which <see cref="ArchivePath.SafeMemberPath"/> rejects.</para></summary>
    private static string? SafeRelative(string name)
    {
        var trimmed = name.Trim().Replace('\\', '/').TrimEnd('/');
        if (trimmed.Length == 0) return null;
        if (trimmed[0] == '/') return null;
        if (trimmed.Length >= 2 && trimmed[1] == ':') return null;
        return ArchivePath.SafeMemberPath(trimmed)?.TrimEnd('/');
    }

    public static string? AddMembers(string archiveFile, string internalPath,
        IReadOnlyList<(string Source, string Entry)> items, string? password, bool sevenZip,
        PackProgress? progress = null)
    {
        if (items.Count == 0) return null;
        if (!File.Exists(archiveFile)) return "That archive is no longer there";

        string ext = Path.GetExtension(archiveFile).ToLowerInvariant();
        if (!Caps(ext, sevenZip).Write)
            return ext is ".rar"
                ? "rar archives are read-only"
                : "Adding to this format needs 7-Zip — set it in Tools → Archiver";

        string prefix = internalPath.Replace('\\', '/').Trim('/');

        if (!WritesThroughSevenZip(archiveFile, password))
        {
            // The entry name carries the folder, so one Update pass puts everything in place.
            var named = items
                .Select(i => (i.Source, Entry: prefix.Length == 0 ? i.Entry : prefix + "/" + i.Entry.Replace('\\', '/')))
                .ToList();
            return CreateZip(named, archiveFile, CompressionLevel.Optimal, append: true, progress);
        }

        // ── the 7-Zip detour ────────────────────────────────────────────────
        // The top-level names the sources will land under at the archive root.
        var roots = items
            .Select(i => i.Entry.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault())
            .Where(s => !string.IsNullOrEmpty(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (prefix.Length > 0)
        {
            var existing = AllMembers(archiveFile, password, sevenZip);
            if (existing != null)
            {
                // A root member of the same name would be overwritten by the add and then
                // renamed away — losing a file nobody was asked about. Refused, not risked.
                var clash = roots.FirstOrDefault(r =>
                    existing.Keys.Any(k => k.Equals(r, StringComparison.OrdinalIgnoreCase)
                                        || k.StartsWith(r + "/", StringComparison.OrdinalIgnoreCase)));
                if (clash != null)
                    return $"\"{clash}\" is already at the top level of this archive — "
                         + "add it there instead, or rename it first";

                // Clear what the closing rename is about to land on. These are members the
                // caller already decided to replace — an unapproved one never reaches here —
                // and "rn" onto an occupied path fails rather than merging.
                var occupied = roots
                    .Select(r => prefix + "/" + r)
                    .Where(t => existing.Keys.Any(k => k.Equals(t, StringComparison.OrdinalIgnoreCase)
                                                    || k.StartsWith(t + "/", StringComparison.OrdinalIgnoreCase)))
                    .ToList();
                if (occupied.Count > 0 && SevenZip.Delete(archiveFile, occupied, password) is { } delErr)
                    return delErr;
            }
        }

        var err = SevenZip.Create(items, archiveFile, CompressionLevel.Optimal, password, progress);
        if (err != null) return err;
        if (prefix.Length == 0) return null;

        return SevenZip.Rename(archiveFile, [.. roots.Select(r => (r!, prefix + "/" + r))], password);
    }

    /// <summary>
    /// Remove members from an archive. <paramref name="internalPaths"/> are the pane's
    /// backslash-separated internal paths; a directory path removes everything beneath it.
    /// Plain zip rewrites through the BCL (mirroring how it is listed); every other writable
    /// format goes through <c>7z d</c>. Returns an error message or null.
    ///
    /// <para>Nested archives are the caller's problem: a path that crosses into an inner
    /// archive reaches a temp extraction, so a delete there would edit the copy and leave the
    /// real archive untouched — the UI blocks it before getting here.</para>
    /// </summary>
    public static string? DeleteMembers(string archiveFile, IReadOnlyList<string> internalPaths,
        string? password, bool sevenZip)
    {
        var targets = internalPaths
            .Select(p => p.Replace('\\', '/').TrimEnd('/'))
            .Where(p => p.Length > 0).ToList();
        if (targets.Count == 0) return null;

        string ext = Path.GetExtension(archiveFile).ToLowerInvariant();
        if (!Caps(ext, sevenZip).Write)
            return ext is ".rar"
                ? "rar archives are read-only"
                : "Deleting from this format needs 7-Zip — set it in Tools → Archiver";

        try
        {
            return ext is ".zip" ? DeleteFromZip(archiveFile, targets)
                                 : SevenZip.Delete(archiveFile, targets, password);
        }
        catch (Exception ex) { return ex.Message; }
    }

    private static string? DeleteFromZip(string archiveFile, List<string> targets)
    {
        using var zip = ZipFile.Open(archiveFile, ZipArchiveMode.Update);
        int removed = 0;
        // Normalized the same way the listing normalized it — through SafeMemberPath, not just
        // a separator swap — so a member the pane showed as "notes/a.txt" is still matched when
        // the archive stored it as "./notes/a.txt". Matching is case-insensitive like the
        // listing, so what the pane showed under a folder is what leaves with it. Directory
        // markers end with '/', which the TrimEnd folds into the exact match.
        foreach (var entry in zip.Entries.ToList())
        {
            if (ArchivePath.SafeMemberPath(entry.FullName) is not { } full) continue;
            var norm = full.TrimEnd('/');
            if (targets.Any(t => norm.Equals(t, StringComparison.OrdinalIgnoreCase)
                              || norm.StartsWith(t + "/", StringComparison.OrdinalIgnoreCase)))
            { entry.Delete(); removed++; }
        }
        return removed > 0 ? null : "Nothing in the archive matched";
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
