using System.Text;
using HultUI;
using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.Icons;

namespace PerfSuite;

static partial class Scenarios
{
    private const int SwitchFiles = 200;

    private static IEnumerable<Scenario> DocumentSwitch()
    {
        yield return new Scenario("component/cursor key over a 200-file list beside its document, text landing a frame later",
            "A cursor walks down a virtual list of 200 source files beside the open file's document — a header and a read-only, syntax-coloured TextArea of 180 to 420 lines — that one component builds (Junction's Files view): each key opens the next file through a keyed AsyncLoader, so the key's frame builds the view with \"Loading…\" where the text goes and paints it, and the next frame takes the text in, builds the view again and paints the file; the step is both frames",
            ctx => DocumentSwitchWalk(ctx, textLater: true)) { Width = 1240, Height = 820 };

        yield return new Scenario("component/cursor key over a 200-file list beside its document, text in hand",
            "The same walk with every file's text in hand when the cursor moves: the key's frame builds the view once, with the next file's text in its TextArea, and paints it, so the pair shows what the loader's second build and frame cost",
            ctx => DocumentSwitchWalk(ctx, textLater: false)) { Width = 1240, Height = 820 };
    }

    /// <summary>
    /// The cursor walks down the list, one key a step, and wraps to the top past the last file. The
    /// key is posted, so the frame that follows takes it in, as a real frame takes in its events
    /// before it lays out and paints: a key pressed between two pumps would build the view before
    /// the next frame, and that frame's start, where a headless session lands loads, would hand the
    /// text over before "Loading…" was ever laid out. Both builds therefore run inside the step's
    /// frames, and show under other, not build. A step is two frames for both rows: the key's, and
    /// the one where the text lands (the twin has nothing left for it, and it renders nothing). The
    /// row reports the view's builds a step.
    /// </summary>
    private static Op DocumentSwitchWalk(Ctx ctx, bool textLater)
    {
        var view = new DocumentSwitchView(SourceFiles(ctx.Rng, SwitchFiles), textLater);
        var host = view.Mount();
        host.On<KeyDownEvent>(e =>
        {
            view.Press(e.Key);
            e.StopPropagation = true;
        });
        ctx.Mount(host);
        ctx.Pump(2);
        int buildsBefore = 0, perStep = textLater ? 2 : 1;
        Action press = () => ctx.Window.PressKey(KeyCode.Down);
        return new Op
        {
            Run = _ =>
            {
                buildsBefore = view.Builds;
                view.LastLoading = null;
                HultUI.Application.InvokeOnMainThread(press);
            },
            Frames = 2,
            Figure = () => view.Builds - buildsBefore,
            FigureUnit = "builds",
            Check = step =>
            {
                int built = view.Builds - buildsBefore;
                if (built != perStep) return $"the view built {built} times for one key, not {perStep}";
                if (textLater != view.LastLoading is not null) return textLater ? "no build showed \"Loading…\"" : "a build showed \"Loading…\"";
                if (view.LastLoading is { } loading && loading.Layout.AbsoluteContentBox.Width <= 0)
                    return "\"Loading…\" was never laid out: the text landed in the key's own frame";
                if (view.Verify((step + 1) % SwitchFiles) is { } wrong) return wrong;
                return ctx.Session.PumpFrame() ? "a frame after the step still rendered: the step did not wait for the text to land" : null;
            },
        };
    }

    /// <summary>The files the list shows, each with its text and colour spans made here, so neither
    /// row's steps pay for making them (Junction reads and colours a file on a worker).</summary>
    private static List<SourceFile> SourceFiles(Random rng, int count)
    {
        var files = new List<SourceFile>(count);
        for (int i = 0; i < count; i++)
        {
            string area = $"Area{i % 7}", name = $"{CodeText.Pascal(rng)}{i:000}.cs";
            files.Add(new SourceFile($"src/{area}/{name}", name, CodeText.Make(rng, $"Junction.{area}", 180 + rng.Next(241))));
        }
        return files;
    }
}

