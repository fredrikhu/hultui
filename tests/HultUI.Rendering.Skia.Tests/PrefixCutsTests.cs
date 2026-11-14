using HultUI.Core.Layout;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.Text;
using Xunit;

namespace HultUI.Rendering.Skia.Tests;

/// <summary>
/// A word a prefix table cuts short is read off the whole word's run where HarfBuzz says a cut
/// there is safe (<see cref="RunCuts"/>): the width and glyph count of the prefix shaped alone, to
/// the bit. Where it is unsafe, inside a cluster, or before the character the run's script was
/// guessed from, the prefix is shaped as before.
/// </summary>
public class PrefixCutsTests
{
    private static readonly ResolvedInheritedStyle Sans = new(
        FontFamily: "sans-serif", FontSize: 13f, FontWeight: FontWeight.Regular,
        TextColor: new(0, 0, 0, 255), TextDirection: TextDirection.Ltr, Cursor: CursorType.Default);

    private static string Cps(params int[] codepoints) => string.Concat(codepoints.Select(char.ConvertFromUtf32));

    // Japanese, Chinese and Korean lines (code points by number), and Latin with kerning and ligatures.
    private static readonly string[] Lines =
    [
        Cps(0x6771, 0x4EAC, 0x3067, 0x306F, 0x3001, 0x30BF, 0x30EF, 0x30FC, 0x304C, 0x898B, 0x3048, 0x307E, 0x3059, 0x3002,
            0x65E5, 0x672C, 0x8A9E, 0x306E, 0x6587, 0x7AE0, 0x3092, 0x6298, 0x308A, 0x8FD4, 0x3057, 0x307E, 0x3059),
        Cps(0x6211, 0x4EEC, 0x7684, 0x6587, 0x5B57, 0xFF0C, 0x5728, 0x8FD9, 0x91CC, 0x6362, 0x884C, 0x3002, 0x4E2D, 0x6587,
            0x6CA1, 0x6709, 0x7A7A, 0x683C, 0xFF0C, 0x6240, 0x4EE5, 0x6574, 0x884C, 0x662F, 0x4E00, 0x4E2A, 0x8BCD),
        Cps(0xD55C, 0xAD6D, 0xC5B4, 0xB294, 0xB744, 0xC5B4, 0xC4F0, 0xAE30, 0xB97C, 0xD558, 0xC9C0, 0xB9CC, 0xAE34,
            0xB2E8, 0xC5B4, 0xAC00, 0xAE38, 0xAE30, 0xB3C4, 0xD569, 0xB2C8, 0xB2E4),
        Cps(0x3001, 0x3002, 0x3001, 0x6771, 0x4EAC, 0x30BF, 0x30EF, 0x30FC, 0x3067, 0x3059, 0x3088, 0x306D),
        "AVAWAYAVAToTaVaYoWoLTPAVAWAY",
        "Donaudampfschifffahrtsgesellschaftskapitän",
        "officeefficientfiflffiffl",
        "https://example.com/a/very/long/path?query=value",
    ];

    [Fact]
    public void RunCuts_ReadSafeClusterStartsOnly()
    {
        // Glyphs: a cluster of two glyphs at 0, then 2 (unsafe), 3, 5; the first character with a
        // script is at 1.
        var cuts = new RunCuts([0, 0, 2 | RunCuts.Unsafe, 3, 5], [0f, 4f, 7f, 9f, 12f], firstStrong: 1);

        Assert.False(cuts.TryPrefix(1, out _, out _));   // at or before the character the script came from
        Assert.False(cuts.TryPrefix(2, out _, out _));   // unsafe
        Assert.True(cuts.TryPrefix(3, out float width, out int glyphs));
        Assert.Equal((9f, 3), (width, glyphs));
        Assert.False(cuts.TryPrefix(4, out _, out _));   // inside the cluster that starts at 3
        Assert.True(cuts.TryPrefix(5, out width, out glyphs));
        Assert.Equal((12f, 4), (width, glyphs));
        Assert.False(cuts.TryPrefix(6, out _, out _));   // past the last cluster's start

        var none = new RunCuts([0, 1, 2], [0f, 1f, 2f], firstStrong: -1);
        Assert.True(none.TryPrefix(1, out width, out glyphs));
        Assert.Equal((1f, 1), (width, glyphs));
    }

