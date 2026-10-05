using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Platform;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Core.Tests.Elements;

/// <summary>
/// A masked field keeps its value to itself. The clipboard, which any application can read,
/// never gets it; assistive tech gets the bullets the screen shows, and word that it is a
/// password. Typing, pasting and <see cref="TextInputElement.TextChanged"/> still work on the
/// real text — that is the field's job.
///
/// <para>Each "does nothing" test has an unmasked twin that does something, so a harness that
/// never reached the clipboard or the tree cannot pass them all.</para>
/// </summary>
public class MaskedTextInputTests
{
    private const string Secret = "hunter2";
    private static readonly string Bullets = new('●', Secret.Length);

    private readonly LayoutEngine _layout = new(new StubMeasureContext());

    private TextInputElement Field(string text, bool masked, StubClipboard? clipboard = null)
    {
        var field = TextInputElement.New(text);
        field.Masked = masked;
        field.Clipboard = clipboard;
        field.Box.Width = 200;
        field.Box.Height = 30;
        var container = BoxElement.New();
        container.Box.Width = 400;
        container.Box.Height = 400;
        container.AddChild(field);
        StyleResolver.Resolve(container, Theme.Light.ToResolvedStyle());
        _layout.PerformLayout(container, 400, 400);
        EventDispatcher.DispatchDirect(new FocusEvent(), field);
        return field;
    }

    private static void Ctrl(Element field, KeyCode key) =>
        EventDispatcher.Dispatch(new KeyDownEvent { Key = key, Modifiers = KeyModifiers.Ctrl }, field);

    private static ContextMenuItem Item(TextInputElement field, string label) =>
        field.ContextMenuItems!().Single(i => i.Label == label);

    // ──── The clipboard ────

    [Theory]
    [InlineData(false, Secret)]
    [InlineData(true, "before")]
    public void CtrlCCopiesOnlyWhatIsShown(bool masked, string clipboardAfter)
    {
        var clipboard = new StubClipboard { Text = "before" };
        var field = Field(Secret, masked, clipboard);

        Ctrl(field, KeyCode.A);
        Ctrl(field, KeyCode.C);

        Assert.Equal(clipboardAfter, clipboard.Text);
    }

    /// <summary>Cut does nothing at all on a masked field. Deleting the selection without
    /// copying it would lose the text and look like a cut that worked.</summary>
    [Theory]
    [InlineData(false, Secret, "")]
    [InlineData(true, "before", Secret)]
    public void CtrlXCutsOnlyWhatIsShown(bool masked, string clipboardAfter, string textAfter)
    {
        var clipboard = new StubClipboard { Text = "before" };
        var field = Field(Secret, masked, clipboard);

        Ctrl(field, KeyCode.A);
        Ctrl(field, KeyCode.X);

        Assert.Equal(clipboardAfter, clipboard.Text);
        Assert.Equal(textAfter, field.Text);
    }

