using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using VesperCommander.Theme;

namespace VesperCommander;

/// <summary>
/// A tiny, language-agnostic syntax tinter for the preview excerpt — enough to
/// give code the design's look (keywords accent-400, types accent-300, comments
/// neutral-600, strings soft green). Heuristic, not a real parser: it scans
/// comments, strings, and identifiers and colours keywords + Capitalized words.
/// </summary>
public static class SyntaxHighlight
{
    private static readonly Color StringColor = Color.FromHex("#8fd0b0");

    // A broad set spanning the common languages a dev browses (C#, Rust, JS/TS,
    // Python, Go). Matched case-sensitively.
    private static readonly HashSet<string> Keywords = new(System.StringComparer.Ordinal)
    {
        // C#
        "public","private","protected","internal","class","interface","struct","enum","namespace",
        "using","void","var","new","this","base","override","virtual","abstract","sealed","readonly",
        "get","set","static","async","await","return","if","else","for","foreach","while","do","switch",
        "case","break","continue","try","catch","finally","throw","null","true","false","const","in","is","as",
        // Rust
        "fn","let","mut","impl","trait","mod","use","pub","match","loop","move","ref","dyn","self","where","unsafe",
        // JS/TS
        "function","export","import","from","default","extends","typeof","instanceof","yield","of","let",
        // Python
        "def","lambda","None","True","False","elif","import","class","pass","with","yield","not","and","or","nil",
        // Go
        "func","package","type","interface","chan","go","defer","range","map","struct",
    };

    public static IReadOnlyList<TextSpan> Spans(string code)
    {
        var spans = new List<TextSpan>();
        int i = 0, n = code.Length;
        while (i < n)
        {
            char ch = code[i];

            if (ch == '/' && i + 1 < n && code[i + 1] == '/')          // line comment
            {
                int start = i;
                while (i < n && code[i] != '\n') i++;
                spans.Add(new TextSpan(start, i - start, Nocturne.Neutral600));
                continue;
            }
            if (ch == '/' && i + 1 < n && code[i + 1] == '*')          // block comment
            {
                int start = i; i += 2;
                while (i < n && !(code[i] == '*' && i + 1 < n && code[i + 1] == '/')) i++;
                if (i < n) i += 2;
                spans.Add(new TextSpan(start, i - start, Nocturne.Neutral600));
                continue;
            }
            if (ch is '"' or '\'' or '`')                              // string
            {
                char q = ch; int start = i; i++;
                while (i < n && code[i] != q) { if (code[i] == '\\') i++; i++; }
                if (i < n) i++;
                spans.Add(new TextSpan(start, i - start, StringColor));
                continue;
            }
            if (char.IsLetter(ch) || ch == '_')                       // identifier
            {
                int start = i;
                while (i < n && (char.IsLetterOrDigit(code[i]) || code[i] == '_')) i++;
                var word = code[start..i];
                if (Keywords.Contains(word))
                    spans.Add(new TextSpan(start, i - start, Nocturne.Accent400));
                else if (char.IsUpper(word[0]))
                    spans.Add(new TextSpan(start, i - start, Nocturne.Accent300));
                continue;
            }
            i++;
        }
        return spans;
    }
}
