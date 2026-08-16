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

    /// <summary>The full path of the 7-Zip executable, or null when it isn't installed.
    /// <para>Never a bare name: <c>CreateProcess</c> searches the current directory before
    /// <c>PATH</c>, and this probe runs at startup, so a <c>7z.exe</c> left in the folder
    /// Vesper was launched from would otherwise be executed. See
    /// <see cref="ProgramPath"/>.</para></summary>
    /// <remarks>A configured path wins, but a stale one still falls back to whatever is on
    /// PATH — the same behaviour as before, minus the bare name.</remarks>
    public static string? ResolvePath() =>
        (string.IsNullOrWhiteSpace(ExePath) ? null : ProgramPath.Resolve(ExePath))
        ?? ProgramPath.Resolve("7z");

    public static bool Available()
    {
        if (ResolvePath() is null) return false;
        try
        {
            var psi = Psi([]);
            if (psi is null) return false;
            psi.RedirectStandardOutput = true;
            using var p = Process.Start(psi);
            if (p is null) return false;
            p.WaitForExit(2000);
            return true;
        }
        catch { return false; }
    }

    public static ArchiveListing List(string archiveFile, string internalPath, string? password)
    {
        var (ok, stdout, wrongPassword) = Run(ListArgs(archiveFile, password), readAll: true);
        if (wrongPassword) return new(ArchiveStatus.Encrypted, []);
        if (!ok) return new(ArchiveStatus.Corrupt, []);

        // Parse -slt blocks into (relative path, size, packed, modified, isDir).
        var members = ParseListing(stdout);
        string prefix = internalPath.Length == 0 ? "" : internalPath.Replace('\\', '/').TrimEnd('/') + "/";
        var dirs = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new List<FileEntry>();
        long totalSize = 0, totalPacked = 0;
        int unsafeCount = 0;

        foreach (var m in members)
        {
            // Same guard as the built-in zip reader: 7-Zip reports whatever the archive
            // stored, including a path that walks out of the destination on extraction.
            if (ArchivePath.SafeMemberPath(m.Path) is not { } full) { unsafeCount++; continue; }
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
        return new ArchiveListing(status, result, totalSize, totalPacked, unsafeCount);
    }

    /// <summary>Extract one member to a stream (<c>7z x -so</c>, member bytes on stdout).</summary>
    public static Stream? OpenEntry(string archiveFile, string internalPath, string? password)
    {
        try
        {
            var psi = Psi(ExtractArgs(archiveFile, internalPath, password));
            if (psi is null) return null;
            psi.RedirectStandardOutput = true;
            var p = Process.Start(psi);
            return p is null ? null : new ProcessStdoutStream(p);
        }
        catch { return null; }
    }

    public static string? Create(IReadOnlyList<(string Source, string Entry)> items, string archivePath,
        CompressionLevel level, string? password, PackProgress? progress = null)
    {
        if (progress is null)
        {
            var (ok, _, _) = Run(CreateArgs(items, archivePath, level, password), readAll: false);
            return ok ? null : "7-Zip failed to create the archive";
        }
        return CreateWatched(CreateArgs(items, archivePath, level, password, reportProgress: true), items, progress);
    }

    /// <summary>Run <c>7z a</c> with its progress output switched on and feed the overlay as it
    /// arrives. Unlike the built-in writer, 7-Zip reports one percentage for the whole run and
    /// separately logs each file as it adds it, so the byte bar is driven by the percentage and
    /// the queue rows by the logged paths — see <see cref="PackScan"/>.
    ///
    /// <para>Cancel kills the process. That leaves a part-written archive behind, which the
    /// caller deletes; there is no way to ask 7-Zip to unwind what it has already put in.</para></summary>
    private static string? CreateWatched(string[] args, IReadOnlyList<(string Source, string Entry)> items,
        PackProgress progress)
    {
        try
        {
            var psi = Psi(args);
            if (psi is null) return "7-Zip is not available";
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            using var p = Process.Start(psi);
            if (p is null) return "7-Zip failed to start";

            var scan = new PackScan(items);
            var buffer = new char[4096];
            while (true)
            {
                // Read blocks, so cancellation is noticed between chunks rather than instantly.
                // -bsp1 makes 7-Zip write its percentage continuously, which keeps those chunks
                // arriving even while it works through one large member.
                if (progress.Stop()) { Kill(p); return ArchiveVfs.CancelledError; }
                int n = p.StandardOutput.Read(buffer, 0, buffer.Length);
                if (n <= 0) break;
                scan.Feed(new string(buffer, 0, n), progress);
            }

            string errText = p.StandardError.ReadToEnd();
            p.WaitForExit();
            if (progress.IsCancelled) return ArchiveVfs.CancelledError;
            if (p.ExitCode == 0) { scan.Finish(progress); return null; }
            return errText.Contains("password", StringComparison.OrdinalIgnoreCase)
                ? "Wrong password"
                : "7-Zip failed to create the archive";
        }
        catch (Exception ex) { return ex.Message; }
    }

    private static void Kill(Process p)
    {
        try { p.Kill(entireProcessTree: true); } catch { }
    }

    /// <summary>Remove members (<c>7z d</c>). <paramref name="targets"/> are '/'-separated
    /// member paths; a directory target takes everything beneath it. The archive is listed
    /// first and the targets expanded to the exact stored paths — 7-Zip's wildcard matching
    /// changes meaning with <c>-r</c>, and an exact list can't over-match a same-named
    /// entry somewhere else in the tree. Returns an error message or null.</summary>
    public static string? Delete(string archiveFile, IReadOnlyList<string> targets, string? password)
    {
        var (ok, stdout, wrongPassword) = Run(ListArgs(archiveFile, password), readAll: true);
        if (wrongPassword) return "Wrong password";
        if (!ok) return "7-Zip could not read the archive";

        // Stored paths as 7-Zip printed them (it wants them back in the same shape);
        // matching happens on a '/'-normalized copy.
        var exact = ParseListing(stdout)
            .Select(m => (Stored: m.Path, Norm: m.Path.Replace('\\', '/').TrimEnd('/')))
            .Where(m => targets.Any(t => m.Norm.Equals(t, StringComparison.OrdinalIgnoreCase)
                                      || m.Norm.StartsWith(t + "/", StringComparison.OrdinalIgnoreCase)))
            .Select(m => m.Stored).ToList();
        if (exact.Count == 0) return "Nothing in the archive matched";

        var (delOk, _, delWrongPw) = Run(DeleteArgs(archiveFile, exact, password), readAll: false);
        if (delWrongPw) return "Wrong password";
        return delOk ? null : "7-Zip failed to modify the archive";
    }

    // ── Command lines ────────────────────────────────────────────────────
    //
    // Everything positional goes after "--". 7-Zip reads any argument starting with "-" as a
    // switch wherever it appears, and one of the positional arguments here is a *member path
    // read out of the archive* — so an archive holding a member called "-y" or "-r" would be
    // choosing 7-Zip's switches for us. "--" ends switch parsing; what follows is data.
    //
    // Built here rather than inline so the shape can be asserted in tests: this machine has no
    // 7-Zip to run, and "did we put -- in the right place" is exactly the kind of thing that
    // silently stops being true.
    private const string EndOfSwitches = "--";

    internal static string[] ListArgs(string archiveFile, string? password) =>
        ["l", "-slt", PwdArg(password), EndOfSwitches, archiveFile];

    internal static string[] ExtractArgs(string archiveFile, string internalPath, string? password) =>
        ["x", "-so", PwdArg(password), EndOfSwitches, archiveFile, internalPath.Replace('\\', '/')];

    internal static string[] DeleteArgs(string archiveFile, IReadOnlyList<string> members, string? password) =>
        [.. new[] { "d", PwdArg(password), EndOfSwitches, archiveFile }, .. members];

    /// <param name="reportProgress">Adds the two output switches the Pack overlay reads:
    /// <c>-bsp1</c> sends the run's percentage to stdout, <c>-bb1</c> logs each file as it is
    /// added. Off by default so a pack with nobody watching keeps 7-Zip quiet.</param>
    internal static string[] CreateArgs(IReadOnlyList<(string Source, string Entry)> items,
        string archivePath, CompressionLevel level, string? password, bool reportProgress = false)
    {
        // 7-Zip packs by source path; entry-name remapping isn't expressed on the CLI, so
        // v1 adds the sources as-is (flat/relative names preserved by 7z).
        var args = new List<string> { "a", $"-mx={Level(level)}" };
        if (reportProgress) { args.Add("-bsp1"); args.Add("-bb1"); }
        args.Add(PwdArg(password));
        args.Add(EndOfSwitches);
        args.Add(archivePath);
        args.AddRange(items.Select(i => i.Source));
        return [.. args];
    }

    /// <summary>Turns 7-Zip's console chatter into <see cref="PackProgress"/> calls.
    ///
    /// <para>Two things are picked out of the stream. <c>-bsp1</c> writes the run's percentage
    /// as <c>" 45% 12 + name"</c>, redrawn in place with carriage returns rather than newlines —
    /// so the text is split on both, and every token is examined rather than assuming lines.
    /// <c>-bb1</c> writes a file log, <c>"+ path"</c> for an addition and <c>"U path"</c> for an
    /// update, which is what moves the queue rows along.</para>
    ///
    /// <para>A read can end mid-token, so the trailing fragment is held over to be completed by
    /// the next one. Kept separate from the process plumbing because this machine has no 7-Zip
    /// to run: the parsing is what the tests can reach.</para></summary>
    internal sealed class PackScan
    {
        private readonly string[] _entries;
        private string _tail = "";
        private int _percent = -1;
        private int _active = -1;

        internal PackScan(IReadOnlyList<(string Source, string Entry)> items) =>
            _entries = [.. items.Select(i => i.Entry)];

        internal void Feed(string chunk, PackProgress progress)
        {
            _tail += chunk;
            // Split on both terminators; the final piece is a fragment unless the chunk ended
            // on one, and holding it back costs at most one update.
            var parts = _tail.Split('\r', '\n');
            _tail = parts[^1];
            for (int i = 0; i < parts.Length - 1; i++) Token(parts[i], progress);
        }

        private void Token(string token, PackProgress progress)
        {
            if (Percent(token) is { } pct && pct != _percent)
            {
                _percent = pct;
                progress.Percent?.Invoke(pct);
            }
            if (LoggedPath(token) is { } path && IndexOf(path) is var i && i >= 0 && i != _active)
            {
                if (_active >= 0) progress.EntryDone?.Invoke(_active, true);
                _active = i;
                progress.EntryStart?.Invoke(i, 0);
            }
        }

        /// <summary>The run ended cleanly: close out the member left mid-flight and fill the bar.
        /// Without this the last row would sit at "packing" forever, because nothing follows it
        /// in the log to displace it.</summary>
        internal void Finish(PackProgress progress)
        {
            if (_active >= 0) { progress.EntryDone?.Invoke(_active, true); _active = -1; }
            progress.Percent?.Invoke(100);
        }

        /// <summary>The leading "NN%" of a progress token, or null when there isn't one.</summary>
        internal static int? Percent(string token)
        {
            int i = 0;
            while (i < token.Length && token[i] == ' ') i++;
            int start = i;
            while (i < token.Length && char.IsAsciiDigit(token[i])) i++;
            if (i == start || i >= token.Length || token[i] != '%') return null;
            return int.TryParse(token.AsSpan(start, i - start), out var pct) ? Math.Clamp(pct, 0, 100) : null;
        }

        /// <summary>The path out of a "+ path" / "U path" file-log token. The marker may follow
        /// a percentage on the same redrawn line, so it is looked for after any leading
        /// progress rather than only at the start.</summary>
        internal static string? LoggedPath(string token)
        {
            for (int i = 0; i + 1 < token.Length; i++)
            {
                if (token[i] is not ('+' or 'U')) continue;
                if (token[i + 1] != ' ') continue;
                // A marker is a word of its own: what precedes it must be nothing or a space,
                // so a '+' inside a filename can't be read as the start of a log entry.
                if (i > 0 && token[i - 1] != ' ') continue;
                var path = token[(i + 2)..].Trim();
                return path.Length > 0 ? path : null;
            }
            return null;
        }

        /// <summary>Match a logged path back to the item that produced it. 7-Zip logs the
        /// <em>stored</em> name, so a file from a packed folder arrives as
        /// <c>folder\sub\file.txt</c> while the item is the folder — hence the match on the
        /// first segment as well as on the whole path.</summary>
        private int IndexOf(string stored)
        {
            var norm = stored.Replace('\\', '/');
            int slash = norm.IndexOf('/');
            var first = slash < 0 ? norm : norm[..slash];
            for (int i = 0; i < _entries.Length; i++)
            {
                var entry = _entries[i].Replace('\\', '/');
                if (entry.Equals(norm, StringComparison.OrdinalIgnoreCase)
                    || entry.Equals(first, StringComparison.OrdinalIgnoreCase)) return i;
            }
            return -1;
        }
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

    /// <summary>Null when 7-Zip can't be located — callers surface that as "not available"
    /// rather than falling back to a name the OS would resolve for us.</summary>
    private static ProcessStartInfo? Psi(string[] args)
    {
        if (ResolvePath() is not { } exe) return null;
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in args) if (a.Length > 0) psi.ArgumentList.Add(a);
        return psi;
    }

    private static (bool ok, string stdout, bool wrongPassword) Run(string[] args, bool readAll)
    {
        try
        {
            var psi = Psi(args);
            if (psi is null) return (false, "", false);
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
