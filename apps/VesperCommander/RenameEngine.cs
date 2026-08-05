using System.Text;
using System.Text.RegularExpressions;

namespace VesperCommander;

/// <summary>Batch-rename engine (multi-rename tool). Pure and unit-testable: token expansion,
/// the fixed pipeline (expand → find/replace → case → trim), the counter, and conflict
/// classification produce a <see cref="Preview"/> from a set of files + <see cref="Options"/>.
/// The only I/O is <see cref="Apply"/>, which renames via temp names so a permutation
/// (a↔b swaps) never collides mid-batch. Completed renames are never rolled back.</summary>
public static class RenameEngine
{
    public enum CaseMode { AsIs, Upper, Lower, Title, FirstCap }

    public sealed record Options(
        string NameMask = "[N]", string ExtMask = "",
        string Find = "", string Replace = "", bool MatchCase = false, bool Regex = false,
        CaseMode Case = CaseMode.AsIs, bool TrimEnds = false, bool CollapseSpaces = false,
        int CounterStart = 1, int CounterStep = 1, int CounterDigits = 3);

    /// <summary>Ok = will rename; the rest block or skip.</summary>
    public enum Status { Ok, Unchanged, Collision, Exists, Illegal, Empty, TooLong }

    public sealed record Row(string OldName, string OldFullPath, string NewName, Status Status)
    {
        public bool IsConflict => Status is Status.Collision or Status.Exists or Status.Illegal or Status.Empty or Status.TooLong;
    }

    public sealed record Preview(IReadOnlyList<Row> Rows, int WillRename, int Conflicts, int Unchanged, string? RegexError);

    private const int MaxNameLen = 255;
    private static readonly char[] Invalid = Path.GetInvalidFileNameChars();

    // Whether the counter is dormant (no [C] in either mask) — the UI dims it.
    public static bool UsesCounter(Options o) =>
        HasCounterToken(o.NameMask) || HasCounterToken(o.ExtMask);

    private static bool HasCounterToken(string mask) =>
        Regex.IsMatch(mask, @"\[[Cc](\s*:\s*\d+)?\]");

    // ── Token expansion ──────────────────────────────────────────────────
    /// <summary>Expand a mask against one file's parts. Tokens: [N] whole name · [N a-b] /
    /// [N a-] char ranges (1-based, clamped) · [E] extension · [C] / [C:d] counter · [P]
    /// parent folder · [[ ]] escape to literal brackets. Unknown / malformed tokens pass
    /// through literally.</summary>
    public static string Expand(string mask, string name, string ext, string parent, int counter, int counterDigits)
    {
        var sb = new StringBuilder(mask.Length + 8);
        int i = 0;
        while (i < mask.Length)
        {
            char c = mask[i];
            if (c == '[')
            {
                if (i + 1 < mask.Length && mask[i + 1] == '[') { sb.Append('['); i += 2; continue; }
                int close = mask.IndexOf(']', i + 1);
                if (close < 0) { sb.Append(c); i++; continue; }        // unclosed → literal '['
                sb.Append(ExpandToken(mask.Substring(i + 1, close - i - 1), name, ext, parent, counter, counterDigits));
                i = close + 1;
            }
            else if (c == ']' && i + 1 < mask.Length && mask[i + 1] == ']') { sb.Append(']'); i += 2; }
            else { sb.Append(c); i++; }
        }
        return sb.ToString();
    }

    private static string ExpandToken(string tok, string name, string ext, string parent, int counter, int counterDigits)
    {
        var t = tok.Trim();
        if (t.Length == 0) return "[]";
        switch (char.ToUpperInvariant(t[0]))
        {
            case 'N':
                if (t.Length == 1) return name;
                var m = Regex.Match(t, @"^[Nn]\s*(\d+)\s*-\s*(\d*)$");
                if (m.Success)
                {
                    int start = Math.Clamp(int.Parse(m.Groups[1].Value) - 1, 0, name.Length);
                    int end = m.Groups[2].Value.Length > 0
                        ? Math.Clamp(int.Parse(m.Groups[2].Value), 0, name.Length) : name.Length;
                    return end > start ? name[start..end] : "";
                }
                return name;
            case 'E': return ext;
            case 'P': return parent;
            case 'C':
                int digits = counterDigits;
                var cm = Regex.Match(t, @"^[Cc]\s*:\s*(\d+)$");
                if (cm.Success) digits = int.Parse(cm.Groups[1].Value);
                return counter.ToString().PadLeft(Math.Max(1, digits), '0');
            default: return "[" + tok + "]";   // unknown → literal passthrough
        }
    }