    [Fact]
    public void ACut_ReadOffTheRun_IsThePrefixShapedAlone_ToTheBit()
    {
        var fonts = new SkiaFontCollection();
        using var shaper = new SkiaTextShaper();
        int read = 0, refused = 0;
        foreach (string line in Lines)
        {
            // The face the line's first character is drawn in, as itemizing would pick it.
            var primary = fonts.Resolve(Sans.FontFamily, Sans.FontWeight, Sans.FontSlant);
            int first = char.ConvertToUtf32(line, 0) is int c && c == 0x3001 ? 0x6771 : char.ConvertToUtf32(line, 0);
            var font = primary.HasGlyph(first) ? primary : fonts.MatchCharacter(first, Sans.FontWeight, Sans.FontSlant, "ja") ?? primary;
            foreach (float size in new[] { 11f, 13f, 17.5f })
            {
                var whole = ((IShapesWithCuts)shaper).Shape(line, font, size, out var cuts);
                Assert.NotNull(cuts);
                Assert.Equal(BitConverter.SingleToInt32Bits(shaper.Shape(line, font, size).Width), BitConverter.SingleToInt32Bits(whole.Width));
                for (int length = 1; length < line.Length; length++)
                {
                    if (char.IsLowSurrogate(line[length])) continue;
                    if (!cuts!.TryPrefix(length, out float width, out int glyphs)) { refused++; continue; }
                    var alone = shaper.Shape(line[..length], font, size);
                    Assert.Equal(BitConverter.SingleToInt32Bits(alone.Width), BitConverter.SingleToInt32Bits(width));
                    Assert.Equal(alone.Glyphs.Length, glyphs);
                    read++;
                }
            }
        }
        Assert.True(read > 300, $"only {read} cuts read");
        Assert.True(refused > 0, "no cut refused: the unsafe ones are not being found");
    }

    [Fact]
    public void APrefixWithoutTheCharacterTheScriptCameFrom_IsNotRead()
    {
        var fonts = new SkiaFontCollection();
        using var shaper = new SkiaTextShaper();
        string line = Lines[3];   // 、。、 then 東京…: the script is guessed from 東, the fourth
        var font = fonts.MatchCharacter(0x6771, FontWeight.Regular, FontSlant.Normal, "ja")
            ?? fonts.Resolve("sans-serif", FontWeight.Regular, FontSlant.Normal);
        ((IShapesWithCuts)shaper).Shape(line, font, 13f, out var cuts);
        for (int length = 1; length <= 3; length++)
            Assert.False(cuts!.TryPrefix(length, out _, out _));
    }

    [Fact]
    public void RightToLeftRuns_HaveNoCuts()
    {
        var fonts = new SkiaFontCollection();
        using var shaper = new SkiaTextShaper();
        string hebrew = Cps(0x05E9, 0x05DC, 0x05D5, 0x05DD, 0x05E2, 0x05D5, 0x05DC, 0x05DD, 0x05D8, 0x05D5, 0x05D1);
        var font = fonts.MatchCharacter(0x05E9, FontWeight.Regular, FontSlant.Normal)
            ?? fonts.Resolve("sans-serif", FontWeight.Regular, FontSlant.Normal);
        ((IShapesWithCuts)shaper).Shape(hebrew, font, 13f, out var cuts);
        Assert.Null(cuts);
    }

