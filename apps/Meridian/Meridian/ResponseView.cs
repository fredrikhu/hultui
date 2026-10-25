using System.Text.Json;
using HultUI.Core.Rendering;
using HultUI.Syntax;
using Meridian.Core;

namespace Meridian;

/// <summary>
/// A text response body as the body pane shows it: pretty or raw, split into lines, where each
/// line begins, and — for a short body — its colour.
///
/// <para>Made off the UI thread, by <see cref="Format"/> on a worker. A large JSON response
/// used to be pretty-printed, split and tokenized inside the build that first showed it: an
/// 18,884-line response held the UI thread for ~110 ms before its first frame (headless, JIT,
/// warm), 2.5 ms now, and 4 ms with the frame that draws its colour.</para>
///
/// <para>A body of at most <see cref="ColourWithTextMaxLines"/> lines is tokenized in the same
/// job and shows coloured at once. A longer one shows plain as soon as it is split, and
/// <see cref="Highlight"/>, a second job, colours it when its spans land: the text is what the
/// reader came for, and a tokenize of tens of thousands of lines takes a tenth of a second or
/// more.</para>
/// </summary>
internal sealed class ResponseView
{
    /// <summary>A body of at most this many lines is tokenized with its formatting, so it
    /// appears coloured rather than plain for a frame and then coloured: JSON tokenizes at
    /// ~5.5 µs a line, so this is about 11 ms of a worker's time.</summary>
    internal const int ColourWithTextMaxLines = 2_000;

    /// <summary>
    /// The most lines a body is coloured at; a longer one stays plain. Ten times
    /// <see cref="SyntaxHighlighter.DefaultMaxLines"/>, which was this pane's cap while the
    /// tokenize ran on the UI thread. On a worker a JSON response tokenizes at ~5.5 µs a line
    /// (a number array at ~2.9), allocating ~4 KB a line that dies young, and keeps 40 bytes
    /// a span (~1.25 spans a line) plus the body's index of them, 4 bytes a span. The
    /// highlighter's 2,000,000-character cap stops a JSON object response near 100,000 lines
    /// anyway (0.54 s, 4.7 MB of spans); this one bounds a body of short lines, which that
    /// cap lets reach ~400,000: 200,000 lines of numbers is 0.57 s and 7.6 MB.
    /// </summary>
    internal const int MaxColouredLines = 200_000;

    public required HttpResponseData Response { get; init; }
    public required bool Raw { get; init; }
    public required string Text { get; init; }
    public required string[] Lines { get; init; }

    /// <summary>Character offset where each line begins. The search tinting and the
    /// scroll-to-match both speak absolute offsets into <see cref="Text"/>.</summary>
    public required int[] LineStarts { get; init; }

    /// <summary>The name the grammar is chosen by ("response.json"), or null for a content
    /// type that has none.</summary>
    public string? GrammarPath { get; init; }

    /// <summary>The colour spans in absolute offsets, when they came with the text.</summary>
    public IReadOnlyList<TextSpan>? Spans { get; init; }

    /// <summary>True when the colour comes later, from <see cref="Highlight"/>.</summary>
    public bool ColourToCome { get; init; }

    /// <summary>
    /// Formats <paramref name="response"/>'s text body: pretty (JSON re-indented) or as
    /// received, line endings made '\n', split, and coloured when it is short. Runs on a
    /// worker; throws <see cref="OperationCanceledException"/> when the pane stopped wanting it.
    /// The tokenize's time budget runs on <paramref name="clock"/>, the application's.
    /// </summary>
    public static ResponseView Format(HttpResponseData response, bool raw, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        string text = raw ? response.Body.Text : Prettify(response);
        text = text.ReplaceLineEndings("\n");
        cancellationToken.ThrowIfCancellationRequested();
        var lines = text.Split('\n');
        var starts = StartsOf(lines);
        string? grammar = GrammarPathFor(response.ContentType);
        bool now = grammar != null && lines.Length <= ColourWithTextMaxLines;
        return new ResponseView
        {
            Response = response,
            Raw = raw,
            Text = text,
            Lines = lines,
            LineStarts = starts,
            GrammarPath = grammar,
            Spans = now ? Tokenize(grammar!, text, starts, clock, cancellationToken) : null,
            ColourToCome = grammar != null && !now,
        };
    }