    // ── Pipeline ─────────────────────────────────────────────────────────
    private static string NewName(string originalName, int index, Options o, string parent, Regex? rx)
    {
        var (stem, ext) = SplitName(originalName);
        int counter = o.CounterStart + index * o.CounterStep;

        string namePart = Expand(o.NameMask, stem, ext, parent, counter, o.CounterDigits);
        string extPart = o.ExtMask.Length == 0 ? ext : Expand(o.ExtMask, stem, ext, parent, counter, o.CounterDigits);

        // find/replace on the name part (extension preserved), then case, then trim.
        if (o.Find.Length > 0)
            namePart = rx != null ? rx.Replace(namePart, o.Replace ?? "")
                                  : namePart.Replace(o.Find, o.Replace ?? "",
                                        o.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

        namePart = ApplyCase(namePart, o.Case);
        if (o.TrimEnds) namePart = namePart.Trim();
        if (o.CollapseSpaces) namePart = Regex.Replace(namePart, "  +", " ");

        return extPart.Length > 0 ? namePart + "." + extPart : namePart;
    }

    /// <summary>Split "a.b.jpg" into stem "a.b" + extension "jpg" (no dot). A leading-dot
    /// name (".gitignore") is all stem, no extension.</summary>
    private static (string stem, string ext) SplitName(string name)
    {
        int dot = name.LastIndexOf('.');
        return dot <= 0 ? (name, "") : (name[..dot], name[(dot + 1)..]);
    }

    private static string ApplyCase(string s, CaseMode mode) => mode switch
    {
        CaseMode.Upper => s.ToUpperInvariant(),
        CaseMode.Lower => s.ToLowerInvariant(),
        CaseMode.Title => TitleCase(s),
        CaseMode.FirstCap => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant(),
        _ => s,
    };

    private static string TitleCase(string s)
    {
        var sb = new StringBuilder(s.Length);
        bool boundary = true;
        foreach (var ch in s)
        {
            sb.Append(boundary ? char.ToUpperInvariant(ch) : char.ToLowerInvariant(ch));
            boundary = !char.IsLetterOrDigit(ch);
        }
        return sb.ToString();
    }

    // ── Preview (expansion + conflict classification) ────────────────────
    /// <summary>Compute the full old→new mapping + conflict classes for a batch. On a
    /// malformed regex, returns an empty preview carrying <see cref="Preview.RegexError"/> so
    /// the UI can hold the last valid mapping and disable Rename.</summary>
    public static Preview Build(IReadOnlyList<(string Name, string FullPath)> files, Options o)
    {
        Regex? rx = null;
        if (o.Regex && o.Find.Length > 0)
        {
            try { rx = new Regex(o.Find, o.MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase); }
            catch (ArgumentException ex) { return new Preview([], 0, 0, 0, ex.Message); }
        }

        var newNames = new string[files.Count];
        for (int i = 0; i < files.Count; i++)
        {
            string parent = new DirectoryInfo(Path.GetDirectoryName(files[i].FullPath) ?? "").Name;
            newNames[i] = NewName(files[i].Name, i, o, parent, rx);
        }

        // Intra-batch collisions (case-insensitive) — count non-empty results.
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in newNames) if (n.Length > 0) counts[n] = counts.GetValueOrDefault(n) + 1;
        // A batch member's own current name (so a target vacated by the batch isn't "Exists").
        var batchNames = new HashSet<string>(files.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);

        var rows = new List<Row>(files.Count);
        int will = 0, conflicts = 0, unchanged = 0;
        for (int i = 0; i < files.Count; i++)
        {
            string nn = newNames[i];
            Status st;
            if (nn.Length == 0) st = Status.Empty;
            else if (nn.IndexOfAny(Invalid) >= 0) st = Status.Illegal;
            else if (nn.Length > MaxNameLen) st = Status.TooLong;
            else if (string.Equals(nn, files[i].Name, StringComparison.OrdinalIgnoreCase)) st = Status.Unchanged;
            else if (counts[nn] > 1) st = Status.Collision;
            else if (ExistsBlocking(files[i].FullPath, nn, batchNames)) st = Status.Exists;
            else st = Status.Ok;

            if (st == Status.Ok) will++;
            else if (st == Status.Unchanged) unchanged++;
            else conflicts++;
            rows.Add(new Row(files[i].Name, files[i].FullPath, nn, st));
        }
        return new Preview(rows, will, conflicts, unchanged, null);
    }

    /// <summary>The target exists on disk and is neither the file itself nor another batch
    /// member being renamed away (those are resolved by the two-phase apply).</summary>
    private static bool ExistsBlocking(string oldFull, string newName, HashSet<string> batchNames)
    {
        var dir = Path.GetDirectoryName(oldFull) ?? "";
        var target = Path.Combine(dir, newName);
        if (string.Equals(target, oldFull, StringComparison.OrdinalIgnoreCase)) return false;   // case-only self
        if (batchNames.Contains(newName)) return false;   // occupied by a batch member (two-phase clears it)
        return File.Exists(target) || Directory.Exists(target);
    }

    // ── Apply (two-phase) ────────────────────────────────────────────────
    /// <summary>Rename the Ok rows via unique temp names first, then to their targets, so a
    /// permutation never collides. Returns a per-row error (null = success). A row that fails
    /// phase 2 is restored to its original name; successful renames are kept.</summary>
    public static IReadOnlyList<(Row Row, string? Error)> Apply(IReadOnlyList<Row> rows)
    {
        var todo = rows.Where(r => r.Status == Status.Ok).ToList();
        var results = new List<(Row, string?)>(todo.Count);
        var temps = new List<(Row row, string temp)>();

        // Phase 1 → temp names.
        for (int i = 0; i < todo.Count; i++)
        {
            var r = todo[i];
            var dir = Path.GetDirectoryName(r.OldFullPath) ?? "";
            var temp = Path.Combine(dir, $"__vesper_rn_{i}__");
            try
            {
                if (Directory.Exists(r.OldFullPath)) Directory.Move(r.OldFullPath, temp);
                else File.Move(r.OldFullPath, temp);
                temps.Add((r, temp));
            }
            catch (Exception ex) { results.Add((r, ex.Message)); }
        }

        // Phase 2 → final targets.
        foreach (var (r, temp) in temps)
        {
            var dir = Path.GetDirectoryName(r.OldFullPath) ?? "";
            var target = Path.Combine(dir, r.NewName);
            try
            {
                if (Directory.Exists(temp)) Directory.Move(temp, target);
                else File.Move(temp, target);
                results.Add((r, null));
            }
            catch (Exception ex)
            {
                try { if (Directory.Exists(temp)) Directory.Move(temp, r.OldFullPath); else File.Move(temp, r.OldFullPath); }
                catch { /* leave at temp if even the restore fails */ }
                results.Add((r, ex.Message));
            }
        }
        return results;
    }
}
