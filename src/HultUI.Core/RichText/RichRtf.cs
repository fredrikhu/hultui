using System.Globalization;
using System.Text;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;

namespace HultUI.Core.RichText;

/// <summary>
/// Blocks as RTF, which every word processor opens and pastes: Word, LibreOffice, WordPad,
/// TextEdit, mail clients.
/// </summary>
/// <remarks>
/// <para>Sizes follow the editor at 96 to the inch: a layout unit is 15 twips, and a font size of
/// 14 is 10.5 pt. Headings are their scale of the body and carry an outline level, so Word's
/// navigation pane lists them. Lists are written with their markers as text — the bullet, the
/// number or the box the editor draws, then a tab on a hanging indent — so every reader numbers
/// them as the editor did, though Word does not take them for its own lists.</para>
/// <para>A link is a <c>HYPERLINK</c> field, a quote a ruled indent, a code block the code face
/// on a shaded ground, a table <c>\trowd</c> rows with the header row repeated across pages, and
/// a picture a PNG or JPEG <c>\pict</c> when <see cref="RichExportOptions.Pictures"/> gives its
/// file, else its alt text in brackets. The output is 7-bit: everything past ASCII is a
/// <c>\u</c> escape.</para>
/// </remarks>
public static class RichRtf
{
    /// <summary><paramref name="document"/> as an RTF file.</summary>
    public static string Write(RichDocument document, RichExportOptions? options = null) =>
        Write(document.Blocks, options);

    /// <summary><paramref name="blocks"/> as RTF: a whole document, or a fragment for the
    /// clipboard.</summary>
    public static string Write(IReadOnlyList<RichBlock> blocks, RichExportOptions? options = null)
    {
        var writer = new Writer(options ?? new RichExportOptions());
        writer.Blocks(blocks);
        return writer.Finish();
    }

    private sealed class Writer
    {
        private readonly RichExportOptions _options;
        private readonly RichTextTheme _rt;
        private readonly StringBuilder _body = new();
        private readonly List<(string Name, string Kind)> _fonts = [];
        private readonly List<Color> _colors = [];
        private readonly int[] _counters = new int[RichLists.Depths];
        private readonly int _base, _codeFont;
        // The size of the paragraph being written, in half-points, which inline code scales.
        private int _size;

        public Writer(RichExportOptions options)
        {
            _options = options;
            _rt = options.Look;
            Font(options.FontFamily ?? "Arial");   // \f0, the body face
            _codeFont = Font(_rt.CodeFamily, "fmodern");
            _base = HalfPoints(options.FontSize);
        }

        public string Finish()
        {
            var sb = new StringBuilder(_body.Length + 512);
            sb.Append("{\\rtf1\\ansi\\ansicpg1252\\deff0\\deflang1033\n{\\fonttbl");
            for (int i = 0; i < _fonts.Count; i++)
            {
                sb.Append("{\\f").Append(i).Append('\\').Append(_fonts[i].Kind).Append("\\fcharset0 ");
                Escape(sb, _fonts[i].Name);
                sb.Append(";}");
            }
            sb.Append("}\n{\\colortbl ;");
            foreach (var c in _colors)
                sb.Append("\\red").Append(c.R).Append("\\green").Append(c.G).Append("\\blue").Append(c.B).Append(';');
            sb.Append("}\n");
            if (_options.Title is { Length: > 0 } title)
            {
                sb.Append("{\\info{\\title ");
                Escape(sb, title);
                sb.Append("}}\n");
            }
            sb.Append("\\viewkind4\\uc1\n").Append(_body).Append('}');
            return sb.ToString();
        }

        // ── Tables of fonts and colours, filled as the body uses them ──

        private int Font(string name, string kind = "fswiss")
        {
            // A word processor knows no generic families: the face each stands for everywhere.
            (name, kind) = name switch
            {
                "monospace" or "ui-monospace" => ("Courier New", "fmodern"),
                "serif" or "ui-serif" => ("Times New Roman", "froman"),
                "sans-serif" or "system-ui" or "ui-sans-serif" => ("Arial", "fswiss"),
                _ => (name, kind),
            };
            int i = _fonts.FindIndex(f => f.Name == name);
            if (i >= 0) return i;
            _fonts.Add((name, kind));
            return _fonts.Count - 1;
        }