    [Fact]
    public void PrefixTables_ReadWordsCutShortOffCuts_WithTheReferencesWidths()
    {
        var cutShaper = new CountingShaper(new SkiaTextShaper());
        var referenceShaper = new CountingShaper(new SkiaTextShaper());
        var withCuts = new TextPipeline(new SkiaFontCollection(), cutShaper);
        var reference = new TextPipeline(new SkiaFontCollection(), referenceShaper) { PrefixesFromCuts = false };
        string text = string.Join('\n', Lines.Select((l, i) => i % 2 == 0 ? l + " " + l : l + l));
        var styles = new[] { Sans, Sans with { FontFamily = FontFamilies.Monospace, FontSize = 12f }, Sans with { LetterSpacing = 0.6f },
            Sans with { FontWeight = FontWeight.Bold, FontSize = 15f } };
        int compared = 0;
        foreach (var style in styles)
        {
            int lineStart = 0;
            foreach (string line in text.Split('\n'))
            {
                for (int start = 0; start < line.Length; start += 3)
                {
                    if (char.IsLowSurrogate(line[start])) continue;
                    int length = Math.Min(line.Length - start, 40);
                    var a = withCuts.MeasurePrefixes(text, lineStart + start, length, style);
                    var aw = new float[length + 1];
                    for (int n = 0; n <= length; n++) aw[n] = a?.Width(n) ?? float.NaN;
                    var b = reference.MeasurePrefixes(text, lineStart + start, length, style);
                    for (int n = 0; n <= length; n++)
                    {
                        float bw = b?.Width(n) ?? float.NaN;
                        Assert.True(BitConverter.SingleToInt32Bits(aw[n]) == BitConverter.SingleToInt32Bits(bw),
                            $"'{line}' from {start}, {n} chars in {style.FontFamily} {style.FontSize}: {aw[n]} where the reference has {bw}");
                        compared++;
                    }
                }
                lineStart += line.Length + 1;
            }
        }
        Assert.True(compared > 5_000, $"only {compared} widths compared");
        Assert.True(withCuts.PrefixesCut > 1_000, $"{withCuts.PrefixesCut} read off cuts");
        Assert.True(withCuts.PrefixesShaped > 0, "no word cut short was shaped: the unsafe cuts are not being found");
        Assert.Equal(0, reference.PrefixesCut);
        Assert.True(cutShaper.Calls < referenceShaper.Calls * 3 / 4,
            $"{cutShaper.Calls} shaper calls against the reference's {referenceShaper.Calls}");
    }

    // README-like paragraphs, as a TextArea shows them: Japanese and Chinese prose without spaces
    // between its words, with Latin names, commands and numbers between spaces, and long lines.
    private static readonly string Japanese = string.Join('\n',
        "# HultUI について",
        "",
        "HultUI は、.NET で動くデスクトップ アプリのための小さな UI フレームワークです。画面の要素は木の形に組み立てられ、状態が変わると変わった部分だけが描き直されます。文字の組版には HarfBuzz と Skia を使い、日本語や中国語のように単語の間に空白がない文章でも、行の幅に合わせて自然な位置で折り返します。",
        "",
        "## はじめに",
        "",
        "リポジトリを手元に複製したら、まず dotnet build でソリューション全体をビルドしてください。サンプルのアプリは samples フォルダーにあり、それぞれ dotnet run で起動できます。ウィンドウの大きさを変えると、テキストエリアの行は新しい幅で組み直されます。そのたびに同じ単語を字形処理し直さないよう、パイプラインは単語ごとの結果を覚えておき、どこで切ってよいかも一緒に残します。",
        "設定ファイルは JSON で書かれ、テーマの色や文字の大きさ、キーボードの割り当てを変えられます。変更は保存した時点で反映されるので、アプリを再起動する必要はありません。詳しくは docs フォルダーの GUIDE.md を読んでください。",
        "",
        "## よくある質問",
        "",
        "「文字がぼやけて見える」という報告がありました。高い解像度の画面で倍率が 150 % のとき、ピクセルの境目に合わせて線と塗りを揃えることで直りました。それでも気になる場合は、環境変数を設定して描画の方式を切り替え、もう一度試してみてください。",
        "長い文書を開いたときに動作が重くなる場合は、折り返しを止めるか、ウィンドウを少し広げてみてください。一行が長いほど、幅を変えるたびに測る文字が増えます。");