/// <summary>A file's text as a viewer shows it: the lines joined with '\n', the colour spans over the
/// whole text, its line count and its size. <see cref="Empty"/> is what a loader holds before it lands.</summary>
sealed record SourceText(string? Text, TextSpan[]? Spans, int Lines, int Bytes)
{
    public static readonly SourceText Empty = new(null, null, 0, 0);
}

/// <summary>A file in the list: its path, its name, its text.</summary>
sealed class SourceFile(string path, string name, SourceText text)
{
    public string Path { get; } = path;
    public string Name { get; } = name;
    public SourceText Text { get; } = text;
}

/// <summary>
/// C# source as a file viewer shows it, with the spans HultUI.Syntax's Dark+ colours give such text
/// — keywords, control keywords, types, method names, strings, numbers, comments — recorded while the
/// text is written, so setup needs no grammar: usings and a namespace, then classes of a few fields
/// and methods of four to ten statements each.
/// </summary>
static class CodeText
{
    public static readonly Color Plain = Color.FromHex("#D4D4D4"), Keyword = Color.FromHex("#569CD6"),
        Control = Color.FromHex("#C586C0"), Type = Color.FromHex("#4EC9B0"), Function = Color.FromHex("#DCDCAA"),
        String = Color.FromHex("#CE9178"), Number = Color.FromHex("#B5CEA8"), Comment = Color.FromHex("#6A9955");

    public static string Pascal(Random rng)
    {
        var sb = new StringBuilder(24);
        foreach (var word in Ui.Label(rng).Split(' ')) sb.Append(char.ToUpperInvariant(word[0])).Append(word, 1, word.Length - 1);
        return sb.ToString();
    }

