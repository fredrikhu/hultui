using System.Text;

namespace GitClient.Git;

/// <summary>How a single conflict region is resolved.</summary>
public enum ConflictChoice
{
    Unresolved,
    Ours,
    Theirs,
    /// <summary>Keep both sides, ours first.</summary>
    Both,
    /// <summary>Keep both sides, theirs first.</summary>
    BothReversed,
    /// <summary>Hand-edited text (see the manual-text map).</summary>
    Manual,
}

/// <summary>
/// One conflicted region of a file: the two (or three, with diff3 style) competing
/// bodies git wrote between its markers.
/// </summary>
public sealed record ConflictHunk(
    int Index,
    IReadOnlyList<string> Ours,
    IReadOnlyList<string> Base,
    IReadOnlyList<string> Theirs,
    string OursLabel,
    string TheirsLabel,
    // git labels the ancestor section with the merge base's short hash
    // (e.g. "||||||| 4374b94"), not a fixed string — keep it so an unresolved
    // hunk re-renders byte-identically.
    string BaseLabel = "",
    bool SawBaseMarker = false)
{
    /// <summary>True when git used diff3/zdiff3 style and recorded the common ancestor.</summary>
    public bool HasBase => SawBaseMarker;
}

/// <summary>A run of the file: either unconflicted context, or a conflict region.</summary>
public sealed record ConflictSegment(IReadOnlyList<string>? Context, ConflictHunk? Hunk);

/// <summary>
/// A conflicted working-tree file parsed into context runs and conflict regions,
/// able to render itself back out with per-region resolutions applied. Parsing the
/// working file (rather than the index stages) preserves everything git already
/// merged cleanly — only the marked regions are up to the user.
/// </summary>
public sealed class ConflictDocument
{
    public IReadOnlyList<ConflictSegment> Segments { get; }
    public IReadOnlyList<ConflictHunk> Hunks { get; }
    /// <summary>The file's dominant line ending, reproduced on render.</summary>
    public string NewLine { get; }
    /// <summary>Whether the original file ended with a newline.</summary>
    public bool TrailingNewline { get; }

    private ConflictDocument(IReadOnlyList<ConflictSegment> segments, string newLine, bool trailingNewline)
    {
        Segments = segments;
        Hunks = segments.Where(s => s.Hunk != null).Select(s => s.Hunk!).ToList();
        NewLine = newLine;
        TrailingNewline = trailingNewline;
    }

    public bool HasConflicts => Hunks.Count > 0;

    /// <summary>
    /// Parses conflict markers. Handles both the default 2-way style and diff3/zdiff3
    /// (which adds a <c>|||||||</c> common-ancestor section).
    /// </summary>
    public static ConflictDocument Parse(string text)
    {
        var newLine = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        bool trailing = text.EndsWith('\n');

        var raw = text.Split('\n');
        // A trailing newline yields a final empty element that isn't a real line.
        int count = trailing && raw.Length > 0 ? raw.Length - 1 : raw.Length;

        var segments = new List<ConflictSegment>();
        var context = new List<string>();
        List<string> ours = [], baseLines = [], theirs = [];
        string oursLabel = "", theirsLabel = "", baseLabel = "";
        bool sawBase = false;
        int state = 0;   // 0 = context, 1 = ours, 2 = base, 3 = theirs
        int index = 0;

        void FlushContext()
        {
            if (context.Count == 0) return;
            segments.Add(new ConflictSegment(context.ToList(), null));
            context.Clear();
        }

        for (int i = 0; i < count; i++)
        {
            var line = raw[i].TrimEnd('\r');

            if (state == 0 && IsMarker(line, '<'))
            {
                FlushContext();
                oursLabel = LabelOf(line);
                ours = []; baseLines = []; theirs = [];
                baseLabel = ""; sawBase = false;
                state = 1;
                continue;
            }
            if (state == 1 && IsMarker(line, '|'))
            {
                baseLabel = LabelOf(line);
                sawBase = true;
                state = 2;
                continue;
            }
            if ((state == 1 || state == 2) && IsMarker(line, '='))
            {
                state = 3;
                continue;
            }
            if (state == 3 && IsMarker(line, '>'))
            {
                theirsLabel = LabelOf(line);
                segments.Add(new ConflictSegment(null,
                    new ConflictHunk(index++, ours, baseLines, theirs, oursLabel, theirsLabel,
                        baseLabel, sawBase)));
                state = 0;
                continue;
            }

            switch (state)
            {
                case 0: context.Add(line); break;
                case 1: ours.Add(line); break;
                case 2: baseLines.Add(line); break;
                case 3: theirs.Add(line); break;
            }
        }

        // An unterminated block (truncated/hand-mangled file): fall back to treating
        // what we captured as plain context so nothing is silently dropped.
        if (state != 0)
        {
            context.AddRange(ours);
            context.AddRange(baseLines);
            context.AddRange(theirs);
        }
        FlushContext();

        return new ConflictDocument(segments, newLine, trailing);
    }

