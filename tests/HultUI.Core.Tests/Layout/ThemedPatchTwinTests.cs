using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using Xunit;
using TextB = HultUI.Core.Builders.Text;

namespace HultUI.Core.Tests.Layout;

/// <summary>
/// A themed control patched by a rebuild compares against a twin themed with the window's theme
/// (Element.PatchThemesTwin), so a patch that changes nothing leaves its layout standing, and one
/// whose twin themes to other sizes lays it out again. With no window theme to hand (an element
/// not in a window, or one the style pass has not reached) the twin cannot be themed, and the
/// patch marks layout as it always did.
/// </summary>
public class ThemedPatchTwinTests
{
    private sealed record Entry(string Name, long Size);

    private sealed class Host(Theme? theme) : IOverlayHost
    {
        public OverlayHandle ShowOverlay(Element content, float x, float y, bool dismissOnOutsideClick = true,
            bool hitTestable = true, Action? onDismissed = null, bool modal = false) => new() { Id = 1 };
        public void CloseOverlay(OverlayHandle handle) { }
        public void UpdateOverlayPosition(OverlayHandle handle, float x, float y) { }
        public Theme? ActiveTheme => theme;
    }

    private static readonly TableView<Entry> Table = new(
    [
        new TableColumn<Entry> { Id = "name", Header = "Name", Fill = 1, Text = e => e.Name,
            Compare = (a, b) => string.CompareOrdinal(a.Name, b.Name) },
        new TableColumn<Entry> { Id = "size", Header = "Size", Width = 90, Align = ColumnAlign.End,
            Text = e => $"{e.Size} B", Compare = (a, b) => a.Size.CompareTo(b.Size) },
    ], () => { });

    /// <summary>Every kind the step opted in, built the same way each time.</summary>
    private static Element Build(ButtonVariant variant = ButtonVariant.Primary)
    {
        var bar = new MenuBarElement();
        var title = new MenuTitleElement();
        title.AddChild(TextB.New("File"));
        bar.AddChild(title);
        return Flex.Column(gap: 4).Children(
            bar,
            Flex.Row(gap: 4).Children(
                Button.New("Save", variant),
                Kbd.New("Ctrl+S"),
                TextInput.New("query"),
                new SplitterHandleElement(SplitterAxis.Horizontal).Height(Dimension.Px(20))),
            Table.Header(),
            Table.Row(new Entry("a.txt", 100)).Height(Dimension.Px(24)));
    }

    private static (BoxElement Parent, Element Live, LayoutEngine Engine) Mounted(Theme theme, Theme? hostTheme)
    {
        var parent = Box.New();
        parent.OverlayHost = new Host(hostTheme);
        var live = Build();
        parent.AddChild(live);
        StyleResolver.Resolve(parent, theme);
        var engine = new LayoutEngine(new StubMeasureContext());
        engine.PerformLayout(parent, 800, 400);
        ClearFlags(parent);   // as a frame leaves them
        return (parent, live, engine);
    }

    private static void ClearFlags(Element element)
    {
        element.DirtyFlags = DirtyFlags.None;
        foreach (var child in element.Children) ClearFlags(child);
    }

    private static T? Find<T>(Element e) where T : Element
    {
        if (e is T t) return t;
        foreach (var c in e.Children) if (Find<T>(c) is { } f) return f;
        return null;
    }

    private static List<string> Marked(Element e, List<string>? into = null)
    {
        into ??= [];
        if ((e.DirtyFlags & DirtyFlags.Layout) != 0) into.Add(e.GetType().Name);
        foreach (var c in e.Children) Marked(c, into);
        return into;
    }

    [Fact]
    public void APatchFromATwinThatThemesTheSame_LeavesEveryLayoutStanding()
    {
        var (parent, live, _) = Mounted(Theme.Light, Theme.Light);

        TreeDiffer.Reconcile(parent, live, Build());

        Assert.Empty(Marked(parent));
    }

    [Fact]
    public void WithNoWindowThemeToHand_APatchMarksLayoutAsBefore()
    {
        var (parent, live, _) = Mounted(Theme.Light, hostTheme: null);

        TreeDiffer.Reconcile(parent, live, Build());

        var marked = Marked(parent);
        foreach (var kind in new[] { nameof(ButtonElement), nameof(KbdElement), nameof(TextInputElement), nameof(MenuBarElement) })
            Assert.Contains(kind, marked);
    }

    [Fact]
    public void ATwinThatThemesToAnotherPadding_IsLaidOutAgain()
    {
        var theme = Theme.Light.With(new ButtonTheme { Ghost = new ButtonStyle { Padding = new Thickness(2, 4, 2, 4) } });
        var (parent, live, engine) = Mounted(theme, theme);
        var button = Find<ButtonElement>(live)!;
        float wide = button.Layout.Width;

        TreeDiffer.Reconcile(parent, live, Build(ButtonVariant.Ghost));

        Assert.True((button.DirtyFlags & DirtyFlags.Layout) != 0, "the button kept its layout");
        StyleResolver.Resolve(parent, theme);
        engine.PerformLayout(parent, 800, 400);
        Assert.Equal(wide - 24f, button.Layout.Width, 2);
    }

    [Fact]
    public void ThemingTheTwin_TouchesNothingButTheTwin()
    {
        // The twin's theme is applied before the copy; the live element and its children must
        // come out of the patch exactly as a style pass leaves them.
        var (parent, live, _) = Mounted(Theme.Light, Theme.Light);
        var before = Snapshot(parent);

        TreeDiffer.Reconcile(parent, live, Build());
        StyleResolver.Resolve(parent, Theme.Light);

        Assert.Equal(before, Snapshot(parent));
    }

    private static string Snapshot(Element root)
    {
        var sb = new System.Text.StringBuilder();
        void Walk(Element e)
        {
            var b = e.BoxRead;
            sb.Append(e.GetType().Name).Append(' ').Append(b.Padding).Append(' ').Append(b.BorderWidth).Append(' ')
              .Append(b.Width).Append(' ').Append(b.Height).Append(' ').Append(e.FlexRead.JustifyContent).Append(' ')
              .Append(e.ResolvedInherited).Append(' ').Append((e as BoxElement)?.BackgroundColor).Append('\n');
            foreach (var c in e.Children) Walk(c);
        }
        Walk(root);
        return sb.ToString();
    }
}
