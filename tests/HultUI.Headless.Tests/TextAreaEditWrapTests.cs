using System.Reflection;
using System.Text;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Reactivity;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// An edit wraps again only the hard lines it touched. The rows of the lines before it are kept
/// as they were, and those of the lines after it are moved by the characters it added or took
/// away, in the list the area keeps (a line's breaks depend on its own text, the width, the
/// style's metrics, the render context and the UI culture, and nothing else). These hold that
/// list to the one a fresh area works out from scratch, row for row, after every step of a long
/// run of edits made the ways an area is edited — typing, Enter, Backspace and Delete, a
/// selection typed over, a paste of several lines, undo and redo, a new text from the parent —
/// with widths and font sizes changing in between, over real fonts and text that is anything
/// but plain: surrogate pairs, emoji joined with ZWJ, flags, variation selectors, CJK, Hangul,
/// Hebrew and Arabic, combining marks, Thai and Devanagari clusters, soft hyphens, tabs, CRLF
/// line ends, and runs with nowhere to break.
/// </summary>
public class TextAreaEditWrapTests
{
    private static string U(params int[] codePoints) => string.Concat(codePoints.Select(char.ConvertFromUtf32));

    private static readonly string[] Pieces =
    [
        "the", "a", "wrap", "of", "measure", "paragraph", "x", "AV", "office", "WWWW", "iiii", "1234",
        "supercalifragilisticexpialidocious",
        U(0x63, 0x61, 0x66, 0xE9),                                  // café, precomposed
        U(0x63, 0x61, 0x66, 0x65, 0x301),                           // café, a combining acute
        U(0x61, 0x308, 0x323, 0x6F, 0x302),                         // two marks on a, one on o
        U(0x6771, 0x4EAC),                                          // 東京
        U(0x6F22, 0x5B57, 0x304B, 0x306A, 0x4EA4, 0x3058, 0x308A, 0x6587), // 漢字かな交じり文
        U(0xD55C, 0xAD6D, 0xC5B4),                                  // 한국어
        U(0x1100, 0x1161, 0x11A8),                                  // a syllable in jamo
        U(0x1F600),                                                 // an emoji: a surrogate pair
        U(0x1F469, 0x200D, 0x1F4BB),                                // woman technologist (ZWJ)
        U(0x1F1F8, 0x1F1EA),                                        // a flag
        U(0x2764, 0xFE0F),                                          // a heart with VS16
        U(0x1F44D, 0x1F3FD),                                        // a skin tone
        U(0x5E9, 0x5DC, 0x5D5, 0x5DD), U(0x5E2, 0x5D5, 0x5DC, 0x5DD),       // Hebrew
        U(0x645, 0x631, 0x62D, 0x628, 0x627), U(0x628, 0x627, 0x644, 0x639, 0x627, 0x644, 0x645), // Arabic
        U(0x395, 0x3BB, 0x3BB, 0x3B7, 0x3BD, 0x3B9, 0x3BA, 0x3AC),  // Greek
        U(0xE2A, 0xE27, 0xE31, 0xE2A, 0xE14, 0xE35),                // Thai
        U(0x928, 0x92E, 0x938, 0x94D, 0x924, 0x947),                // Devanagari
        "co" + U(0xAD) + "op" + U(0xAD) + "er" + U(0xAD) + "a" + U(0xAD) + "tion", // soft hyphens
        "a" + U(0x9) + "b", U(0x9),                                 // tabs
        U(0x2014), U(0x2192), U(0x2026),                            // dash, arrow, ellipsis
    ];

    private static string Piece(Random rng) => rng.Next(14) switch
    {
        0 => new string('m', rng.Next(20, 160)),                                   // nowhere to break
        1 => string.Concat(Enumerable.Repeat(U(0x1F469, 0x200D, 0x1F4BB), rng.Next(3, 25))),
        _ => Pieces[rng.Next(Pieces.Length)],
    };

    private static string Words(Random rng, int count)
    {
        var sb = new StringBuilder();
        for (int w = 0; w < count; w++)
        {
            if (w > 0) sb.Append(rng.Next(10) switch { 0 => "", 1 => "  ", _ => " " });
            sb.Append(Piece(rng));
        }
        return sb.ToString();
    }

    private static string LineEnd(Random rng) => rng.Next(6) == 0 ? "\r\n" : "\n";