    private static readonly string Chinese = string.Join('\n',
        "# HultUI 简介",
        "",
        "HultUI 是一个运行在 .NET 上的小型桌面界面框架。界面上的元素按照树的形状组织起来，状态改变时只重新绘制发生变化的部分。文字排版使用 HarfBuzz 和 Skia，即使是中文和日文这样词与词之间没有空格的文字，也能按照行宽在合适的位置换行。",
        "",
        "## 开始使用",
        "",
        "把仓库克隆到本地以后，先用 dotnet build 构建整个解决方案。示例程序放在 samples 文件夹里，每一个都可以用 dotnet run 启动。改变窗口的大小时，文本区域里的每一行都会按照新的宽度重新排列。为了不让同一个词一次又一次地重新整形，排版管线会记住每个词的结果，并且记下哪些位置可以安全地切开。",
        "配置文件用 JSON 编写，可以修改主题颜色、字体大小和键盘快捷键。保存以后修改立刻生效，不需要重新启动程序。更多内容请阅读 docs 文件夹中的 GUIDE.md 文件。",
        "",
        "## 常见问题",
        "",
        "有用户反映在高分辨率屏幕上缩放比例为 150 % 时文字看起来有些模糊。把线条和填充对齐到像素边界以后，这个问题就解决了。如果仍然觉得不清楚，可以设置环境变量切换绘制方式，然后再试一次。",
        "打开很长的文档时如果感觉变慢，可以关闭自动换行，或者把窗口稍微拉宽一些。一行越长，每次改变宽度时需要测量的文字就越多。");

    public static TheoryData<string> Paragraphs => new() { "ja", "zh", "zh-messages" };

    [Fact]
    public void AWordCutShortThatHasNoCuts_GetsThemAtItsFirstCut()
    {
        // A line's width shapes its word without cuts (as a TextArea keeps a hard line's width), and a
        // table shapes a word under MinCutWord without them. Cut short, the word gets its cuts once,
        // and its later cuts are read off them, the reference's widths to the bit.
        foreach (string word in new[] { Lines[0], Lines[1], Lines[2], "office", "東京タワー" })
        {
            var shaper = new CountingShaper(new SkiaTextShaper());
            var pipeline = new TextPipeline(new SkiaFontCollection(), shaper);
            var reference = new TextPipeline(new SkiaFontCollection(), new SkiaTextShaper()) { PrefixesFromCuts = false };
            pipeline.MeasureWidth(word, Sans);
            var table = pipeline.MeasurePrefixes(word, 0, word.Length, Sans)!;
            var expected = reference.MeasurePrefixes(word, 0, word.Length, Sans)!;
            int before = shaper.Calls;
            for (int n = 1; n < word.Length; n++)
                Assert.Equal(BitConverter.SingleToInt32Bits(expected.Width(n)), BitConverter.SingleToInt32Bits(table.Width(n)));
            int cutsShaped = pipeline.PrefixesShaped;
            // Its words' cuts made once each (a font run is a word of its own), and the refused cuts.
            Assert.True(shaper.Calls - before - cutsShaped < word.Length - 1,
                $"'{word}': {shaper.Calls - before} shaper calls for {word.Length - 1} cuts, {cutsShaped} of them refused");
            Assert.True(pipeline.PrefixesCut >= (word.Length - 1) / 2, $"'{word}': {pipeline.PrefixesCut} cuts read of {word.Length - 1}");
        }
    }

    [Fact]
    public void ANewWidth_ReadsTheWordsItsTablesCutShort_OffCutsMadeOnce()
    {
        // Measured on Windows: 326 calls against the reference's 718 (0.45); without the table's end
        // read off the word to its natural end 484 (0.67), without that and cuts made at need (round
        // 12's cuts alone) 0.7 and more.
        string text = Japanese + "\n" + Chinese;
        var (calls, rows, _) = NewWidths(new TextPipeline(new SkiaFontCollection(), new CountingShaper(new SkiaTextShaper())), text);
        var (reference, _, _) = NewWidths(new TextPipeline(new SkiaFontCollection(), new CountingShaper(new SkiaTextShaper()))
            { PrefixesFromCuts = false }, text);
        Assert.True(rows > 500, $"only {rows} rows");
        Assert.True(calls.Sum() * 16 < reference.Sum() * 9,
            $"{string.Join(", ", calls)} = {calls.Sum()} shaper calls at new widths, the reference {reference.Sum()}");
    }

