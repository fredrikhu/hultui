using System.Buffers;
using System.Text;
using System.Text.Json;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;

namespace PerfSuite;

/// <summary>Building blocks the scenarios share, so their trees look like an app's, not a test's.</summary>
static class Ui
{
    // The js-framework-benchmark vocabulary, so labels repeat the way list labels do
    // (about 3,600 combinations: the text caches warm up, as they would in an app).
    private static readonly string[] Adjectives =
    [
        "pretty", "large", "big", "small", "tall", "short", "long", "handsome",
        "plain", "quaint", "clean", "elegant", "easy", "angry", "crazy", "helpful",
        "mushy", "odd", "unsightly", "adorable", "important", "inexpensive",
        "cheap", "expensive", "fancy",
    ];
    private static readonly string[] Colours =
        ["red", "yellow", "blue", "green", "pink", "brown", "purple", "white", "black", "orange"];
    private static readonly string[] Nouns =
        ["table", "chair", "house", "bbq", "desk", "car", "pony", "cookie", "sandwich", "burger", "pizza", "mouse", "keyboard"];

    public static string Label(Random rng)
        => $"{Adjectives[rng.Next(Adjectives.Length)]} {Colours[rng.Next(Colours.Length)]} {Nouns[rng.Next(Nouns.Length)]}";

    /// <summary>A paragraph of <paramref name="words"/> vocabulary words — wraps at any sensible width.</summary>
    public static string Paragraph(Random rng, int words)
    {
        var parts = new string[words];
        for (int i = 0; i < words; i++)
            parts[i] = (i % 3) switch
            {
                0 => Adjectives[rng.Next(Adjectives.Length)],
                1 => Colours[rng.Next(Colours.Length)],
                _ => Nouns[rng.Next(Nouns.Length)],
            };
        return string.Join(' ', parts);
    }

    /// <summary>
    /// A 900-line document shaped like a project's notes file: headings, gaps, bullets, and a
    /// paragraph in every fourteen lines kept on one line however long it runs (up to 1,700
    /// characters), so a TextArea wraps it.
    /// </summary>
    public static string Notes(Random rng)
    {
        var lines = new string[900];
        for (int i = 0; i < lines.Length; i++)
            lines[i] = (i % 14) switch
            {
                0 => "## " + Label(rng),
                1 or 7 => "",
                4 => Paragraph(rng, 60 + rng.Next(180)),
                _ => "- " + Paragraph(rng, 6 + rng.Next(18)),
            };
        return string.Join('\n', lines);
    }

    private static readonly string[] HebrewWords =
        ["שלום", "עולם", "ספר", "בית", "מחשב", "חלון", "קובץ", "תיקייה", "שורה", "טקסט", "מסך", "עכבר"];

    /// <summary>
    /// Hebrew with a vocabulary word in every third place: right-to-left text with left-to-right
    /// runs in it, laid out by the BiDi path.
    /// </summary>
    public static string BidiParagraph(Random rng, int words)
    {
        var parts = new string[words];
        for (int i = 0; i < words; i++)
            parts[i] = i % 3 == 2 ? Nouns[rng.Next(Nouns.Length)] : HebrewWords[rng.Next(HebrewWords.Length)];
        return string.Join(' ', parts);
    }

    // IPA letters Cascadia Mono (Windows' monospace) has no glyph for: in a monospace label
    // they come from a fallback face, so the label is several font runs.
    private static readonly string[] IpaWords = ["ɐɓɖ", "ɠɦɨ", "ɯɰɱ", "ɕɗɘ", "ɜɝɞ", "ɟɡɤ", "ɥɧɭ", "ɮɳɐ"];

    /// <summary>
    /// Vocabulary words with an IPA "word" in every third place — a label of more than one font
    /// run when set in a face without IPA, as Windows' monospace is. Below U+0590 throughout,
    /// so the plain path lays it out.
    /// </summary>
    public static string MultiFontParagraph(Random rng, int words)
    {
        var parts = new string[words];
        for (int i = 0; i < words; i++)
            parts[i] = i % 3 == 2 ? IpaWords[rng.Next(IpaWords.Length)] : Nouns[rng.Next(Nouns.Length)];
        return string.Join(' ', parts);
    }

    private static readonly string[] States = ["open", "merged", "closed"];

