namespace VesperCommander;

/// <summary>Compares two directory trees for the F9 synchronize view. Aligns files by their
/// path relative to each root and classifies the relationship (only-one-side / newer / equal /
/// conflict). Pure apart from filesystem reads and unit-testable via <see cref="Classify"/>.
/// The actual transfer is the app's existing copy pipeline — this stops at the row list.</summary>
public static class SyncEngine
{
    public enum Rel { OnlyLeft, OnlyRight, LeftNewer, RightNewer, Equal, Conflict }
    public enum CompareMode { DateSize, Content }

    public sealed record Row(
        string RelPath, string Name, Rel Rel,
        long LeftSize, DateTime LeftTime, bool LeftExists,
        long RightSize, DateTime RightTime, bool RightExists);

    private const double MTimeToleranceSeconds = 2.0;

    /// <summary>Classify one aligned pair. Pure — the inputs are the two sides' metadata
    /// (and, in content mode, whether the bytes are equal). Used directly by tests.</summary>
    public static Rel Classify(
        bool leftExists, long leftSize, DateTime leftTime,
        bool rightExists, long rightSize, DateTime rightTime,
        CompareMode mode, bool? contentEqual)
    {
        if (leftExists && !rightExists) return Rel.OnlyLeft;
        if (!leftExists && rightExists) return Rel.OnlyRight;

        if (mode == CompareMode.Content)
            return contentEqual == true ? Rel.Equal : Rel.Conflict;

        // Date + size.
        bool sameSize = leftSize == rightSize;
        double dt = (leftTime - rightTime).TotalSeconds;
        if (sameSize && Math.Abs(dt) <= MTimeToleranceSeconds) return Rel.Equal;
        if (dt > MTimeToleranceSeconds) return Rel.LeftNewer;
        if (dt < -MTimeToleranceSeconds) return Rel.RightNewer;
        return Rel.Conflict;   // same timestamp, different size
    }

    public static Task<List<Row>> Compare(string left, string right, CompareMode mode, bool recurse,
        Action<int, string>? onProgress, System.Threading.CancellationToken ct)
    {
        return Task.Run(() =>
        {
            var leftFiles = new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);
            var rightFiles = new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);
            CollectFiles(left, left, recurse, leftFiles, ct);
            CollectFiles(right, right, recurse, rightFiles, ct);

            var rels = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var k in leftFiles.Keys) rels.Add(k);
            foreach (var k in rightFiles.Keys) rels.Add(k);

            var rows = new List<Row>();
            int done = 0;
            foreach (var rel in rels)
            {
                if (ct.IsCancellationRequested) break;
                leftFiles.TryGetValue(rel, out var lf);
                rightFiles.TryGetValue(rel, out var rf);
                bool le = lf != null, re = rf != null;
                long ls = lf?.Length ?? 0, rs = rf?.Length ?? 0;
                DateTime lt = lf?.LastWriteTime ?? default, rt = rf?.LastWriteTime ?? default;

                bool? contentEqual = null;
                if (mode == CompareMode.Content && le && re)
                    contentEqual = ls == rs && FilesEqual(lf!.FullName, rf!.FullName);

                var relKind = Classify(le, ls, lt, re, rs, rt, mode, contentEqual);
                rows.Add(new Row(rel, Path.GetFileName(rel), relKind, ls, lt, le, rs, rt, re));
                onProgress?.Invoke(++done, rel);
            }
            return rows;
        }, ct);
    }

    private static void CollectFiles(string dir, string root, bool recurse,
        Dictionary<string, FileInfo> map, System.Threading.CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return;
        DirectoryInfo di;
        try { di = new DirectoryInfo(dir); if (!di.Exists) return; }
        catch { return; }

        try
        {
            foreach (var f in di.EnumerateFiles())
            {
                string rel = Path.GetRelativePath(root, f.FullName);
                map[rel] = f;
            }
        }
        catch { /* denied — skip this folder's files */ }

        if (!recurse) return;
        try
        {
            foreach (var sub in di.EnumerateDirectories())
            {
                if (ct.IsCancellationRequested) return;
                CollectFiles(sub.FullName, root, recurse, map, ct);
            }
        }
        catch { /* denied */ }
    }

    private static bool FilesEqual(string a, string b)
    {
        try
        {
            using var fa = File.OpenRead(a);
            using var fb = File.OpenRead(b);
            var ba = new byte[64 * 1024];
            var bb = new byte[64 * 1024];
            int na;
            while ((na = fa.Read(ba, 0, ba.Length)) > 0)
            {
                int nb = 0, off = 0;
                while (off < na && (nb = fb.Read(bb, off, na - off)) > 0) off += nb;
                if (off != na) return false;
                for (int i = 0; i < na; i++) if (ba[i] != bb[i]) return false;
            }
            return fb.ReadByte() == -1;   // b must also be at EOF
        }
        catch { return false; }
    }
}