        // Colour 0 is the reader's own text colour; these count from 1. RTF has no alpha, so a
        // translucent colour is laid over the white page.
        private int Colour(Color c)
        {
            if (c.A < 255)
            {
                float a = c.A / 255f;
                c = new Color((byte)MathF.Round(255 + (c.R - 255) * a), (byte)MathF.Round(255 + (c.G - 255) * a),
                    (byte)MathF.Round(255 + (c.B - 255) * a), 255);
            }
            int i = _colors.FindIndex(k => k.R == c.R && k.G == c.G && k.B == c.B);
            if (i >= 0) return i + 1;
            _colors.Add(c);
            return _colors.Count;
        }

        private static int Twips(float units) => (int)MathF.Round(units * 15f);
        private static int HalfPoints(float units) => Math.Max(1, (int)MathF.Round(units * 1.5f));

        // ── Blocks ──

        public void Blocks(IReadOnlyList<RichBlock> blocks)
        {
            for (int i = 0; i < blocks.Count;)
            {
                var b = blocks[i];
                int number = RichLists.Next(b, _counters);
                if (b.IsTableCell)
                {
                    i = Table(blocks, i);
                    continue;
                }
                var next = i + 1 < blocks.Count ? blocks[i + 1] : null;
                // The editor's gaps: a little between list items and the paragraphs continuing
                // them, and between quoted lines; a paragraph's space otherwise.
                int after = Twips(next is not null && ((Listy(b) && Listy(next)) || (b.Kind == BlockKind.Quote && next.Kind == BlockKind.Quote))
                    ? _rt.ListItemSpacing : _rt.BlockSpacing);
                int indent = Twips(b.Indent * _rt.IndentWidth);
                string left = indent > 0 ? $"\\li{indent}" : "";
                switch (b.Kind)
                {
                    case BlockKind.Heading:
                    {
                        // Room above to stand apart, as the editor gives it: the paragraph before
                        // already leaves its own space, which a word processor adds to this.
                        float size = _options.FontSize * _rt.HeadingScaleOf(b.HeadingLevel);
                        int above = Twips(Math.Max(0f, _rt.HeadingSpaceAbove * size - _rt.BlockSpacing));
                        Paragraph($"\\outlinelevel{b.HeadingLevel - 1}\\keepn\\sb{above}\\sa{after}{Align(b)}",
                            HalfPoints(size), _rt.HeadingWeight >= FontWeight.SemiBold ? "\\b" : "");
                        Inline(b);
                        break;
                    }
                    case BlockKind.BulletItem or BlockKind.NumberedItem or BlockKind.TaskItem:
                    {
                        int column = Twips(_rt.IndentWidth);
                        int text = indent + column;
                        Paragraph($"\\li{text}\\fi-{column}\\tx{text}\\sa{after}{Align(b)}", _base);
                        string marker = b.Kind switch
                        {
                            BlockKind.NumberedItem => number.ToString(CultureInfo.InvariantCulture) + ".",
                            BlockKind.TaskItem => RichLists.Box(b.Checked).ToString(),
                            _ => RichLists.Bullet(b.Indent).ToString(),
                        };
                        Escape(_body, marker);
                        _body.Append("\\tab ");
                        Inline(b);
                        break;
                    }
                    case BlockKind.Quote:
                    {
                        int bar = Twips(_rt.QuoteBarWidth), gap = Twips(_rt.QuoteGap);
                        Paragraph($"\\li{indent + bar + gap}\\sa{after}{Align(b)}\\brdrl\\brdrs\\brdrw{bar}\\brsp{gap}\\brdrcf{Colour(RichPaper.Line)}",
                            _base, $"\\cf{Colour(RichPaper.Muted)}");
                        Inline(b);
                        break;
                    }
                    case BlockKind.CodeBlock or BlockKind.Raw:
                    {
                        var pad = _rt.CodeBlockPadding;
                        Paragraph($"\\li{indent + Twips(pad.Left)}\\ri{Twips(pad.Right)}\\sa{after}\\cbpat{Colour(RichPaper.Code)}",
                            HalfPoints(_options.FontSize * _rt.CodeScale), $"\\f{_codeFont}");
                        Escape(_body, b.Text);
                        break;
                    }
                    case BlockKind.Rule:
                        // An empty line ruled along its foot, small enough to sit between blocks.
                        Paragraph($"\\sa{after}\\brdrb\\brdrs\\brdrw{Math.Max(1, Twips(_rt.RuleThickness))}\\brdrcf{Colour(RichPaper.Line)}", 4);
                        break;
                    case BlockKind.Image:
                        Paragraph($"{left}\\sa{after}{Align(b)}", _base);
                        if (b.Image is { } image) Picture(image);
                        break;
                    default:
                        Paragraph($"{left}\\sa{after}{Align(b)}", _base);
                        Inline(b);
                        break;
                }
                _body.Append("\\par\n");
                i++;
            }
        }

