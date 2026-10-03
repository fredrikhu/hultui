using HultUI.Core.Events;
using System.Collections;
using System.IO.Compression;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// F4 on an archive member: unpack it, edit the copy, and put it back when Vesper next comes to
/// the foreground. Handing the member's virtual path straight to an editor opened an empty
/// buffer — there is no such file on disk.
///
/// <para>The launch itself is not driven here: it would start a real editor on whoever is
/// running the suite, and <see cref="EditorLaunchTests"/> already covers how the program is
/// invoked. What is driven is everything around it — the unpack, the change detection on
/// activation, and the write-back — by registering the edit the way <c>EditArchiveMember</c>
/// does and then editing the temp file as an editor would.</para>
/// </summary>
public class ArchiveEditTests
{
    private static string MakeZip(string dir, string name, params (string Entry, string Body)[] members)
    {
        var path = Path.Combine(dir, name);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (entry, body) in members)
        {
            using var w = new StreamWriter(zip.CreateEntry(entry).Open());
            w.Write(body);
        }
        return path;
    }

    private static string MemberText(string archive, string entry)
    {
        using var zip = ZipFile.OpenRead(archive);
        using var r = new StreamReader(zip.GetEntry(entry)!.Open());
        return r.ReadToEnd();
    }

    /// <summary>Unpack a member and register it, as F4 does either side of launching the
    /// editor. Returns the temp file the "editor" would be pointed at.</summary>
    private static string TakeOutForEditing(DiffViewTests.Fixture t, string archive, string internalPath, string name)
    {
        var edit = t.Invoke("ExtractForEdit", archive, internalPath, name, null);
        Assert.NotNull(edit);
        t.Field<IList>("_edits").Add(edit);
        return (string)edit!.GetType().GetField("TempPath")!.GetValue(edit)!;
    }

    private static bool ConfirmOpen(DiffViewTests.Fixture t) => t.Field<object?>("_confirm") != null;

    /// <summary>Come back to Vesper, exactly as the OS foregrounding the window does: the
    /// platform flags it, and the loop raises Window.Activated on the next frame.</summary>
    private static void ReturnToVesper(DiffViewTests.Fixture t)
    {
        t.Session.Window.Activate();
        t.Session.PumpFrame();
    }

    [Fact]
    public void TheMemberIsUnpackedWithItsRealContentAndItsRealName()
    {
        using var t = new DiffViewTests.Fixture();
        var zip = MakeZip(t.Left, "assets.zip", ("readme.txt", "hello from inside"));
        t.Open();

        var temp = TakeOutForEditing(t, zip, "readme.txt", "readme.txt");

        Assert.Equal("hello from inside", File.ReadAllText(temp));
        // The name carries: an editor picks its syntax mode off the extension, and a temp file
        // called "tmp4F2A.tmp" would open as plain text.
        Assert.Equal("readme.txt", Path.GetFileName(temp));
    }

    [Fact]
    public void ComingBackWithTheFileEditedOffersToUpdateTheArchive()
    {
        using var t = new DiffViewTests.Fixture();
        var zip = MakeZip(t.Left, "assets.zip", ("readme.txt", "before"));
        t.Open();
        var temp = TakeOutForEditing(t, zip, "readme.txt", "readme.txt");

        File.WriteAllText(temp, "after");
        ReturnToVesper(t);

        Assert.True(ConfirmOpen(t));
    }

    [Fact]
    public void ConfirmingWritesTheEditBackIntoTheArchive()
    {
        using var t = new DiffViewTests.Fixture();
        var zip = MakeZip(t.Left, "assets.zip", ("readme.txt", "before"), ("other.txt", "untouched"));
        t.Open();
        var temp = TakeOutForEditing(t, zip, "readme.txt", "readme.txt");

        File.WriteAllText(temp, "after");
        ReturnToVesper(t);
        t.Press(KeyCode.Enter);
        t.Session.PumpUntil(() => !t.Field<bool>("_editWriting"), TimeSpan.FromSeconds(30));

        Assert.Equal("after", MemberText(zip, "readme.txt"));
        Assert.Equal("untouched", MemberText(zip, "other.txt"));   // the rest of the archive survives
    }

    /// <summary>A member in a subfolder goes back to the subfolder, not to the root.</summary>
    [Fact]
    public void AMemberInAFolderGoesBackWhereItCameFrom()
    {
        using var t = new DiffViewTests.Fixture();
        var zip = MakeZip(t.Left, "assets.zip", ("docs/readme.txt", "before"));
        t.Open();
        var temp = TakeOutForEditing(t, zip, Path.Combine("docs", "readme.txt"), "readme.txt");

        File.WriteAllText(temp, "after");
        ReturnToVesper(t);
        t.Press(KeyCode.Enter);
        t.Session.PumpUntil(() => !t.Field<bool>("_editWriting"), TimeSpan.FromSeconds(30));

        Assert.Equal("after", MemberText(zip, "docs/readme.txt"));
        using var check = ZipFile.OpenRead(zip);
        Assert.Null(check.GetEntry("readme.txt"));                 // and not at the root as well
    }

    [Fact]
    public void AnUntouchedFileIsNotAskedAbout()
    {
        using var t = new DiffViewTests.Fixture();
        var zip = MakeZip(t.Left, "assets.zip", ("readme.txt", "before"));
        t.Open();
        TakeOutForEditing(t, zip, "readme.txt", "readme.txt");

        ReturnToVesper(t);

        Assert.False(ConfirmOpen(t));
    }

    /// <summary>Declining is an answer: the archive is left alone, and the same edit is not
    /// raised again every time the window comes forward.</summary>
    [Fact]
    public void DecliningLeavesTheArchiveAloneAndDoesNotAskAgain()
    {
        using var t = new DiffViewTests.Fixture();
        var zip = MakeZip(t.Left, "assets.zip", ("readme.txt", "before"));
        t.Open();
        var temp = TakeOutForEditing(t, zip, "readme.txt", "readme.txt");

        File.WriteAllText(temp, "after");
        ReturnToVesper(t);
        t.Press(KeyCode.Escape);

        Assert.Equal("before", MemberText(zip, "readme.txt"));

        ReturnToVesper(t);
        Assert.False(ConfirmOpen(t));

        // Editing it again is a new answer to give, so it asks once more.
        File.WriteAllText(temp, "after again");
        ReturnToVesper(t);
        Assert.True(ConfirmOpen(t));
    }

    /// <summary>Two files out at once are asked about one after another, not stacked.</summary>
    [Fact]
    public void SeveralChangedFilesAreOfferedInTurn()
    {
        using var t = new DiffViewTests.Fixture();
        var zip = MakeZip(t.Left, "assets.zip", ("a.txt", "a"), ("b.txt", "b"));
        t.Open();
        var tempA = TakeOutForEditing(t, zip, "a.txt", "a.txt");
        var tempB = TakeOutForEditing(t, zip, "b.txt", "b.txt");

        File.WriteAllText(tempA, "a2");
        File.WriteAllText(tempB, "b2");

        ReturnToVesper(t);
        t.Press(KeyCode.Enter);
        t.Session.PumpUntil(() => !t.Field<bool>("_editWriting"), TimeSpan.FromSeconds(30));
        t.Session.PumpUntil(() => ConfirmOpen(t), TimeSpan.FromSeconds(30));   // the second one follows
        t.Press(KeyCode.Enter);
        t.Session.PumpUntil(() => !t.Field<bool>("_editWriting"), TimeSpan.FromSeconds(30));

        Assert.Equal("a2", MemberText(zip, "a.txt"));
        Assert.Equal("b2", MemberText(zip, "b.txt"));
    }

    /// <summary>Pressing F4 on the same member twice reopens the copy already out — a second
    /// extraction would be a fresh file, missing whatever was typed into the first. Two members
    /// that share a name in different folders are still two edits.</summary>
    [Fact]
    public void TheSameMemberKeepsOneEditCopy()
    {
        using var t = new DiffViewTests.Fixture();
        var zip = MakeZip(t.Left, "assets.zip", ("readme.txt", "root"), ("docs/readme.txt", "nested"));
        t.Open();
        var temp = TakeOutForEditing(t, zip, "readme.txt", "readme.txt");
        File.WriteAllText(temp, "work in progress");

        var found = t.Invoke("OpenEditFor", zip, "readme.txt");
        Assert.NotNull(found);
        Assert.Equal(temp, found!.GetType().GetField("TempPath")!.GetValue(found));
        Assert.Equal("work in progress", File.ReadAllText(temp));

        Assert.Null(t.Invoke("OpenEditFor", zip, @"docs\readme.txt"));
    }
}