    /// <summary>
    /// An API response as Meridian's response viewer shows it: a page of <c>items</c> objects
    /// (ids, strings, a nested author, short arrays, booleans, nulls), pretty-printed by
    /// <see cref="Utf8JsonWriter"/> as Meridian's Prettify does — two-space indent, '\n' line
    /// ends, about 19 characters a line. About <paramref name="lines"/> lines long; at 24 lines
    /// an item.
    /// </summary>
    public static string JsonResponse(Random rng, int lines)
    {
        int items = Math.Max(1, lines / 24);
        var buffer = new ArrayBufferWriter<byte>(items * 600);
        using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true, NewLine = "\n" }))
        {
            w.WriteStartObject();
            w.WriteNumber("page", 1);
            w.WriteNumber("per_page", items);
            w.WriteNumber("total", items * 7 + rng.Next(1_000));
            w.WriteStartArray("items");
            for (int i = 0; i < items; i++)
            {
                w.WriteStartObject();
                w.WriteNumber("id", 100_000 + i);
                w.WriteString("sha", rng.Next(0x1000000, int.MaxValue).ToString("x7")[..7]);
                w.WriteString("title", Label(rng));
                w.WriteString("state", States[rng.Next(States.Length)]);
                w.WriteBoolean("draft", rng.Next(4) == 0);
                w.WriteNumber("score", Math.Round(rng.NextDouble() * 100, 2));
                w.WriteStartObject("author");
                w.WriteNumber("id", rng.Next(1, 500));
                w.WriteString("login", Nouns[rng.Next(Nouns.Length)] + rng.Next(100));
                if (rng.Next(3) == 0) w.WriteNull("avatar_url");
                else w.WriteString("avatar_url", $"https://cdn.example/a/{rng.Next(10_000)}.png");
                w.WriteEndObject();
                w.WriteStartArray("labels");
                for (int k = rng.Next(1, 4); k > 0; k--) w.WriteStringValue(Colours[rng.Next(Colours.Length)]);
                w.WriteEndArray();
                w.WriteStartArray("sizes");
                for (int k = 0; k < 4; k++) w.WriteNumberValue(rng.Next(1, 5_000));
                w.WriteEndArray();
                w.WriteString("created_at", new DateTime(2026, 1, 1).AddMinutes(rng.Next(400_000)).ToString("yyyy-MM-ddTHH:mm:ssZ"));
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    // VS Code Dark+, the theme HultUI.Syntax colours with by default.
    public static readonly Color JsonKey = Color.FromHex("#4EC9B0");
    public static readonly Color JsonString = Color.FromHex("#CE9178");
    public static readonly Color JsonNumber = Color.FromHex("#B5CEA8");
    public static readonly Color JsonLiteral = Color.FromHex("#569CD6");

    /// <summary>
    /// The colour spans HultUI.Syntax gives <see cref="JsonResponse"/>'s text, flattened to
    /// whole-text offsets as Meridian flattens them: a key with its quotes, a string value with
    /// its quotes, a number, a <c>true</c>/<c>false</c>/<c>null</c>. Punctuation keeps the
    /// editor colour, which Dark+ gives no span. Checked against
    /// <c>SyntaxHighlighter.TokenizeLines("response.json", …)</c> on a 19,013-line response:
    /// the same 22,968 spans, start, length and colour. Written out here so the suite needs no
    /// TextMate grammar, and so setup does not spend a third of a second tokenizing.
    /// </summary>
    public static TextSpan[] JsonSpans(string text)
    {
        var spans = new List<TextSpan>(text.Length / 12);
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            int end = i + 1;
            if (c == '"')
            {
                while (text[end] != '"') end += text[end] == '\\' ? 2 : 1;
                end++;
                int after = end;
                while (after < text.Length && text[after] == ' ') after++;
                spans.Add(new TextSpan(i, end - i, after < text.Length && text[after] == ':' ? JsonKey : JsonString));
            }
            else if (c == '-' || char.IsAsciiDigit(c))
            {
                while (end < text.Length && (char.IsAsciiDigit(text[end]) || text[end] is '.' or 'e' or 'E' or '+' or '-')) end++;
                spans.Add(new TextSpan(i, end - i, JsonNumber));
            }
            else if (char.IsAsciiLetter(c))
            {
                while (end < text.Length && char.IsAsciiLetter(text[end])) end++;
                spans.Add(new TextSpan(i, end - i, JsonLiteral));
            }
            i = end;
        }
        return spans.ToArray();
    }

    public static readonly Color Muted = Color.FromHex("#94A3B8");
    public static readonly Color Accent = Color.FromHex("#4F46E5");
    public static readonly Color Selected = Color.FromHex("#DBEAFE");
    public static readonly Color CardFill = Color.FromHex("#F8FAFC");
    public static readonly Color Line = Color.FromHex("#E2E8F0");

    /// <summary>The js-framework-benchmark row: id, label, a remove glyph.</summary>
    public static BoxElement Row(int id, string label)
        => Flex.Row(gap: 12).FlexShrink(0)
            .AlignItems(AlignItems.Center)
            .Padding(new Thickness(6, 16))
            .Background(Color.White)
            .Children(
                Text.New($"{id}").FontSize(13f).TextColor(Muted).Width(Dimension.Px(64)),
                Box.New().FlexGrow(1).Children(Text.New(label).FontSize(13f)),
                Box.New().Width(Dimension.Px(24)).Children(Text.New("✕").FontSize(13f).TextColor(Color.FromHex("#EF4444"))));

    public static BoxElement Rows(Random rng, int count, int firstId = 1)
    {
        var body = Flex.Column().FlexShrink(0);
        var rows = new Element[count];
        for (int i = 0; i < count; i++) rows[i] = Row(firstId + i, Label(rng));
        body.ReplaceChildren(rows);
        return body;
    }

    /// <summary>A small card: title, two lines of text, a footer row.</summary>
    public static BoxElement Card(Random rng, int i)
        => Flex.Column(gap: 6).Width(Dimension.Px(220)).FlexShrink(0)
            .Padding(14).Background(Color.White).CornerRadius(10f)
            .BorderColor(Line).BorderWidth(1f)
            .Children(
                Text.New($"Card {i}").FontSize(15f).FontWeight(FontWeight.SemiBold),
                Text.New(Paragraph(rng, 12)).FontSize(12f).TextColor(Color.FromHex("#475569")),
                Flex.Row(gap: 8).Children(
                    Text.New(Label(rng)).FontSize(11f).TextColor(Muted).FlexGrow(1),
                    Text.New($"#{i}").FontSize(11f).TextColor(Accent)));

    /// <summary>
    /// The window width at a step of a resize drag: 40 distinct widths, 4 px apart, so a
    /// step never lands on a width any measure cache still holds (the cache keeps four).
    /// Alternating between two widths would time cache hits, not a resize.
    /// </summary>
    public static int DragWidth(int step, int from = 1040) => from - 4 * (1 + step % 40);
}