    // Chinese as a translated README has it (gettext's messages): commands, options, numbers and
    // %s set among the ideographs, some with spaces around them and some without.
    private static readonly string ChineseMessages = string.Join('\n',
        "提示：用 dotnet build 构建以后，运行%s来启动示例程序；如果启动失败，请查看第2行第%d列的错误信息，详见 GUIDE.md 的第3节。配置项 'ui.scale=1.5' 已被弃用，请改用%s，旧的设置将在 HultUI 2.0 中移除。",
        "%s 期望一个非负整数以及可选的k/m/g后缀，必须介于 0（不含）和 1 之间，否则使用默认值%s。无法打开文件 '%s'：没有找到第%d个参数，也没有找到pkg-config的输出；请检查 PATH 环境变量，然后再试一次。",
        "（使用 \"git restore <文件>...\" 丢弃工作区的改动）分支 '%s' 设置为跟踪 '%s'，要么合并提交%s要么更新至已合并这些变更的提交；新的引用将存储于 refs/remotes/%s，共%d个对象，压缩后为%s。",
        "窗口的大小改变时，文本区域里的每一行都按新的宽度重新排列，一行有200个字符时每次需要测量的文字就越多；把HarfBuzz和Skia的结果按词记住，并记下哪些位置可以安全地切开，下一次就不必再整形。");

    [Fact]
    public void ANewWidth_MakesNoWordToItsNaturalEnd_ForASegmentTheCacheHas_OrPastALatinName()
    {
        // A table's last word is read off the word to its natural end only where the word cache does
        // not have the segment the table cuts, and that word stops where ASCII letters or digits
        // meet ideographs when that is past the table's end. Measured on Windows: 750 characters
        // shaped with their cuts at the ten new widths; 936 with the cached segment read off the word
        // to its natural end, 874 with that word run on to the next space, 1,123 with both (round 13).
        string text = Chinese + "\n" + ChineseMessages;
        var shaper = new CountingShaper(new SkiaTextShaper());
        var (calls, rows, chars) = NewWidths(new TextPipeline(new SkiaFontCollection(), shaper), text);
        var (reference, _, _) = NewWidths(new TextPipeline(new SkiaFontCollection(), new CountingShaper(new SkiaTextShaper()))
            { PrefixesFromCuts = false }, text);
        Assert.True(rows > 300, $"only {rows} rows");
        Assert.True(chars < 830, $"{chars} characters shaped with cuts at new widths in {calls.Sum()} calls (the reference {reference.Sum()})");
    }

    // The shaper calls of each of ten new widths of `text`, a character or so apart (the rows move),
    // opened at one width and resized to two more first, as a user drags a pane; their rows, and the
    // characters shaped with their cuts at those widths.
    private static (List<int> Calls, int Rows, int CharsWithCuts) NewWidths(TextPipeline pipeline, string text)
    {
        var shaper = (CountingShaper)pipeline.Shaper;
        Wrap(pipeline, text, 520f, Sans);
        Wrap(pipeline, text, 470f, Sans);
        Wrap(pipeline, text, 495f, Sans);
        var calls = new List<int>();
        int rows = 0, chars = shaper.CharsWithCuts;
        for (float width = 450f; width > 300f; width -= 15f)
        {
            int before = shaper.Calls;
            rows += Wrap(pipeline, text, width, Sans).Count;
            calls.Add(shaper.Calls - before);
        }
        return (calls, rows, shaper.CharsWithCuts - chars);
    }