        private static bool Listy(RichBlock b) => b.IsListItem || (b.Kind == BlockKind.Paragraph && b.Indent > 0);

        // Every paragraph starts from nothing, so no block inherits the one before it.
        private void Paragraph(string format, int size, string characters = "")
        {
            _size = size;
            _body.Append("\\pard\\plain").Append(format).Append("\\f0\\fs").Append(size).Append(characters).Append(' ');
        }

        private static string Align(RichBlock b) => b.Align switch
        {
            BlockAlign.Center => "\\qc",
            BlockAlign.End => "\\qr",
            _ => "",
        };

        // A PNG or JPEG the app hands over, at the width it asks for or its own, no wider than
        // the page; anything else, its alt text.
        private void Picture(RichImage image)
        {
            if (image.Link is { } href) OpenLink(href);
            if (_options.Pictures?.Invoke(image) is { Length: > 0 } bytes
                && RichPictureFile.Read(bytes) is { MediaType: "image/png" or "image/jpeg", Width: > 0, Height: > 0 } file)
            {
                float width = Math.Min(image.Width ?? file.Width, RichPaper.TextWidth);
                float height = width * file.Height / file.Width;
                _body.Append("{\\pict").Append(file.MediaType == "image/png" ? "\\pngblip" : "\\jpegblip")
                    .Append("\\picw").Append(file.Width).Append("\\pich").Append(file.Height)
                    .Append("\\picwgoal").Append(Twips(width)).Append("\\pichgoal").Append(Twips(height)).Append('\n');
                var hex = Convert.ToHexString(bytes);
                for (int at = 0; at < hex.Length; at += 128)
                    _body.Append(hex, at, Math.Min(128, hex.Length - at)).Append('\n');
                _body.Append('}');
            }
            else
                Escape(_body, "[" + (image.Alt.Length > 0 ? image.Alt : image.Source) + "]");
            if (image.Link is not null) CloseLink();
        }

        // The cells from `start` to the end of their table, a row at a time. Columns share the
        // page by how much their longest cell holds, within the theme's bounds, as the editor
        // shares its width. A fragment may hold only part of a table, and a row only some cells.
        private int Table(IReadOnlyList<RichBlock> blocks, int start)
        {
            int end = start;
            while (end < blocks.Count && blocks[end].IsTableCell && (end == start || !RichTable.StartsTable(blocks, end))) end++;

            int columns = 0;
            for (int i = start; i < end; i++) columns = Math.Max(columns, blocks[i].Column + 1);
            var pad = _rt.TableCellPadding;
            var weights = new float[columns];
            for (int i = start; i < end; i++)
            {
                float natural = blocks[i].Text.Length * _options.FontSize * 0.55f + pad.Left + pad.Right;
                weights[blocks[i].Column] = Math.Max(weights[blocks[i].Column], natural);
            }
            float total = 0;
            for (int c = 0; c < columns; c++) total += weights[c] = Math.Clamp(weights[c], _rt.TableColumnMinWeight, _rt.TableColumnMaxWeight);
            var edges = new int[columns];
            float x = 0;
            for (int c = 0; c < columns; c++) edges[c] = Twips(x += weights[c] / total * RichPaper.TextWidth);

            int border = Math.Max(1, Twips(_rt.TableBorderWidth)), line = Colour(RichPaper.Line), header = Colour(RichPaper.Header);
            string edge = $"\\brdrs\\brdrw{border}\\brdrcf{line}";
            for (int row = start; row < end;)
            {
                int rowEnd = row + 1;
                while (rowEnd < end && blocks[rowEnd].Row == blocks[row].Row) rowEnd++;
                bool head = blocks[row].Row == 0;

                _body.Append("\\trowd\\trgaph").Append(Twips(pad.Left)).Append("\\trleft0")
                    .Append("\\trpaddt").Append(Twips(pad.Top)).Append("\\trpaddft3\\trpaddb").Append(Twips(pad.Bottom)).Append("\\trpaddfb3");
                if (head) _body.Append("\\trhdr");
                for (int i = row; i < rowEnd; i++)
                {
                    _body.Append("\\clbrdrt").Append(edge).Append("\\clbrdrl").Append(edge)
                        .Append("\\clbrdrb").Append(edge).Append("\\clbrdrr").Append(edge);
                    if (head) _body.Append("\\clcbpat").Append(header);
                    _body.Append("\\cellx").Append(edges[blocks[i].Column]);
                }
                _body.Append('\n');
                for (int i = row; i < rowEnd; i++)
                {
                    Paragraph("\\intbl" + Align(blocks[i]), _base, head && _rt.TableHeaderWeight >= FontWeight.SemiBold ? "\\b" : "");
                    Inline(blocks[i]);
                    _body.Append("\\cell\n");
                }
                _body.Append("\\row\n");
                row = rowEnd;
            }
            return end;
        }