    /// <summary>Pasting a password from a password manager is how many people type one.</summary>
    [Fact]
    public void PasteStillWorks()
    {
        string? reported = null;
        var field = Field("", masked: true, new StubClipboard { Text = Secret });
        field.TextChanged = t => reported = t;

        Ctrl(field, KeyCode.V);

        Assert.Equal(Secret, field.Text);
        Assert.Equal(Secret, reported);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheMenuGreysOutCutAndCopyButNotPaste(bool masked)
    {
        var clipboard = new StubClipboard { Text = "before" };
        var field = Field(Secret, masked, clipboard);
        Ctrl(field, KeyCode.A);

        Assert.Equal(masked, Item(field, "Cut").IsDisabled);
        Assert.Equal(masked, Item(field, "Copy").IsDisabled);
        Assert.False(Item(field, "Paste").IsDisabled);
        Assert.False(Item(field, "Select All").IsDisabled);

        // A disabled item is still an action someone could run; it must not copy either.
        Item(field, "Copy").Action!();
        Assert.Equal(masked ? "before" : Secret, clipboard.Text);
    }

    // ──── The screen ────
    //
    // What a masked field draws, highlights and hits has to be measured on the bullets. The
    // text's own widths are the text: a highlight as wide as "iii" rather than three bullets,
    // or a caret that lands by the real glyphs, gives away what the bullets hide.

    private static void Press(Element field, KeyCode key) =>
        EventDispatcher.Dispatch(new KeyDownEvent { Key = key }, field);

    /// <summary>The input-method preedit is drawn in the middle of the value, and the value
    /// around it was drawn as itself — the whole password in clear for as long as a
    /// composition lasted. The preedit stays visible: it is being typed, and the input
    /// method's own candidate window shows it anyway.</summary>
    [Theory]
    [InlineData(false, "hunniter2")]
    [InlineData(true, "●●●ni●●●●")]
    public void ComposingShowsThePreeditAmongTheBullets(bool masked, string drawn)
    {
        var field = Field(Secret, masked);
        Press(field, KeyCode.Home);
        for (int i = 0; i < 3; i++) Press(field, KeyCode.Right);
        EventDispatcher.Dispatch(new TextEditingEvent { Text = "ni" }, field);

        var ctx = new GlyphContext();
        field.Render(ctx);

        Assert.Equal(drawn, Assert.Single(ctx.Texts));
    }

    [Theory]
    [InlineData(false, 42f)] // h u n t e r 2: six wide each
    [InlineData(true, 56f)]  // seven bullets: eight wide each
    public void TheSelectionCoversTheBullets(bool masked, float width)
    {
        var field = Field(Secret, masked);
        Ctrl(field, KeyCode.A);

        var ctx = new GlyphContext();
        field.Render(ctx);

        var highlight = Assert.Single(ctx.Rects, r => r.Fill == field.SelectionColor);
        Assert.Equal(width, highlight.Rect.Width);
    }

    /// <summary>Two bullets in is index 2. Measured on "WWWW" it was index 1: the caret
    /// landed somewhere the bullets do not say, and where it landed told the glyph widths.</summary>
    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    public void AClickLandsBetweenTheBulletsItHits(bool masked, int caret)
    {
        var field = Field("WWWW", masked);
        field.Render(new GlyphContext()); // the field hit-tests with the context it last drew with

        var box = field.Layout.AbsoluteContentBox;
        EventDispatcher.Dispatch(new PointerDownEvent { Position = new Point(box.X + 16, box.Y + 5), Button = 0 }, field);

        Assert.Equal(caret, field.CursorPosition);
    }

    /// <summary>A field with no width of its own grows with its content; that has to be the
    /// bullets' width, or the field's edge traces the text.</summary>
    [Theory]
    [InlineData(false, 20 * 14f)]
    [InlineData(true, 20 * 8f)]
    public void ANaturalWidthIsTheBulletsWidth(bool masked, float textWidth)
    {
        var field = Field(new string('W', 20), masked);

        var size = field.MeasureContent(float.PositiveInfinity, float.PositiveInfinity, new GlyphContext());

        Assert.Equal(textWidth + field.CursorWidth, size.Width);
    }

    /// <summary>The arrow keys move the caret visually on a line that mixes directions —
    /// which, worked out on the text, would trace where its right-to-left runs are.</summary>
    [Theory]
    [InlineData(false, Secret)]
    [InlineData(true, "●●●●●●●")]
    public void TheArrowKeysMoveOverWhatIsShown(bool masked, string measured)
    {
        var field = Field(Secret, masked);
        var ctx = new GlyphContext();
        field.Render(ctx);

        Press(field, KeyCode.Left);

        Assert.Equal(measured, ctx.MovedOver);
    }

    // ──── One word ────
    //
    // A masked value is one word, as a browser's password field is. Anything that stops at a
    // word boundary — a Ctrl+arrow, a Ctrl+Backspace, a double-click, an undo — would count out
    // where the password's spaces are. Each unmasked twin stops inside the value, so the masked
    // cases are not passing on a value with no boundary in it.

    private const string Phrase = "correct horse";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CtrlArrowsGoToTheEnds(bool masked)
    {
        var field = Field(Phrase, masked);

        Ctrl(field, KeyCode.Left);
        int left = field.CursorPosition;
        Press(field, KeyCode.Home);
        Ctrl(field, KeyCode.Right);
        int right = field.CursorPosition;

        if (masked)
        {
            Assert.Equal(0, left);
            Assert.Equal(Phrase.Length, right);
        }
        else
        {
            Assert.InRange(left, 1, Phrase.Length - 1);
            Assert.InRange(right, 1, Phrase.Length - 1);
        }
    }

    [Theory]
    [InlineData(false, "correct ")]
    [InlineData(true, "")]
    public void CtrlBackspaceErasesTheWholeWord(bool masked, string left)
    {
        var field = Field(Phrase, masked);

        Ctrl(field, KeyCode.Backspace);

        Assert.Equal(left, field.Text);
    }

    [Theory]
    [InlineData(false, "horse")]
    [InlineData(true, Phrase)]
    public void ADoubleClickSelectsTheWholeWord(bool masked, string selected)
    {
        var field = Field(Phrase, masked);
        field.Render(new GlyphContext());

        // Inside "horse": index 10, at the width each glyph is drawn.
        var box = field.Layout.AbsoluteContentBox;
        float x = box.X + (masked ? 8 : 6) * 10 + 1;
        EventDispatcher.Dispatch(new PointerDownEvent { Position = new Point(x, box.Y + 5), Button = 0, ClickCount = 2 }, field);

        Assert.Equal(selected, field.SelectedText);
    }

    [Theory]
    [InlineData(false, "correct ")]
    [InlineData(true, "")]
    public void UndoTakesBackTheWholeRun(bool masked, string left)
    {
        var field = Field("", masked);
        foreach (char c in Phrase)
            EventDispatcher.Dispatch(new TextInputEvent { Text = c.ToString() }, field);

        Ctrl(field, KeyCode.Z);

        Assert.Equal(left, field.Text);
    }

    /// <summary>Widths that differ per glyph, unlike the stub's even ones, so a highlight or a
    /// hit measured on the text cannot pass for one measured on the bullets.</summary>
    private sealed class GlyphContext : NullRenderContext, IRenderContext, IMeasureContext
    {
        public List<string> Texts { get; } = [];
        public List<(Rect Rect, Color Fill)> Rects { get; } = [];
        public string? MovedOver { get; private set; }

        private static float Width(char c) => c switch { '●' => 8f, 'i' => 2f, 'W' => 14f, _ => 6f };

        public override Size MeasureText(string text, ResolvedInheritedStyle style) =>
            new(text.Sum(Width), style.FontSize);

        public Size MeasureText(string text, ResolvedInheritedStyle style, float maxWidth, int? maxLines,
            TextOverflow overflow = TextOverflow.Clip) => new(text.Sum(Width), style.FontSize);

        public override void DrawText(string text, Rect bounds, ResolvedInheritedStyle style,
            TextOverflow overflow, int? maxLines) => Texts.Add(text);

        public override void DrawRect(Rect rect, Color fill) => Rects.Add((rect, fill));

        bool IRenderContext.TryMoveCaretVisually(string lineText, ResolvedInheritedStyle style, int offset, bool right, out int result)
        {
            MovedOver = lineText;
            result = offset;
            return false;
        }
    }

    // ──── Assistive tech ────

    private static BoxElement AppRoot(Element child)
    {
        var root = BoxElement.New();
        root.Role = AccessibilityRole.Application;
        root.Box.Width = 400;
        root.Box.Height = 400;
        root.AddChild(child);
        return root;
    }

    private AccessibilityNode Node(Element root)
    {
        _layout.PerformLayout(root, 400, 400);
        return AccessibilityTreeBuilder.Build(root).Children.Single();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AReaderIsGivenTheBulletsAndToldItIsAPassword(bool masked)
    {
        var input = new TextInputElement { Text = Secret, Masked = masked };
        input.AccessibleName = "Password";

        var node = Node(AppRoot(input));

        Assert.Equal(AccessibilityRole.TextInput, node.Role);
        Assert.Equal(masked, node.IsPassword);
        Assert.Equal(masked ? Bullets : Secret, node.Value);
    }

    /// <summary>A show-password toggle, both ways: the reader hears the role and the value
    /// change, and the value it is sent on masking is the bullets.</summary>
    [Fact]
    public void MaskingALiveFieldIsAChangeAReaderHears()
    {
        var input = new TextInputElement { Text = Secret };
        var root = AppRoot(input);
        _layout.PerformLayout(root, 400, 400);
        var before = AccessibilityTreeBuilder.Build(root);

        input.Masked = true;
        _layout.PerformLayout(root, 400, 400);
        var masked = AccessibilityTreeBuilder.Build(root);
        var diff = AccessibilityTreeDiffer.Diff(before, masked);

        Assert.Equal(
            new[] { AccessibilityProperties.IsPassword, AccessibilityProperties.Value },
            diff.PropertyChanged.Select(c => c.PropertyName).Order());
        Assert.All(diff.PropertyChanged, c => Assert.Equal(Bullets, c.Node.Value));

        input.Masked = false;
        var revealed = AccessibilityTreeBuilder.Build(root);
        var back = AccessibilityTreeDiffer.Diff(masked, revealed);

        Assert.Contains(back.PropertyChanged, c => c.PropertyName == AccessibilityProperties.IsPassword && !c.Node.IsPassword);
        Assert.Equal(Secret, revealed.Children.Single().Value);
    }

    /// <summary>Without the notification the change waits for the next layout-driven rebuild,
    /// which a toggle that only repaints never causes.</summary>
    [Fact]
    public void SettingTheMaskTellsTheWindowAtOnce()
    {
        var input = new TextInputElement { Text = Secret };
        var root = AppRoot(input);
        var observer = new RecordingObserver();
        root.AccessibilityObserver = observer;

        input.Masked = true;
        input.Masked = true; // no change, no news

        var (element, property) = Assert.Single(observer.Changes);
        Assert.Same(input, element);
        Assert.Equal(AccessibilityProperties.IsPassword, property);
    }

    // ──── The builder ────

    /// <summary>Named Password, not Masked: <c>input.Masked()</c> would bind to the property.</summary>
    [Fact]
    public void PasswordSetsTheMaskAndClearsIt()
    {
        var input = TextInput.New(Secret).Password();
        Assert.True(input.Masked);

        input.Password(false);
        Assert.False(input.Masked);
    }

    private sealed class RecordingObserver : IElementPropertyObserver
    {
        public List<(Element, string)> Changes { get; } = [];
        public void OnElementPropertyChanged(Element element, string propertyName) => Changes.Add((element, propertyName));
    }

    private sealed class StubClipboard : IClipboard
    {
        public string? Text { get; set; }
        public string? GetText() => Text;
        public void SetText(string text) => Text = text;
        public bool HasFormat(string mimeType) => false;
        public byte[]? GetData(string mimeType) => null;
        public void SetData(IReadOnlyDictionary<string, byte[]> data) => throw new NotSupportedException();
        public void Clear() => Text = null;
    }
}
