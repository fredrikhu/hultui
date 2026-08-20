using System.IO.Compression;
using HultUI.Core.Events;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// F5 and F6 ask where before they write. The other pane's folder is filled in, so Enter still
/// means what the key has always meant — but it is a text field, so a copy can be aimed at a
/// folder neither pane is standing in. Copy used to start the moment the key came up with no
/// dialog at all; move showed a fixed yes/no with nothing to change.
/// </summary>
public class TransferPromptTests
{
    private static bool PromptOpen(DiffViewTests.Fixture t) => t.Field<object?>("_namePrompt") != null;
    private static string PromptValue(DiffViewTests.Fixture t) => t.Field<string>("_namePromptValue");
    private static string? PromptError(DiffViewTests.Fixture t) => t.Field<string?>("_namePromptError");

    /// <summary>Retype the destination. The real input is pre-filled, so a user selects all and
    /// types over it; TypeText would append to what is already there.</summary>
    private static void Retarget(DiffViewTests.Fixture t, string path) => t.SetField("_namePromptValue", path);

    private static void RunToCompletion(DiffViewTests.Fixture t) =>
        t.Session.PumpUntil(() => t.Field<object?>("_op") == null, TimeSpan.FromSeconds(30));

    [Fact]
    public void F5OpensTheDialogOnTheOtherPanesFolderAndCopiesNothingUntilConfirmed()
    {
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("a.txt", "A");
        t.Open();
        t.PutCursorOn('L', "a.txt");

        t.Press(KeyCode.F5);

        Assert.True(PromptOpen(t));
        Assert.Equal(t.Right, PromptValue(t));
        Assert.False(File.Exists(Path.Combine(t.Right, "a.txt")));   // nothing has moved yet
    }

    [Fact]
    public void EnterTakesTheFilledInDestination()
    {
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("a.txt", "A");
        t.Open();
        t.PutCursorOn('L', "a.txt");

        t.Press(KeyCode.F5);
        t.Press(KeyCode.Enter);
        RunToCompletion(t);

        Assert.Equal("A", File.ReadAllText(Path.Combine(t.Right, "a.txt")));
    }

    [Fact]
    public void EscapeCancelsAndNothingIsWritten()
    {
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("a.txt", "A");
        t.Open();
        t.PutCursorOn('L', "a.txt");

        t.Press(KeyCode.F5);
        t.Press(KeyCode.Escape);

        Assert.False(PromptOpen(t));
        Assert.False(File.Exists(Path.Combine(t.Right, "a.txt")));
    }

    /// <summary>The reason the box is editable: somewhere neither pane is looking.</summary>
    [Fact]
    public void AnEditedDestinationIsWhereTheFilesGo()
    {
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("a.txt", "A");
        var elsewhere = Path.Combine(Path.GetDirectoryName(t.Left)!, "elsewhere");
        Directory.CreateDirectory(elsewhere);
        t.Open();
        t.PutCursorOn('L', "a.txt");

        t.Press(KeyCode.F5);
        Retarget(t, elsewhere);
        t.Press(KeyCode.Enter);
        RunToCompletion(t);

        Assert.Equal("A", File.ReadAllText(Path.Combine(elsewhere, "a.txt")));
        Assert.False(File.Exists(Path.Combine(t.Right, "a.txt")));
    }

    /// <summary>Read the way Ctrl+G reads a path: relative to the pane the files are in.</summary>
    [Fact]
    public void ARelativeDestinationResolvesAgainstTheSourcePane()
    {
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("a.txt", "A");
        Directory.CreateDirectory(Path.Combine(t.Left, "sub"));
        t.Open();
        t.PutCursorOn('L', "a.txt");

        t.Press(KeyCode.F5);
        Retarget(t, "sub");
        t.Press(KeyCode.Enter);
        RunToCompletion(t);

        Assert.Equal("A", File.ReadAllText(Path.Combine(t.Left, "sub", "a.txt")));
    }

    /// <summary>A path that doesn't exist takes two confirms. One is how a typo scatters empty
    /// folders across a disk; the second press costs nothing when the folder really is new.</summary>
    [Fact]
    public void AMissingDestinationIsCreatedOnlyOnTheSecondConfirm()
    {
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("a.txt", "A");
        var fresh = Path.Combine(t.Right, "brand-new");
        t.Open();
        t.PutCursorOn('L', "a.txt");

        t.Press(KeyCode.F5);
        Retarget(t, fresh);
        t.Press(KeyCode.Enter);

        Assert.True(PromptOpen(t));
        Assert.NotNull(PromptError(t));
        Assert.False(Directory.Exists(fresh));

        t.Press(KeyCode.Enter);
        RunToCompletion(t);

        Assert.Equal("A", File.ReadAllText(Path.Combine(fresh, "a.txt")));
    }

    [Fact]
    public void F6MovesThroughTheSameDialog()
    {
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("a.txt", "A");
        t.Open();
        t.PutCursorOn('L', "a.txt");

        t.Press(KeyCode.F6);
        Assert.Equal(t.Right, PromptValue(t));

        t.Press(KeyCode.Enter);
        RunToCompletion(t);

        Assert.Equal("A", File.ReadAllText(Path.Combine(t.Right, "a.txt")));
        Assert.False(File.Exists(Path.Combine(t.Left, "a.txt")));
    }

    /// <summary>Copying a folder onto itself is refused, and says so in the dialog rather than
    /// closing it — the destination is still there to fix.</summary>
    [Fact]
    public void TheSourceFolderIsRefusedWithTheDialogLeftOpen()
    {
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("a.txt", "A");
        t.Open();
        t.PutCursorOn('L', "a.txt");

        t.Press(KeyCode.F5);
        Retarget(t, t.Left);
        t.Press(KeyCode.Enter);

        Assert.True(PromptOpen(t));
        Assert.NotNull(PromptError(t));
    }

