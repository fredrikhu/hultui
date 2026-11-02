// A rich text editor filling the window, holding a paragraph and a 300-row, 10-column table of
// one- and two-word cells under a header row: the document the perf suite's
// "richtext/... of a 300x10 table" rows mount. The DevTools HUD starts on with continuous redraw,
// so the FPS it shows is how fast a frame can be drawn. Scroll with the wheel, the scroll bar or
// Ctrl+Home / Ctrl+End and read the FPS at each depth: it ought to stay the same from top to bottom.
// F12 cycles the HUD: on and redrawing, off, on.
using HultUI;
using HultUI.Core.Builders;
using HultUI.Core.RichText;

var rng = new Random(17);
string[] adjectives = ["pretty", "large", "big", "small", "tall", "short", "long", "handsome", "plain", "quaint",
    "clean", "elegant", "easy", "angry", "crazy", "helpful", "mushy", "odd", "unsightly", "adorable", "important",
    "inexpensive", "cheap", "expensive", "fancy"];
string[] colours = ["red", "yellow", "blue", "green", "pink", "brown", "purple", "white", "black", "orange"];
string[] nouns = ["table", "chair", "house", "bbq", "desk", "car", "pony", "cookie", "sandwich", "burger", "pizza",
    "mouse", "keyboard"];
string Words(int count) => string.Join(' ', Enumerable.Range(0, count).Select(i => (i % 3) switch
{
    0 => adjectives[rng.Next(adjectives.Length)],
    1 => colours[rng.Next(colours.Length)],
    _ => nouns[rng.Next(nouns.Length)],
}));

var blocks = new List<RichBlock>(3_001) { RichBlock.Paragraph("Before the table") };
for (int row = 0; row < 300; row++)
    for (int column = 0; column < 10; column++)
        blocks.Add(RichBlock.Cell(row, column, row == 0 ? $"Column {column + 1}" : Words(1 + (row + column) % 2)));

var editor = RichTextEditor.New(new RichDocument(blocks)).FlexGrow(1);

using var app = new Application("Rich text table, 300 x 10", 1200, 800);
app.DevTools.IsEnabled = true;
app.DevTools.ContinuousRedraw = true;
app.SetRoot(Flex.Column().FlexGrow(1).Padding(8).Children(editor));
editor.Focus();
app.Run();
