using System.Linq;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Reactivity;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// Where the preview panel sits, and what it is showing.
///
/// <para>It stands in for the pane you are <i>not</i> browsing and reads the cursor of the
/// one you are, so it swaps sides when you press Tab. That is the whole feature, and it is
/// invisible to the excerpt-level checks in <see cref="PreviewTests"/> — those never look at
/// the layout. The old off/side/full cycle had no coverage at all, which is how the side
/// column survived as long as it did.</para>
/// </summary>
public class PreviewPlacementTests
{
    [Theory]
    [InlineData('L')]
    [InlineData('R')]
    public void ThePreviewTakesTheSideYouAreNotBrowsing(char active)
    {
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("a.txt", "left\n");
        t.WriteRight("b.txt", "right\n");
        t.Open();

        SetActive(t, active);
        ShowPreview(t);

        var (pane, preview) = Columns(t);
        if (active == 'L')
            Assert.True(pane.X < preview.X,
                $"active pane is on the left, so the preview belongs on the right — pane at x={pane.X:0}, preview at x={preview.X:0}");
        else
            Assert.True(preview.X < pane.X,
                $"active pane is on the right, so the preview belongs on the left — pane at x={pane.X:0}, preview at x={preview.X:0}");
    }

    [Fact]
    public void TabMovesThePreviewToTheOtherSide()
    {
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("a.txt", "left\n");
        t.WriteRight("b.txt", "right\n");
        t.Open();

        SetActive(t, 'L');
        ShowPreview(t);
        float before = Columns(t).Preview.X;

        t.Press(KeyCode.Tab);
        t.Session.PumpFrames(2);

        var after = Columns(t);
        Assert.Equal('R', Active(t));
        Assert.True(after.Preview.X < before,
            $"the preview stayed at x={after.Preview.X:0} after Tab — it should have crossed to the left");
        Assert.True(after.Preview.X < after.Pane.X, "the preview is no longer opposite the active pane");
    }

    [Fact]
    public void ThePreviewFollowsTheActivePanesCursor()
    {
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("a.txt", "contents of the left file\n");
        t.WriteRight("b.txt", "contents of the right file\n");
        t.Open();

        SetActive(t, 'R');
        ShowPreview(t);
        t.PutCursorOn('R', "b.txt");
        t.Session.PumpUntil(() => Kicker(t).Contains("B.TXT"), System.TimeSpan.FromSeconds(10));

        // The name in the kicker is the proof: the panel is reading the pane the user is
        // standing in, not the one it happens to be drawn over.
        Assert.Contains("B.TXT", Kicker(t));
    }

    [Fact]
    public void TheToggleIsTwoStateNotThree()
    {
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("a.txt", "x\n");
        t.Open();
        var settings = t.Field<Settings>("_settings");
        settings.PreviewPane = false;

        t.Invoke("TogglePreview");
        Assert.True(settings.PreviewPane);
        t.Invoke("TogglePreview");
        Assert.False(settings.PreviewPane);   // a third press would have gone somewhere else
    }

    [Fact]
    public void TheDefaultIsOff()
    {
        // Read from a fresh Settings rather than the app's, which has loaded whatever the
        // last run left on disk. The preview takes over half the window, so it has to be
        // something you ask for.
        Assert.False(new Settings().PreviewPane);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static char Active(DiffViewTests.Fixture t) => t.Field<char>("_active");

    private static void SetActive(DiffViewTests.Fixture t, char side)
    {
        t.SetField("_active", side);
        t.MarkDirty();
        t.Session.PumpFrames(2);
    }

    /// <summary>Sets the panel rather than toggling it. <c>TogglePreview</c> writes
    /// settings.json, and every fixture in the run loads that same file, so a test that
    /// toggled would decide what the next one starts from.</summary>
    private static void ShowPreview(DiffViewTests.Fixture t)
    {
        t.Field<Settings>("_settings").PreviewPane = true;
        t.MarkDirty();
        t.Session.PumpFrames(2);
    }

    /// <summary>The kicker reads "PREVIEW · &lt;filename&gt;" — the panel's first label.</summary>
    private static string Kicker(DiffViewTests.Fixture t)
    {
        var preview = PreviewElement(t);
        return LayoutProbeDescendants<TextElement>(preview).FirstOrDefault()?.Content ?? "";
    }

    /// <summary>The active pane's box and the preview's, both in window coordinates.</summary>
    private static (HultUI.Core.Primitives.Rect Pane, HultUI.Core.Primitives.Rect Preview) Columns(
        DiffViewTests.Fixture t)
    {
        var pane = PaneElement(t);
        var preview = PreviewElement(t);
        return (pane.Layout.AbsoluteBorderBox, preview.Layout.AbsoluteBorderBox);
    }

    private static Element PaneElement(DiffViewTests.Fixture t)
    {
        var reference = (Ref<BoxElement>)t.Invoke("PaneRef", Active(t))!;
        return reference.Current
            ?? throw new Xunit.Sdk.XunitException("the active pane is not mounted");
    }

    /// <summary>The preview column, found by the key Build gives it — it is a sibling of the
    /// pane rather than anything a Ref points at.</summary>
    private static Element PreviewElement(DiffViewTests.Fixture t)
    {
        var body = PaneElement(t).Parent
            ?? throw new Xunit.Sdk.XunitException("the pane has no parent");
        return body.Children.FirstOrDefault(c => c.Key == "preview")
            ?? throw new Xunit.Sdk.XunitException(
                "no preview column is mounted beside the pane — is the panel showing?");
    }

    private static System.Collections.Generic.List<T> LayoutProbeDescendants<T>(Element root) where T : Element =>
        HultUI.Headless.LayoutProbe.Descendants<T>(root);
}
