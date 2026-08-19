using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// The preview column, driven headlessly through the real app. Its first coverage — which
/// is why the caps below are worth pinning down: they are what keeps the preview cheap no
/// matter how large the file under the cursor is, and nothing else enforces them.
/// <para>
/// Reuses the commander harness from <see cref="DiffViewTests"/>; the parts used here
/// (open two panes, write a file, move the cursor) are not diff-specific.
/// </para>
/// </summary>
public class PreviewTests
{
    private static string Code(int lines) =>
        string.Join("\n", System.Linq.Enumerable.Range(0, lines)
            .Select(i => $"    var result{i} = Compute(arg{i}, \"payload {i}\");")) + "\n";

    private static string Excerpt(DiffViewTests.Fixture t)
    {
        var loader = t.Field<object>("_previewLoader");
        return loader.GetType().GetProperty("Value")!.GetValue(loader) as string ?? "";
    }

    private static string Show(DiffViewTests.Fixture t, string name, string content)
    {
        t.WriteLeft(name, content);
        t.WriteLeft("aaa-placeholder.txt", "x\n");   // somewhere else to start
        t.Open();
        // The panel is off until asked for, and nothing reads a file while it is hidden.
        // Set it rather than toggling: TogglePreview writes settings.json, which every
        // fixture in the run loads.
        t.Field<Settings>("_settings").PreviewPane = true;
        t.PutCursorOn('L', "aaa-placeholder.txt");
        t.Session.PumpFrames(2);

        var before = Excerpt(t);
        t.PutCursorOn('L', name);
        // The read runs off-thread; give it until the excerpt actually changes.
        try { t.Session.PumpUntil(() => Excerpt(t) != before, System.TimeSpan.FromSeconds(10)); }
        catch (System.TimeoutException) { }   // an unpreviewable file never changes it
        return Excerpt(t);
    }

    // Without this the preview would grow without bound with the file, which is exactly the
    // problem the diff view had to be virtualized to solve. The cap is why it does not.
    [Fact]
    public void AFileLongerThanTheCapIsPreviewedUpToItOnly()
    {
        using var t = new DiffViewTests.Fixture();

        var shown = Show(t, "big.cs", Code(3000));

        Assert.Equal(500, shown.Split('\n').Length);
        Assert.StartsWith("    var result0 =", shown);
    }

    [Fact]
    public void AShortFileIsPreviewedWhole()
    {
        using var t = new DiffViewTests.Fixture();

        var shown = Show(t, "small.cs", Code(400));

        Assert.Equal(400, shown.Split('\n').Length);
    }

    // Over the byte cap the preview shows nothing at all rather than reading the first 500
    // lines — a deliberate difference from the line cap, and easy to mistake for a bug.
    [Fact]
    public void AFileOverTheSizeCapIsNotPreviewedAtAll()
    {
        using var t = new DiffViewTests.Fixture();
        // ~2MB, comfortably past the 1MB ceiling.
        var huge = string.Join("\n", System.Linq.Enumerable.Range(0, 30000)
            .Select(i => $"    var result{i} = Compute(arg{i}, \"a fairly long payload for line {i}\");"));

        var shown = Show(t, "huge.cs", huge);

        Assert.Equal("", shown);
    }

    [Fact]
    public void AVeryLongLineIsTruncatedRatherThanShownWhole()
    {
        using var t = new DiffViewTests.Fixture();

        var shown = Show(t, "wide.txt", new string('x', 5000) + "\nsecond line\n");

        Assert.Equal(200, shown.Split('\n')[0].Length);
    }

    [Fact]
    public void ABinaryFileIsNotPreviewed()
    {
        using var t = new DiffViewTests.Fixture();

        var shown = Show(t, "blob.bin", "text then a NUL \0 and more text\n");

        Assert.Equal("", shown);
    }
}
