using System.Text;
using HultUI;
using HultUI.Core.Builders;
using HultUI.Core.Platform;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;

// ─────────────────────────────────────────────────────────────────────────────
// One selection, several formats.
//
// The system clipboard is not a value, it is a set of alternative
// representations of one selection, and the receiving app picks whichever it
// understands. That is why "copy this diff" wants to put HTML *and* plain text
// on the clipboard at once: paste into a document and you want colour, paste
// into a terminal and you want the text.
//
// SetData takes them together because that is how the platform models it — a
// second call would replace the first, not add to it.
//
// To see that it really left this process: copy, then paste into a word
// processor or browser (you get the colours) and into Notepad (you get the
// plain text). The formats panel below reads the live clipboard back, so it
// also shows what other applications put there.
// ─────────────────────────────────────────────────────────────────────────────

var app = new Application("Clipboard formats", 780, 620);

const string Html =
    "<p style='font-family:sans-serif'>" +
    "<span style='color:#16a34a'>+ added this line</span><br/>" +
    "<span style='color:#dc2626'>- removed that one</span></p>";

const string Plain = "+ added this line\n- removed that one";

var status = Text.New("Nothing copied yet.").FontSize(13f);
var formats = Text.New("").FontSize(12.5f).FontFamily("monospace");
var pasted = Text.New("").FontSize(12.5f).FontFamily("monospace");

// Formats worth reporting on. Anything else an app offers is still readable
// with GetData — these are just the ones with names worth printing.
(string Mime, string Label)[] known =
[
    (ClipboardFormats.PlainText, "plain text"),
    (ClipboardFormats.Html, "HTML"),
    (ClipboardFormats.Png, "PNG"),
    (ClipboardFormats.Rtf, "RTF"),
    (ClipboardFormats.Uris, "file list"),
];

void RefreshFormats()
{
    var clipboard = app.Clipboard;
    var lines = new StringBuilder();
    foreach (var (mime, label) in known)
    {
        bool present = clipboard.HasFormat(mime);
        int bytes = present ? clipboard.GetData(mime)?.Length ?? 0 : 0;
        lines.AppendLine(present
            ? $"  ✓ {label,-11} {mime,-26} {bytes} bytes"
            : $"    {label,-11} {mime,-26} —");
    }
    formats.Content = lines.ToString().TrimEnd();
}

void CopyBoth()
{
    // The whole API surface for a multi-format copy.
    app.Clipboard.SetData(new Dictionary<string, byte[]>
    {
        [ClipboardFormats.Html] = Encoding.UTF8.GetBytes(Html),
        [ClipboardFormats.PlainText] = Encoding.UTF8.GetBytes(Plain),
    });

    status.Content = "Copied as HTML and as plain text. Paste into a word processor " +
                     "for the first, a terminal for the second.";
    RefreshFormats();
}

void CopyPlainOnly()
{
    app.Clipboard.SetText(Plain);
    status.Content = "Copied as plain text only — taking the clipboard again dropped the " +
                     "HTML alternative, which is what an owner change means.";
    RefreshFormats();
}

void PasteBack()
{
    var clipboard = app.Clipboard;
    var html = clipboard.GetData(ClipboardFormats.Html);

    pasted.Content =
        $"GetText():\n{Indent(clipboard.GetText() ?? "<nothing>")}\n\n" +
        $"GetData(text/html):\n{Indent(html is null ? "<not offered>" : Encoding.UTF8.GetString(html))}";
    RefreshFormats();
}

static string Indent(string value) =>
    string.Join('\n', value.Split('\n').Select(line => "    " + line));

app.SetRoot(Flex.Column(gap: 18).Padding(26).Children(
    Flex.Column(gap: 6).Children(
        Text.New("One selection, several formats").FontSize(21f).FontWeight(FontWeight.Bold),
        Text.New("The clipboard holds alternative representations of a single selection. " +
                 "Offer every one you can produce and the receiving app takes whichever it " +
                 "understands.")
            .FontSize(13f).MaxWidth(Dimension.Px(660))
    ),

    Flex.Row(gap: 10).Children(
        Button.New("Copy as HTML + text").OnClick(_ => CopyBoth()),
        Button.New("Copy as text only").OnClick(_ => CopyPlainOnly()),
        Button.New("Read the clipboard").OnClick(_ => PasteBack())
    ),

    status,

    Flex.Column(gap: 6).Children(
        Text.New("On the clipboard now").FontSize(14f).FontWeight(FontWeight.SemiBold),
        Box.New().Padding(12).CornerRadius(6f).Background(Theme.Light.SurfaceVariant)
            .Children(formats)
    ),

    Flex.Column(gap: 6).FlexGrow(1).Children(
        Text.New("Read back").FontSize(14f).FontWeight(FontWeight.SemiBold),
        Box.New().Padding(12).CornerRadius(6f).FlexGrow(1).Background(Theme.Light.SurfaceVariant)
            .Children(pasted)
    ),

    Text.New("The formats panel reads the real clipboard, so copying in another " +
             "application and pressing \"Read the clipboard\" shows what that one offered.")
        .FontSize(12f).MaxWidth(Dimension.Px(660))
));

RefreshFormats();
app.Run();