    /// <summary>The colour spans for a view that <see cref="ColourToCome"/>, in absolute
    /// offsets; null when the text gets none. Runs on a worker; throws
    /// <see cref="OperationCanceledException"/> when the pane stopped wanting it.</summary>
    public static IReadOnlyList<TextSpan>? Highlight(ResponseView view, TimeProvider clock,
        CancellationToken cancellationToken) =>
        view.GrammarPath is { } grammar ? Tokenize(grammar, view.Text, view.LineStarts, clock, cancellationToken) : null;

    /// <summary>Pretty mode: JSON re-indented via a real parse; anything that isn't valid JSON
    /// (or isn't JSON at all) shows as received.</summary>
    internal static string Prettify(HttpResponseData response)
    {
        if (response.Body.Truncated) return response.Body.Text;   // a cut JSON never parses
        var ct = response.ContentType;
        bool json = ct != null && (ct.Contains("/json", StringComparison.OrdinalIgnoreCase)
            || ct.Contains("+json", StringComparison.OrdinalIgnoreCase));
        if (!json) return response.Body.Text;
        try
        {
            // Utf8JsonWriter, not JsonSerializer: no reflection, so Native AOT
            // has nothing to trim away.
            using var doc = JsonDocument.Parse(response.Body.Text);
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
                doc.WriteTo(writer);
            return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
        }
        catch (JsonException)
        {
            return response.Body.Text;
        }
    }

    private static int[] StartsOf(string[] lines)
    {
        var starts = new int[lines.Length];
        int offset = 0;
        for (int i = 0; i < lines.Length; i++) { starts[i] = offset; offset += lines[i].Length + 1; }
        return starts;
    }

    private static string? GrammarPathFor(string? contentType) => contentType switch
    {
        null => null,
        var ct when ct.Contains("json", StringComparison.OrdinalIgnoreCase) => "response.json",
        var ct when ct.Contains("xml", StringComparison.OrdinalIgnoreCase) => "response.xml",
        var ct when ct.Contains("html", StringComparison.OrdinalIgnoreCase) => "response.html",
        _ => null,
    };

    /// <summary>The per-line tokenize flattened to absolute offsets into the joined text.</summary>
    private static IReadOnlyList<TextSpan>? Tokenize(string grammar, string text, int[] starts,
        TimeProvider clock, CancellationToken cancellationToken)
    {
        IReadOnlyList<TextSpan>[]? perLine;
        try
        {
            perLine = SyntaxHighlighter.TokenizeLines(grammar, text, MaxColouredLines, cancellationToken, clock);
        }
        catch (OperationCanceledException) { throw; }
        catch { return null; }   // highlight is best-effort
        if (perLine == null) return null;

        int lineCount = Math.Min(perLine.Length, starts.Length);
        int total = 0;
        for (int i = 0; i < lineCount; i++) total += perLine[i].Count;
        var flat = new TextSpan[total];
        int n = 0;
        for (int i = 0; i < lineCount; i++)
        {
            int offset = starts[i];
            var line = perLine[i];
            for (int k = 0; k < line.Count; k++)
            {
                var s = line[k];
                flat[n++] = new TextSpan(offset + s.Start, s.Length, s.Color, s.Background);
            }
        }
        return flat;
    }
}

/// <summary>The colour <see cref="ResponseView.Highlight"/> found for <see cref="View"/>.</summary>
internal sealed record ResponseColour(ResponseView View, IReadOnlyList<TextSpan>? Spans);
