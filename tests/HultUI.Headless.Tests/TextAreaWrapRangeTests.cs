using System.Reflection;
using System.Text;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// A hard line that wraps keeps the widths its rows hold for, and a resize to a width inside them
/// takes its rows from the last width's instead of searching it again
/// (<see cref="TextAreaElement.KeepsWrapRanges"/>). With real fonts and real shaping — Latin with
/// accents, dashes and arrows, Han and kana, Hangul and code, which keep ranges, and Arabic (whose
/// prefixes measure narrower as they grow), Hebrew, Thai and emoji, on the BiDi path, which keep
/// none — the rows through a drag both ways, fine steps and a return are those of a pass keeping
/// no ranges and of the reference paths, in a face proven monospace and a proportional one, and
/// the drag searches far fewer lines.
/// </summary>
public class TextAreaWrapRangeTests
{
    private static readonly string[][] Words =
    [
        ["the", "quick", "window", "resize", "wraps", "every", "line", "again", "café", "naïve", "—", "→"],
        ["مرحبا", "بالعالم", "كتاب", "مدينة", "الناس", "يعمل", "بيت", "السلام", "عليكم", "جديد", "١٢٣"],
        ["שלום", "עולם", "ספר", "בית", "חדש", "גדול", "מים", "אור"],
        ["東京", "日本語の", "文章を", "折り返す", "漢字", "かな", "カタカナ", "。"],
        ["한국어", "문장을", "줄바꿈", "합니다", "인터넷"],
        ["สวัสดี", "ภาษา", "ไทย", "ตัดคำ", "ข้อความ"],
        ["\U0001F600", "\U0001F44D\U0001F3FD", "\U0001F468‍\U0001F469‍\U0001F467", "1️⃣", "ok", "\U0001F1F8\U0001F1EA"],
        ["return", "x=>x+1;", "{\"key\":", "[1,2,3],", "null", "0x1f", "==="],
    ];

    // Paragraphs of 100 to 1,400 characters, each of one script, those of Latin, Han and kana,
    // Hangul and code now and then with a word of another of those (a paragraph with right-to-left
    // text, emoji or Thai in it takes the BiDi path, which keeps no ranges), and short lines between.
    private static string Document()
    {
        var rng = new Random(41);
        int[] plain = [0, 3, 4, 7];
        var sb = new StringBuilder();
        for (int p = 0; p < 64; p++)
        {
            int script = p % 8 < 4 ? plain[p % 4] : p % 8 == 4 ? 1 + 4 * (p / 8 % 2) : p % 8 == 5 ? 2 : p % 8 == 6 ? 6 : 0;
            var main = Words[script];
            bool mixes = plain.Contains(script);
            int length = rng.Next(100, 1_400), start = sb.Length;
            while (sb.Length - start < length)
            {
                var words = mixes && rng.Next(8) == 0 ? Words[plain[rng.Next(plain.Length)]] : main;
                sb.Append(words[rng.Next(words.Length)]).Append(' ');
            }
            sb.Append('\n').Append("line ").Append(p).Append('\n');
        }
        return sb.ToString();
    }

    private static readonly FieldInfo LinesValid =
        typeof(TextAreaElement).GetField("_linesValid", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static int[] Rows(TextAreaElement area, float width, ResolvedInheritedStyle style)
    {
        LinesValid.SetValue(area, false);
        return area.GetLineStarts(width, style).ToArray();
    }

    [Theory]
    [InlineData(FontFamilies.Monospace, 12f)]
    [InlineData(FontFamilies.SansSerif, 14f)]
    public void TheRowsThroughADrag_AreThoseOfAPassKeepingNoRanges(string family, float size)
    {
        string text = Document();
        using var session = HeadlessSession.Create(900, 600);
        TextAreaElement Area() => TextArea.New(text).FontFamily(family).FontSize(size).FlexGrow(1);
        var area = Area();
        var fresh = Area();
        fresh.KeepsWrapRanges = false;
        var reference = Area();
        reference.KeepsWrapRanges = false;
        reference.FitsByCount = false;
        reference.ClustersByRow = false;
        session.Window.SetRoot(Flex.Column().FlexGrow(1).Children(area, fresh, reference));
        session.PumpFrame();
        var style = area.ResolvedInherited;

        const float start = 640.3f;
        var widths = new List<float> { start };
        for (int i = 1; i <= 30; i++) widths.Add(start - i);
        for (int i = 1; i <= 12; i++) widths.Add(start - 30 - i * 0.25f);
        for (int i = 1; i <= 36; i++) widths.Add(start - 33 + i);
        widths.Add(start);
        for (int i = 1; i <= 10; i++) widths.Add(start - i * 0.1f);

        int wrapped0 = area.LinesWrapped, fresh0 = fresh.LinesWrapped, rows = 0;
        foreach (float width in widths)
        {
            var expected = Rows(fresh, width, style);
            Assert.True(expected.AsSpan().SequenceEqual(Rows(area, width, style)), $"at {width} the rows differ from a pass keeping no ranges");
            Assert.True(expected.AsSpan().SequenceEqual(Rows(reference, width, style)), $"at {width} the rows differ from the reference paths'");
            rows += expected.Length;
        }
        Assert.True(rows > widths.Count * 300, $"only {rows} rows");
        int searched = area.LinesWrapped - wrapped0, all = fresh.LinesWrapped - fresh0;
        Assert.True(searched < all * 3 / 4, $"the drag searched {searched} lines against {all} keeping no ranges");
    }
}