    /// <summary>The lines a given choice resolves to (manual text supplied separately).</summary>
    public static IReadOnlyList<string> Resolve(ConflictHunk h, ConflictChoice choice, string? manualText)
    {
        switch (choice)
        {
            case ConflictChoice.Ours: return h.Ours;
            case ConflictChoice.Theirs: return h.Theirs;
            case ConflictChoice.Both: return [.. h.Ours, .. h.Theirs];
            case ConflictChoice.BothReversed: return [.. h.Theirs, .. h.Ours];
            case ConflictChoice.Manual:
                if (string.IsNullOrEmpty(manualText)) return [];
                var body = manualText.Replace("\r\n", "\n");
                if (body.EndsWith('\n')) body = body[..^1];
                return body.Split('\n');
            default: return h.Ours;
        }
    }

    /// <summary>
    /// Renders the file with the given per-hunk resolutions. Hunks with no entry (or
    /// <see cref="ConflictChoice.Unresolved"/>) are written back out with their
    /// original markers, so a partial save stays a valid conflicted file.
    /// </summary>
    public string Render(
        IReadOnlyDictionary<int, ConflictChoice> choices,
        IReadOnlyDictionary<int, string>? manual = null)
    {
        var outLines = new List<string>();
        foreach (var seg in Segments)
        {
            if (seg.Context is { } ctx) { outLines.AddRange(ctx); continue; }
            var h = seg.Hunk!;
            var choice = choices.GetValueOrDefault(h.Index, ConflictChoice.Unresolved);
            if (choice == ConflictChoice.Unresolved)
            {
                // Re-emit the original block verbatim.
                outLines.Add("<<<<<<< " + h.OursLabel);
                outLines.AddRange(h.Ours);
                if (h.HasBase)
                {
                    outLines.Add(h.BaseLabel.Length > 0 ? "||||||| " + h.BaseLabel : "|||||||");
                    outLines.AddRange(h.Base);
                }
                outLines.Add("=======");
                outLines.AddRange(h.Theirs);
                outLines.Add(">>>>>>> " + h.TheirsLabel);
                continue;
            }
            outLines.AddRange(Resolve(h, choice, manual?.GetValueOrDefault(h.Index)));
        }

        var sb = new StringBuilder();
        for (int i = 0; i < outLines.Count; i++)
        {
            sb.Append(outLines[i]);
            if (i < outLines.Count - 1 || TrailingNewline) sb.Append(NewLine);
        }
        return sb.ToString();
    }

    // Markers are 7 identical characters, optionally followed by a space + label.
    private static bool IsMarker(string line, char c)
    {
        if (line.Length < 7) return false;
        for (int i = 0; i < 7; i++) if (line[i] != c) return false;
        return line.Length == 7 || line[7] == ' ';
    }

    private static string LabelOf(string line) => line.Length > 8 ? line[8..].Trim() : "";
}