        // ── Text ──

        private void Inline(RichBlock block)
        {
            foreach (var run in RichSpans.Runs(block.Spans, block.Text.Length))
            {
                var m = run.Marks;
                string text = block.Text.Substring(run.Start, run.Length);
                if (m.IsNone)
                {
                    Escape(_body, text);
                    continue;
                }
                if (m.Link is { } link) OpenLink(link);
                int group = _body.Length;
                _body.Append('{');
                if (m.Has(InlineStyle.Bold)) _body.Append("\\b");
                if (m.Has(InlineStyle.Italic)) _body.Append("\\i");
                if (m.Has(InlineStyle.Underline)) _body.Append("\\ul");
                if (m.Has(InlineStyle.Strike)) _body.Append("\\strike");
                float size = m.Size is { } s ? s * 1.5f : _size;
                if (m.Has(InlineStyle.Code))
                {
                    _body.Append("\\f").Append(_codeFont);
                    size *= _rt.CodeScale;
                }
                if (m.Has(InlineStyle.Code) || m.Size is not null) _body.Append("\\fs").Append(Math.Max(1, (int)MathF.Round(size)));
                if (m.Family is { } family) _body.Append("\\f").Append(Font(family));
                if (m.Color is { } color) _body.Append("\\cf").Append(Colour(color));
                if (m.Highlight is { } highlight) _body.Append("\\highlight").Append(Colour(highlight));
                // The space ends the last control word; after a bare brace it would be text.
                if (_body.Length > group + 1) _body.Append(' ');
                Escape(_body, text);
                _body.Append('}');
                if (m.Link is not null) CloseLink();
            }
        }

        // A link is a field: the address to follow, and what shows, underlined in the link colour.
        private void OpenLink(string href)
        {
            _body.Append("{\\field{\\*\\fldinst{HYPERLINK \"");
            Escape(_body, href.Replace("\"", "%22"));
            _body.Append("\"}}{\\fldrslt{\\ul\\cf").Append(Colour(RichPaper.Link)).Append(' ');
        }

        private void CloseLink() => _body.Append("}}}");

        // Text as RTF reads it: its three specials escaped, tabs and line breaks as their control
        // words, and anything past ASCII as a \u escape (a UTF-16 unit, signed) with ? for readers
        // that cannot show it.
        private static void Escape(StringBuilder sb, string text)
        {
            foreach (char ch in text)
            {
                switch (ch)
                {
                    case '\\' or '{' or '}':
                        sb.Append('\\').Append(ch);
                        break;
                    case '\t':
                        sb.Append("\\tab ");
                        break;
                    case '\n':
                        sb.Append("\\line ");
                        break;
                    case < ' ':
                        break;
                    case < (char)0x80:
                        sb.Append(ch);
                        break;
                    default:
                        sb.Append("\\u").Append((short)ch).Append('?');
                        break;
                }
            }
        }
    }
}