    private static string Camel(Random rng)
    {
        var name = Pascal(rng);
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    /// <summary>About <paramref name="lines"/> lines of source in namespace <paramref name="ns"/>.</summary>
    public static SourceText Make(Random rng, string ns, int lines)
    {
        var w = new Writer(lines);
        w.Kw("using").P(" System;").NewLine();
        w.Kw("using").P(" System.Collections.Generic;").NewLine();
        w.Kw("using").P(" System.Linq;").NewLine();
        w.NewLine();
        w.Kw("namespace").P(" " + ns + ";").NewLine();
        while (w.Lines < lines - 8)
        {
            w.NewLine();
            w.Com($"/// <summary>{Ui.Paragraph(rng, 6 + rng.Next(8))}.</summary>").NewLine();
            w.Kw("public").P(" ").Kw("sealed").P(" ").Kw("class").P(" ").Ty(Pascal(rng)).NewLine();
            w.P("{").NewLine();
            for (int f = 2 + rng.Next(3); f > 0; f--)
            {
                w.P("    ").Kw("private").P(" ").Kw("readonly").P(" ");
                if (rng.Next(2) == 0) w.Kw("int").P(" _" + Camel(rng) + " = ").Num(rng.Next(1, 5_000)).P(";").NewLine();
                else w.Ty("List").P("<").Kw("string").P("> _" + Camel(rng) + " = ").Kw("new").P("();").NewLine();
            }
            while (w.Lines < lines - 14)
            {
                w.NewLine();
                Method(w, rng);
                if (rng.Next(5) == 0) break;
            }
            w.P("}").NewLine();
        }
        return w.Done();
    }

    private static void Method(Writer w, Random rng)
    {
        string a = Camel(rng), b = Camel(rng), list = "_" + Camel(rng);
        w.P("    ").Kw("public").P(" ").Kw(rng.Next(2) == 0 ? "int" : "string").P(" ").Fn(Pascal(rng)).P("(")
            .Kw("int").P(" " + a + ", ").Kw("string").P(" " + b + ")").NewLine();
        w.P("    {").NewLine();
        for (int s = 4 + rng.Next(7); s > 0; s--)
        {
            w.P("        ");
            switch (rng.Next(7))
            {
                case 0: w.Com("// " + Ui.Paragraph(rng, 5 + rng.Next(9))); break;
                case 1: w.Kw("var").P($" {Camel(rng)} = {a} + ").Num(rng.Next(1, 100)).P(";"); break;
                case 2:
                    w.Ctl("if").P($" ({a} > ").Num(rng.Next(1, 64)).P($" && {b}.Length < ").Num(rng.Next(1, 32)).P(") ")
                        .Ctl("return").P(" ").Fn(Pascal(rng)).P($"({a}, ").Str($"\"{Ui.Label(rng)}\"").P(");");
                    break;
                case 3:
                    w.Ctl("for").P(" (").Kw("int").P(" i = ").Num(0).P($"; i < {a}; i++) {list}.").Fn("Add").P("(i.").Fn("ToString").P("());");
                    break;
                case 4: w.P(list + ".").Fn("Add").P("(").Str($"\"{Ui.Label(rng)}\"").P($" + {b});"); break;
                case 5: w.Kw("string").P($" {Camel(rng)} = {b}.").Fn("Trim").P("() + ").Str($"\" {Ui.Paragraph(rng, 2 + rng.Next(4))}\"").P(";"); break;
                default:
                    w.Ctl("foreach").P(" (").Kw("var").P(" item ").Ctl("in").P($" {list}.").Fn("Where").P("(x => x.Length > ").Num(rng.Next(1, 9))
                        .P(")) ").Fn(Pascal(rng)).P("(item);");
                    break;
            }
            w.NewLine();
        }
        w.P("        ").Ctl("return").P(" " + a + ";").NewLine();
        w.P("    }").NewLine();
    }

    private sealed class Writer(int lines)
    {
        private readonly StringBuilder _text = new(lines * 48);
        private readonly List<TextSpan> _spans = new(lines * 5);

        public int Lines { get; private set; }

        public Writer P(string s) { _text.Append(s); return this; }

        public Writer NewLine() { _text.Append('\n'); Lines++; return this; }

        public Writer Kw(string s) => Span(s, Keyword);
        public Writer Ctl(string s) => Span(s, Control);
        public Writer Ty(string s) => Span(s, Type);
        public Writer Fn(string s) => Span(s, Function);
        public Writer Str(string s) => Span(s, String);
        public Writer Com(string s) => Span(s, Comment);
        public Writer Num(int n) => Span(n.ToString(System.Globalization.CultureInfo.InvariantCulture), Number);

        private Writer Span(string s, Color color)
        {
            _spans.Add(new TextSpan(_text.Length, s.Length, color));
            _text.Append(s);
            return this;
        }

        public SourceText Done()
        {
            while (_text.Length > 0 && _text[^1] == '\n') _text.Length--;
            var text = _text.ToString();
            return new SourceText(text, _spans.ToArray(), Lines, Encoding.UTF8.GetByteCount(text));
        }
    }
}

/// <summary>
/// The shape of Junction's Files view, which one component builds: a header bar over a tree pane (a
/// search box above a virtual list of file rows, the open file's in the accent fill) beside the open
/// file's pane (its path, size and line count over a read-only, syntax-coloured TextArea keyed by the
/// file). A cursor key opens the next file. With <paramref name="textLater"/> the text comes through a
/// <see cref="KeyedAsyncLoader{TKey, T}"/> keyed by the file, as Junction's does: the build after the
/// key finds the new file's loader empty and shows "Loading…", and the text's landing builds the view
/// again. Without it, the view reads the file's text straight away and builds once.
/// </summary>
sealed partial class DocumentSwitchView(List<SourceFile> files, bool textLater) : Component
{
    private const float RowHeight = 24f;
    private const string Repo = "C:/src/junction", Reference = "HEAD";

    private static readonly Color Bg = Color.FromHex("#101318"), Divider = Color.FromHex("#272C35"),
        TextStrong = Color.FromHex("#E5E7EB"), TextDim = Color.FromHex("#AEB3BC"), Muted = Color.FromHex("#737A85"),
        OpenFill = Color.FromHex("#1E2B4D"), Accent300 = Color.FromHex("#93B4FF");