    /// <summary>Copying *into* an archive was the one destination F5 never asked about: it
    /// skipped the confirm entirely and started packing on the keypress. It is just a path to
    /// the dialog now.</summary>
    [Fact]
    public void CopyingIntoAnArchiveAsksFirstAndThenPacks()
    {
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("a.txt", "A");
        var zip = Path.Combine(t.Right, "box.zip");
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
            using (var w = new StreamWriter(z.CreateEntry("seed.txt").Open())) w.Write("s");
        t.Open();
        t.Invoke("NavigateTo", 'R', zip, null, false);
        t.Session.PumpUntil(() => t.Rows('R').Any(r => r.Name == "seed.txt"), TimeSpan.FromSeconds(10));
        t.PutCursorOn('L', "a.txt");

        t.Press(KeyCode.F5);

        Assert.True(PromptOpen(t));
        Assert.Equal(zip, PromptValue(t));
        using (var before = ZipFile.OpenRead(zip))
            Assert.Null(before.GetEntry("a.txt"));       // nothing packed on the keypress

        t.Press(KeyCode.Enter);
        RunToCompletion(t);

        using var after = ZipFile.OpenRead(zip);
        Assert.NotNull(after.GetEntry("a.txt"));
        Assert.NotNull(after.GetEntry("seed.txt"));      // and the archive keeps what it had
    }

    /// <summary>One archive can't be poured into another: the add writers read their sources
    /// off disk, and an archive member has no disk file. Refused before the dialog opens, since
    /// the destination is already known to be one — there is nothing to type your way out of.</summary>
    [Fact]
    public void ExtractingIntoAnotherArchiveIsRefusedBeforeTheDialogOpens()
    {
        using var t = new DiffViewTests.Fixture();
        var from = Path.Combine(t.Left, "from.zip");
        using (var z = ZipFile.Open(from, ZipArchiveMode.Create))
            using (var w = new StreamWriter(z.CreateEntry("readme.txt").Open())) w.Write("hello");
        var into = Path.Combine(t.Right, "into.zip");
        using (var z = ZipFile.Open(into, ZipArchiveMode.Create))
            using (var w = new StreamWriter(z.CreateEntry("seed.txt").Open())) w.Write("s");
        t.Open();
        t.Invoke("NavigateTo", 'R', into, null, false);
        t.Session.PumpUntil(() => t.Rows('R').Any(r => r.Name == "seed.txt"), TimeSpan.FromSeconds(10));

        t.PutCursorOn('L', "from.zip");
        t.Press(KeyCode.Enter);
        t.Session.PumpUntil(() => t.Rows('L').Any(r => r.Name == "readme.txt"), TimeSpan.FromSeconds(10));
        t.PutCursorOn('L', "readme.txt");

        t.Press(KeyCode.F5);

        Assert.False(PromptOpen(t));
        Assert.NotNull(t.Field<string?>("_notice"));
        using var untouched = ZipFile.OpenRead(into);
        Assert.Null(untouched.GetEntry("readme.txt"));
    }

    /// <summary>And typing one in reaches the same refusal, in the dialog this time — the box
    /// must not accept a destination the key would have turned down.</summary>
    [Fact]
    public void TypingAnArchiveIntoTheExtractDialogIsRefusedToo()
    {
        using var t = new DiffViewTests.Fixture();
        var from = Path.Combine(t.Left, "from.zip");
        using (var z = ZipFile.Open(from, ZipArchiveMode.Create))
            using (var w = new StreamWriter(z.CreateEntry("readme.txt").Open())) w.Write("hello");
        var into = Path.Combine(t.Right, "into.zip");
        using (var z = ZipFile.Open(into, ZipArchiveMode.Create))
            using (var w = new StreamWriter(z.CreateEntry("seed.txt").Open())) w.Write("s");
        t.Open();

        t.PutCursorOn('L', "from.zip");
        t.Press(KeyCode.Enter);
        t.Session.PumpUntil(() => t.Rows('L').Any(r => r.Name == "readme.txt"), TimeSpan.FromSeconds(10));
        t.PutCursorOn('L', "readme.txt");

        t.Press(KeyCode.F5);
        Retarget(t, into);
        t.Press(KeyCode.Enter);

        Assert.True(PromptOpen(t));
        Assert.NotNull(PromptError(t));
    }

    /// <summary>F5 inside an archive is Extract, and goes through the same box — the verb
    /// differs, the ability to aim it somewhere else does not.</summary>
    [Fact]
    public void ExtractingFromAnArchiveUsesTheSameDialog()
    {
        using var t = new DiffViewTests.Fixture();
        var zip = Path.Combine(t.Left, "assets.zip");
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
            using (var w = new StreamWriter(z.CreateEntry("readme.txt").Open())) w.Write("hello");
        t.Open();

        t.PutCursorOn('L', "assets.zip");
        t.Press(KeyCode.Enter);
        t.Session.PumpUntil(() => t.Rows('L').Any(r => r.Name == "readme.txt"));
        t.PutCursorOn('L', "readme.txt");

        t.Press(KeyCode.F5);
        Assert.True(PromptOpen(t));
        Assert.Equal(t.Right, PromptValue(t));

        t.Press(KeyCode.Enter);
        RunToCompletion(t);

        Assert.Equal("hello", File.ReadAllText(Path.Combine(t.Right, "readme.txt")));
    }
}