    [Theory]
    [MemberData(nameof(Paragraphs))]
    public void WrappingAtNewWidths_ReadsOffCuts_TheReferencesWidthsAndRows(string language)
    {
        string text = language switch { "ja" => Japanese, "zh" => Chinese, _ => ChineseMessages };
        var withCuts = new TextPipeline(new SkiaFontCollection(), new SkiaTextShaper());
        var reference = new TextPipeline(new SkiaFontCollection(), new SkiaTextShaper()) { PrefixesFromCuts = false };
        var styles = new[] { Sans, Sans with { FontSize = 15f }, Sans with { LetterSpacing = 0.6f } };
        int compared = 0;
        foreach (var style in styles)
        {
            foreach (float width in new[] { 420f, 380f, 400f, 401f, 399f, 300f, 301f, 250f, 520f, 519f })
            {
                var a = new List<(int, int, int)>();
                var b = new List<(int, int, int)>();
                var rowsA = Wrap(withCuts, text, width, style, a);
                var rowsB = Wrap(reference, text, width, style, b);
                Assert.Equal(rowsB, rowsA);
                Assert.Equal(b.Count, a.Count);
                for (int i = 0; i < a.Count; i++)
                    Assert.True(a[i] == b[i], $"{language} at {width}: from {a[i].Item1}, {a[i].Item2} chars: {BitConverter.Int32BitsToSingle(a[i].Item3)} where the reference has {BitConverter.Int32BitsToSingle(b[i].Item3)}");
                compared += a.Count;
            }
        }
        Assert.True(compared > 2_000, $"only {compared} widths compared");
        Assert.True(withCuts.PrefixesCut > 200, $"{withCuts.PrefixesCut} read off cuts");
        Assert.Equal(0, reference.PrefixesCut);
    }

    [Fact]
    public void ALinesWordTheCacheLetsGo_IsNotShapedAgainForItsCuts()
    {
        // 800 lines of 160 ideographs each (no spaces, no punctuation: one font run), none alike:
        // wrapped at 300 px, the tables' words of one width outgrow the word cache, which lets the
        // lines' words go before the next width reads them.
        var rng = new Random(13);
        var lines = Enumerable.Range(0, 800)
            .Select(_ => string.Concat(Enumerable.Range(0, 160).Select(_ => (char)(0x4E00 + rng.Next(3000)))))
            .ToArray();
        string text = string.Join('\n', lines);
        var shaper = new CountingShaper(new SkiaTextShaper());
        var pipeline = new TextPipeline(new SkiaFontCollection(), shaper);
        foreach (float width in new[] { 300f, 285f, 315f, 270f, 330f })
            Wrap(pipeline, text, width, Sans);
        // Each line's word to its natural end (the whole line) is made with its cuts once at most.
        int made = lines.Count(l => shaper.WithCuts.ContainsKey(l));
        int again = lines.Count(l => shaper.WithCuts.GetValueOrDefault(l) > 1);
        Assert.True(pipeline.WordCacheUse.Bytes > TextPipeline.WordCacheBytes * 9 / 10, "the word cache did not fill");
        Assert.True(made > lines.Length / 2, $"{made} of {lines.Length} lines' words made with their cuts");
        // A slot of the words made that two share forgets one, which is made again once (a few here).
        Assert.True(again < lines.Length / 10, $"{again} of {lines.Length} lines' words made again");
    }