    private static readonly FileIconPalette IconColours = new(
        Color.FromHex("#93B4FF"), Color.FromHex("#6F95F5"), Color.FromHex("#4C74DB"),
        Color.FromHex("#AEB3BC"), Color.FromHex("#8C929C"), Color.FromHex("#6B717B"));

    private static readonly ButtonStyle IconStyle = new() { Size = 28f, Padding = new Thickness(0) };

    // Bounded as Junction's is: a walk opens a new file a key, each pinning its text.
    private readonly KeyedAsyncLoader<string, SourceText> _loaders =
        new(SourceText.Empty, capacity: 24, comparer: StringComparer.Ordinal);

    private readonly Ref<VirtualListElement<FileRow>> _list = new();
    private int _open;
    private bool _reveal;

    /// <summary>The file the cursor is on, which is the one open; a write builds the view.</summary>
    [Reactive] public partial int Cursor { get; set; }

    /// <summary>How many times the view has built.</summary>
    public int Builds { get; private set; }

    public void Press(KeyCode key)
    {
        if (key == KeyCode.Down) Cursor = (Cursor + 1) % files.Count;
        else if (key == KeyCode.Up) Cursor = Math.Max(0, Cursor - 1);
        _reveal = true;
    }

    /// <summary>One row as the list sees it: the file, its place, and whether it is the open one. A
    /// key changes two.</summary>
    private readonly record struct FileRow(SourceFile File, int Index, bool Open);

    /// <summary>The rows as the list's items, each made when the list asks for it.</summary>
    private sealed class FileRows(DocumentSwitchView view) : IReadOnlyList<FileRow>
    {
        public int Count => view.Files.Count;

        public FileRow this[int index] => new(view.Files[index], index, index == view._open);

        public IEnumerator<FileRow> GetEnumerator()
        {
            for (int i = 0; i < Count; i++) yield return this[i];
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private List<SourceFile> Files => files;

    public override Element Build()
    {
        Builds++;
        _open = Cursor;
        var file = files[_open];

        var col = Flex.Column(gap: 0).FlexGrow(1).Background(Bg).TextColor(TextStrong);
        col.AddChild(Header());
        var row = Flex.Row(gap: 0).FlexGrow(1).AlignItems(AlignItems.Stretch);
        row.AddChild(TreePane());
        row.AddChild(ContentPane(file));
        col.AddChild(row);
        return col;
    }

    protected internal override void OnAfterBuild()
    {
        if (!_reveal) return;
        _reveal = false;
        // Rows are a fixed height, so index math needs no per-row layout.
        _list.Current?.ScrollRangeIntoView(_open * RowHeight, RowHeight);
    }

    private Element Header()
    {
        var bar = Flex.Row(gap: 10).FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(11, 16, 11, 16))
            .BorderColor(Divider).BorderWidth(new Thickness(0, 0, 1, 0));
        bar.AddChild(Phosphor.Draw("folders", 14f, Accent300).FlexShrink(0));
        bar.AddChild(Text.New("Files").FontSize(15f).FontWeight(FontWeight.SemiBold).FlexShrink(0));
        bar.AddChild(Text.New("4f2c9e1").FontFamily(FontFamilies.Monospace).FontSize(11.5f).TextColor(Muted).FlexShrink(0));
        bar.AddChild(Text.New("Files: open the next file as the cursor moves").FontSize(12f).TextColor(TextDim)
            .FlexGrow(1).FlexShrink(1).TextOverflow(TextOverflow.Ellipsis).MaxLines(1));
        bar.AddChild(Text.New($"{files.Count} files").FontSize(11f).TextColor(Muted).FlexShrink(0));
        bar.AddChild(IconButton("caret-up", "Previous commit (Ctrl ↑)"));
        bar.AddChild(IconButton("caret-down", "Next commit (Ctrl ↓)"));
        return bar;
    }