    private static string Document(Random rng, int lines)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < lines; i++)
        {
            if (i > 0) sb.Append(LineEnd(rng));
            sb.Append(Words(rng, rng.Next(4) switch { 0 => 0, 1 => rng.Next(1, 5), _ => rng.Next(5, 40) }));
        }
        return sb.ToString();
    }

    // Several lines, as pasted.
    private static string Lines(Random rng, int lines)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < lines; i++)
        {
            if (i > 0) sb.Append(LineEnd(rng));
            sb.Append(Words(rng, rng.Next(0, 25)));
        }
        return sb.ToString();
    }

    // A place in the text that is not inside a surrogate pair, as every caret is.
    private static int Boundary(string text, int at)
    {
        at = Math.Clamp(at, 0, text.Length);
        if (at > 0 && at < text.Length && char.IsLowSurrogate(text[at]) && char.IsHighSurrogate(text[at - 1])) at--;
        return at;
    }

    [Fact]
    public void AfterEveryEdit_TheLineStartsAreTheOnesAFreshAreaWorksOut() => Run(seed: 20261012, edits: 1_000);

    [Fact]
    public void AfterEveryEdit_TheLineStartsAreTheOnesAFreshAreaWorksOut_InOtherDocuments() => Run(seed: 7, edits: 500);

    private static void Run(int seed, int edits)
    {
        using var session = HeadlessSession.Create(900, 600);
        var rng = new Random(seed);
        var editor = new Editor { Body = Document(rng, 40) };
        session.Window.SetRoot(editor.Mount());
        session.PumpFrames(2);
        var area = editor.Area.Current!;
        area.Focus();
        session.PumpFrame();

        var contextField = typeof(TextAreaElement).GetField("_renderCtx", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var anchorField = typeof(TextAreaElement).GetField("_selectionStart", BindingFlags.NonPublic | BindingFlags.Instance)!;
        float[] widths = [233.3f, 233.3f, 233.3f, 90f, 37f, 480f, 2400f];
        float[] sizes = [13f, 13f, 13f, 17f, 10.5f];
        int wrapped = 0, edited = 0;
        var did = new Dictionary<string, int>();

        for (int edit = 0; edit < edits; edit++)
        {
            Assert.Same(area, editor.Area.Current);
            string text = area.Text;
            int at = Boundary(text, rng.Next(text.Length + 1));
            int end = Boundary(text, at + rng.Next(0, 30));
            string kind;
            bool sameText = false;
            switch (rng.Next(22))
            {
                case 20:
                    kind = "a new text from the parent, its ends changed";
                    // As a response sent again: a new first and last line, the lines between
                    // the same text at other places.
                    editor.Body = Words(rng, rng.Next(1, 30)) + text[Math.Max(0, text.IndexOf('\n'))..Math.Max(0, text.LastIndexOf('\n') + 1)]
                        + Words(rng, rng.Next(1, 30));
                    editor.Rebuild();
                    break;
                case 21:
                    kind = "a new text from the parent, its halves swapped";
                    int middle = text.IndexOf('\n', text.Length / 2);
                    editor.Body = middle < 0 ? Lines(rng, 3) : string.Concat(text.AsSpan(middle + 1), "\n", text.AsSpan(0, middle));
                    editor.Rebuild();
                    break;
                case 0 or 1 or 2 or 3:
                    kind = "type";
                    area.CursorPosition = at;
                    EventDispatcher.Dispatch(new TextInputEvent { Text = rng.Next(3) == 0 ? " " : Piece(rng) }, area);
                    break;
                case 4:
                    kind = "type over a selection";
                    area.CursorPosition = end;
                    anchorField.SetValue(area, at);
                    EventDispatcher.Dispatch(new TextInputEvent { Text = Piece(rng) }, area);
                    break;
                case 5 or 6:
                    kind = "enter";
                    area.CursorPosition = at;
                    session.Window.PressKey(KeyCode.Enter);
                    break;
                case 7 or 8:
                    kind = "backspace";
                    area.CursorPosition = at;
                    session.Window.PressKey(KeyCode.Backspace, rng.Next(4) == 0 ? KeyModifiers.Ctrl : KeyModifiers.None);
                    break;
                case 9:
                    kind = "delete";
                    area.CursorPosition = at;
                    session.Window.PressKey(KeyCode.Delete, rng.Next(4) == 0 ? KeyModifiers.Ctrl : KeyModifiers.None);
                    break;
                case 10:
                    kind = "delete a selection";
                    area.CursorPosition = Boundary(text, at + rng.Next(0, 400));
                    anchorField.SetValue(area, at);
                    session.Window.PressKey(KeyCode.Backspace);
                    break;
                case 11:
                    kind = "paste lines";
                    session.Window.Clipboard.Text = Lines(rng, rng.Next(1, 6));
                    area.CursorPosition = end;
                    anchorField.SetValue(area, rng.Next(2) == 0 ? at : end);
                    session.Window.PressKey(KeyCode.V, KeyModifiers.Ctrl);
                    break;
                case 12 or 13:
                    kind = "undo";
                    for (int k = rng.Next(1, 4); k > 0; k--) session.Window.PressKey(KeyCode.Z, KeyModifiers.Ctrl);
                    break;
                case 14:
                    kind = "redo";
                    for (int k = rng.Next(1, 3); k > 0; k--) session.Window.PressKey(KeyCode.Y, KeyModifiers.Ctrl);
                    break;
                case 15:
                    kind = "a new text from the parent";
                    // Lines in the middle replaced; the ends are the text's own.
                    editor.Body = string.Concat(text.AsSpan(0, at), Lines(rng, rng.Next(0, 4)), text.AsSpan(end));
                    editor.Rebuild();
                    break;
                case 16:
                    kind = "the same text from the parent";
                    sameText = true;
                    editor.Body = new StringBuilder(text).ToString();
                    editor.Rebuild();
                    break;
                case 17:
                    kind = "width";
                    editor.Width = widths[rng.Next(widths.Length)];
                    editor.Rebuild();
                    break;
                case 18:
                    kind = "font size";
                    editor.Size = sizes[rng.Next(sizes.Length)];
                    editor.Rebuild();
                    break;
                default:
                    kind = "font";
                    editor.Family = editor.Family is null ? FontFamilies.Monospace : null;
                    editor.Rebuild();
                    break;
            }
            int passes = area.LineStartPasses;
            session.PumpFrame();
            if (sameText)
                Assert.True(passes == area.LineStartPasses, "a rebuild that handed the area its own text again worked its line starts out again");
            else if (area.LineStartPasses > passes && area.Text != text)
                edited++;
            did[kind] = did.GetValueOrDefault(kind) + 1;

            // The list the frame drew by, not one worked out for this comparison.
            float width = area.Layout.AbsoluteContentBox.Width;
            var style = area.ResolvedInherited;
            passes = area.LineStartPasses;
            var actual = area.GetLineStarts(width, style);
            Assert.Equal(passes, area.LineStartPasses);

            var fresh = TextAreaElement.New(area.Text);
            contextField.SetValue(fresh, contextField.GetValue(area));
            var expected = fresh.GetLineStarts(width, style);
            Assert.True(expected.SequenceEqual(actual), $"edit {edit} ({kind}): " + Describe(area.Text, width, expected, actual));
            wrapped += expected.Count - area.Text.AsSpan().Count('\n') - 1;
        }

        // The run did what it is for: long lines wrapped, and most steps were edits of a text
        // whose list was there to patch.
        Assert.True(wrapped > 20 * edits, $"only {wrapped} wrapped rows over {edits} edits");
        Assert.True(edited > edits / 2, $"only {edited} of {edits} steps edited the text: " + string.Join(", ", did));
    }

    private static string Describe(string text, float width, List<int> expected, List<int> actual)
    {
        int k = 0;
        while (k < expected.Count && k < actual.Count && expected[k] == actual[k]) k++;
        int from = k > 0 ? expected[k - 1] : 0;
        return $"{expected.Count} rows expected, {actual.Count} worked out; at width {width}, row {k} from {from}: "
            + $"expected {(k < expected.Count ? expected[k] : -1)}, got {(k < actual.Count ? actual[k] : -1)} "
            + $"in \"{text[from..Math.Min(text.Length, from + 60)]}\"";
    }

    /// <summary>A real component, because only reconciliation runs <c>PatchFrom</c>; it keeps
    /// what the area was edited to, as an app does, so a rebuild hands that text back.</summary>
    private sealed class Editor : Component
    {
        public string Body = "";
        public float Size = 13f;
        public float Width = 233.3f;
        public string? Family;
        public Ref<TextAreaElement> Area { get; } = new();

        public override Element Build()
        {
            var area = TextArea.New(Body).FontSize(Size).Width(Width).Height(400).FlexShrink(0)
                .OnTextChanged(t => Body = t);
            if (Family is not null) area.FontFamily(Family);
            return Flex.Row(gap: 0).FlexGrow(1).Children(area.WithRef(Area));
        }

        public void Rebuild() => MarkDirty();
    }
}