    // The row starts of `text` wrapped at `wrapWidth`, as a TextArea wraps it: a hard line whose
    // width (measured once, as the TextArea keeps it) fits stays whole; a longer one is broken row
    // by row as TextAreaElement.FindLineBreak breaks it, with one prefix table per row from a guess
    // at a line's length and probes around it, at a space where there is one. The rows of the first
    // 40 lines are drawn (laid out on one line), as on screen. Every width read goes to `log`.
    private static List<int> Wrap(TextPipeline pipeline, string text, float wrapWidth, ResolvedInheritedStyle style,
        List<(int, int, int)>? log = null)
    {
        var rows = new List<int>();
        float charWidth = style.FontSize * 0.55f;
        int lineStart = 0;
        while (lineStart <= text.Length)
        {
            int hardEnd = text.IndexOf('\n', lineStart);
            if (hardEnd < 0) hardEnd = text.Length;
            rows.Add(lineStart);
            int length = hardEnd - lineStart;
            bool fits = length == 0 || (length <= wrapWidth / charWidth
                                        && pipeline.MeasureWidth(text.AsSpan(lineStart, length), style) <= wrapWidth);
            for (int start = lineStart; !fits && start < hardEnd;)
            {
                int end = FindLineBreak(pipeline, text, start, hardEnd, wrapWidth, style, ref charWidth, log);
                if (rows.Count <= 40)
                    pipeline.Layout(text.Substring(start, end - start), style, wrapWidth, 1, TextOverflow.Clip);
                if (end >= hardEnd) break;
                rows.Add(end);
                start = end;
            }
            lineStart = hardEnd + 1;
        }
        return rows;
    }

    // TextAreaElement.FindLineBreak with no clusters, reading the pipeline's prefix tables.
    private static int FindLineBreak(TextPipeline pipeline, string text, int start, int hardEnd, float wrapWidth,
        ResolvedInheritedStyle style, ref float charWidth, List<(int, int, int)>? log)
    {
        ITextPrefixWidths? prefixes = null;
        float Measure(int chars)
        {
            if (prefixes is null || chars > prefixes.Length)
                prefixes = pipeline.MeasurePrefixes(text, start, Math.Min(hardEnd - start, chars + chars / 2 + 16), style)!;
            float w = prefixes.Width(chars);
            log?.Add((start, chars, BitConverter.SingleToInt32Bits(w)));
            return w;
        }
        int lo = 1, hi = int.MaxValue;
        bool bounded = false;
        float loWidth = 0;
        int probe = Math.Min(Math.Max((int)(wrapWidth / Math.Max(charWidth, 0.01f)), 1), hardEnd - start);
        int step = 0;
        for (int round = 0; ; round++)
        {
            int end = start + probe;
            float width = Measure(probe);
            if (probe >= 8) charWidth = width / probe;
            if (width <= wrapWidth)
            {
                if (end == hardEnd) return hardEnd;
                lo = probe;
                loWidth = width;
            }
            else
            {
                hi = probe - 1;
                bounded = true;
            }
            if (lo >= hi) break;
            int predicted = lo + (int)((wrapWidth - loWidth) / Math.Max(charWidth, 0.01f));
            if (!bounded)
            {
                probe = Math.Min(Math.Max(predicted + 1, lo + 1) + step, hardEnd - start);
                step = step == 0 ? Math.Max(1, lo / 8) : step * 2;
            }
            else
                probe = round % 2 == 0 ? Math.Clamp(predicted, lo + 1, hi) : (lo + hi + 1) / 2;
        }
        int breakAt = start + lo;
        while (breakAt > start && !char.IsWhiteSpace(text[breakAt - 1]))
            breakAt--;
        return breakAt > start ? breakAt : start + lo;
    }

    // Counts the calls into a shaper that can also say where its runs can be cut.
    private sealed class CountingShaper(SkiaTextShaper inner) : ITextShaper, IShapesWithCuts
    {
        public int Calls;

        // How many times each text was shaped with its cuts, and their characters.
        public readonly Dictionary<string, int> WithCuts = new();
        public int CharsWithCuts;

        public ShapedRun Shape(string text, IResolvedFont font, float fontSize, bool rtl = false)
        {
            Calls++;
            return inner.Shape(text, font, fontSize, rtl);
        }

        ShapedRun IShapesWithCuts.Shape(string text, IResolvedFont font, float fontSize, out RunCuts? cuts)
        {
            Calls++;
            WithCuts[text] = WithCuts.GetValueOrDefault(text) + 1;
            CharsWithCuts += text.Length;
            return ((IShapesWithCuts)inner).Shape(text, font, fontSize, out cuts);
        }
    }
}