    private Element TreePane()
    {
        var pane = Flex.Column(gap: 0).Width(300).FlexShrink(0)
            .BorderColor(Divider).BorderWidth(new Thickness(0, 1, 0, 0));
        var search = Flex.Row(gap: 6).FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(6, 8, 6, 8))
            .BorderColor(Divider).BorderWidth(new Thickness(0, 0, 1, 0));
        var box = Flex.Row(gap: 6).AlignItems(AlignItems.Center).Height(30).FlexGrow(1)
            .Padding(new Thickness(0, 8, 0, 8)).Background(Bg)
            .BorderColor(Divider).BorderWidth(1f).CornerRadius(6f);
        box.AddChild(Phosphor.Draw("magnifying-glass", 12f, Muted).FlexShrink(0));
        box.AddChild(TextInput.New("").PlaceholderText("Press / to find a file")
            .Height(Dimension.Px(26)).FlexGrow(1).FlexShrink(1).FlexBasis(Dimension.Px(0))
            .FontSize(12f).TextColor(TextStrong));
        search.AddChild(box);
        pane.AddChild(search);

        var list = VirtualListElement<FileRow>.New()
            .ItemHeight(RowHeight)
            .Items(new FileRows(this))
            .RenderWith((r, _) => Row(r))
            .FlexGrow(1).Padding(new Thickness(6, 6, 6, 6));
        list.RowsAreComposite = true;
        list.Role = AccessibilityRole.ScrollView;
        list.WithRef(_list);
        pane.AddChild(list);
        return pane;
    }

    /// <summary>Rows the list's template has made.</summary>
    public int RowsMade { get; private set; }

    private Element Row(FileRow item)
    {
        RowsMade++;
        var row = Flex.Row(gap: 6).AlignItems(AlignItems.Center)
            .Height(RowHeight).FlexShrink(0)
            .Padding(new Thickness(0, 6, 0, 6))
            .CornerRadius(4f)
            .Cursor(CursorType.Pointer).Tooltip(item.File.Path);
        row.Key = "f:" + item.File.Path + (item.Open ? "#s" : "");
        row.Role = AccessibilityRole.TreeItem;
        if (item.Open) row.Background(OpenFill);
        row.AddChild(Box.New().Width(10).FlexShrink(0));
        row.AddChild(FileIcons.Draw(FileIcons.KindFor(item.File.Name), IconColours, size: 12f).FlexShrink(0));
        row.AddChild(Text.New(item.File.Name).FontSize(12f).TextColor(item.Open ? TextStrong : TextDim)
            .FlexGrow(1).FlexShrink(1).TextOverflow(TextOverflow.Ellipsis).MaxLines(1));
        int index = item.Index;
        row.OnClick(_ => { _reveal = true; Cursor = index; });
        return row;
    }

    private Element ContentPane(SourceFile file)
    {
        SourceText content;
        bool loading;
        if (textLater)
        {
            var key = $"{Repo}|{Reference}|{file.Path}";
            var loader = _loaders.For(key);
            // Off the UI thread: what a blob read and its colouring hand back, made at setup.
            loader.Load(key, () => file.Text);
            content = loader.Value;
            loading = loader.IsLoading;
        }
        else
        {
            content = file.Text;
            loading = false;
        }

        var pane = Flex.Column(gap: 0).FlexGrow(1);
        var header = Flex.Row(gap: 10).AlignItems(AlignItems.Center).FlexShrink(0)
            .Padding(new Thickness(8, 12, 8, 12))
            .BorderColor(Divider).BorderWidth(new Thickness(0, 0, 1, 0));
        header.AddChild(Text.New(file.Path).FontFamily(FontFamilies.Monospace).FontSize(12f).TextColor(TextStrong)
            .FlexGrow(1).FlexShrink(1).TextOverflow(TextOverflow.Ellipsis).MaxLines(1));
        if (content.Bytes > 0)
            header.AddChild(Text.New($"{content.Bytes / 1024.0:0.0} KB").FontSize(11f).TextColor(Muted).FlexShrink(0));
        if (content.Lines > 0)
        {
            header.AddChild(Text.New($"{content.Lines} lines").FontSize(11f).TextColor(Muted).FlexShrink(0));
            header.AddChild(BlameChip());
        }
        var open = IconButton("rocket-launch", "Open with the default app (Enter)");
        open.Key = "files-open";
        header.AddChild(open);
        var reveal = IconButton("folder-open", "Show in folder");
        reveal.Key = "files-reveal";
        header.AddChild(reveal);
        pane.AddChild(header);

        // Keyed: the two bodies are different elements in one slot.
        var body = Body(file, content, loading);
        body.Key = "files-body-source";
        pane.AddChild(body);
        return pane;
    }

    /// <summary>The "Loading…" label the last build that showed one made, for the check that its
    /// frame laid it out.</summary>
    public TextElement? LastLoading { get; set; }

    private Element Body(SourceFile file, SourceText content, bool loading)
    {
        if (loading && content.Text is null)
            return Box.New().FlexGrow(1).JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
                .Children(LastLoading = Text.New("Loading…").FontSize(13f).TextColor(Muted));

        var wrap = Box.New().FlexGrow(1).Overflow(Overflow.Hidden).Background(Bg)
            .Padding(new Thickness(6, 12, 6, 12));
        // One read-only TextArea holds the whole file; keyed by the file, so its scroll and
        // selection start over when another file opens.
        var area = TextArea.New(content.Text ?? "").ReadOnly().FlexGrow(1)
            .FontFamily(FontFamilies.Monospace).FontSize(12f).TextColor(CodeText.Plain);
        area.ScrollbarWidth = 7f;
        area.Spans(content.Spans);
        area.Key = "filebody:" + file.Path;
        area.IsFocusable = true;
        area.TabIndex = 0;
        wrap.AddChild(area);
        return wrap;
    }

    private static Element BlameChip()
    {
        var chip = Flex.Row(gap: 5).FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(4, 8, 4, 8)).CornerRadius(4f)
            .BorderColor(Divider).BorderWidth(1f)
            .Cursor(CursorType.Pointer).Tooltip("Blame — who last changed each line");
        chip.AddChild(Phosphor.Draw("user", 12f, Muted));
        chip.AddChild(Text.New("Blame").FontSize(11.5f).FontWeight(FontWeight.Medium).TextColor(TextDim));
        chip.Key = "blame-off";
        return chip;
    }

    private static ButtonElement IconButton(string glyph, string tooltip)
    {
        var b = Button.New(ButtonVariant.Secondary).Style(IconStyle);
        b.AddChild(Phosphor.Draw(glyph, 15f, TextStrong));
        b.Tooltip(tooltip);
        b.OnClick(_ => { });
        b.FlexShrink(0);
        return b;
    }

    /// <summary>Why the view on screen is not file <paramref name="cursor"/> open, with its text in
    /// the TextArea and its row the one filled, if it is not.</summary>
    public string? Verify(int cursor)
    {
        if (Cursor != cursor) return $"the cursor is on file {Cursor}, not {cursor}";
        var file = files[cursor];
        TextAreaElement? area = null;
        var open = new List<string>();
        void Walk(Element e)
        {
            if (e is TextAreaElement t) area = t;
            if (e.Key is { } key && key.StartsWith("f:", StringComparison.Ordinal) && key.EndsWith("#s", StringComparison.Ordinal))
                open.Add(key);
            foreach (var c in e.Children) Walk(c);
        }
        Walk(Mount());
        if (area is null) return $"no TextArea on screen for {file.Path}";
        if (!ReferenceEquals(area.Text, file.Text.Text)) return $"the TextArea does not hold {file.Path}'s text";
        if (!ReferenceEquals(area.Spans, file.Text.Spans)) return $"the TextArea does not have {file.Path}'s spans";
        if (area.Key != "filebody:" + file.Path) return $"the TextArea is keyed {area.Key}, not by {file.Path}";
        if (open.Count != 1 || open[0] != $"f:{file.Path}#s") return $"the filled rows are [{string.Join(", ", open)}], not {file.Path}'s alone";
        if (textLater && _loaders.For($"{Repo}|{Reference}|{file.Path}").IsLoading) return $"{file.Path} is still loading";
        return null;
    }
}
