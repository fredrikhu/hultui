using System.IO.Compression;
using HultUI.Core.Events;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// Ctrl+PageDown — "go into this". It went into folders only, which left the one keyboard way
/// down the tree stopping dead at an archive that Enter walks straight into.
///
/// <para>What it deliberately does <em>not</em> borrow from Enter is opening a plain file in
/// the shell: the chord is for walking a tree, and a stray press on a .exe should do nothing
/// rather than run it.</para>
/// </summary>
public class DescendTests
{
    private static void Descend(DiffViewTests.Fixture t) =>
        t.Press(KeyCode.PageDown, KeyModifiers.Ctrl);

    [Fact]
    public void CtrlPageDownEntersAnArchive()
    {
        using var t = new DiffViewTests.Fixture();
        var zip = Path.Combine(t.Left, "assets.zip");
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
            using (var w = new StreamWriter(z.CreateEntry("readme.txt").Open())) w.Write("hello");
        t.Open();
        t.PutCursorOn('L', "assets.zip");

        Descend(t);
        t.Session.PumpUntil(() => t.Rows('L').Any(r => r.Name == "readme.txt"), TimeSpan.FromSeconds(10));

        Assert.Equal(zip, t.Dir('L'));
    }

    /// <summary>And back out, which Ctrl+PageUp already did — worth holding, since an archive
    /// root's ".." is the containing folder rather than a member path.</summary>
    [Fact]
    public void CtrlPageUpLeavesAnArchiveAgain()
    {
        using var t = new DiffViewTests.Fixture();
        var zip = Path.Combine(t.Left, "assets.zip");
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
            using (var w = new StreamWriter(z.CreateEntry("readme.txt").Open())) w.Write("hello");
        t.Open();
        t.PutCursorOn('L', "assets.zip");
        Descend(t);
        t.Session.PumpUntil(() => t.Rows('L').Any(r => r.Name == "readme.txt"), TimeSpan.FromSeconds(10));

        t.Press(KeyCode.PageUp, KeyModifiers.Ctrl);
        t.Session.PumpUntil(() => t.Dir('L') == t.Left, TimeSpan.FromSeconds(10));

        Assert.Equal(t.Left, t.Dir('L'));
    }

    /// <summary>An archive nested inside the one being browsed has no file on disk to check
    /// for, so descending into it goes through the VFS the same way Enter does.</summary>
    [Fact]
    public void CtrlPageDownEntersAnArchiveInsideAnArchive()
    {
        using var t = new DiffViewTests.Fixture();
        var inner = Path.Combine(t.Left, "inner.zip");
        using (var z = ZipFile.Open(inner, ZipArchiveMode.Create))
            using (var w = new StreamWriter(z.CreateEntry("hello.txt").Open())) w.Write("hi");
        var outer = Path.Combine(t.Left, "outer.zip");
        using (var z = ZipFile.Open(outer, ZipArchiveMode.Create))
            z.CreateEntryFromFile(inner, "inner.zip");
        File.Delete(inner);
        t.Open();

        t.PutCursorOn('L', "outer.zip");
        Descend(t);
        t.Session.PumpUntil(() => t.Rows('L').Any(r => r.Name == "inner.zip"), TimeSpan.FromSeconds(10));

        t.PutCursorOn('L', "inner.zip");
        Descend(t);
        t.Session.PumpUntil(() => t.Rows('L').Any(r => r.Name == "hello.txt"), TimeSpan.FromSeconds(10));

        Assert.Contains(t.Rows('L'), r => r.Name == "hello.txt");
    }

    [Fact]
    public void CtrlPageDownStillEntersAPlainFolder()
    {
        using var t = new DiffViewTests.Fixture();
        Directory.CreateDirectory(Path.Combine(t.Left, "sub"));
        t.Open();
        t.PutCursorOn('L', "sub");

        Descend(t);
        t.Session.PumpUntil(() => t.Dir('L') == Path.Combine(t.Left, "sub"), TimeSpan.FromSeconds(10));

        Assert.Equal(Path.Combine(t.Left, "sub"), t.Dir('L'));
    }

    /// <summary>A plain file is not a thing you go into, and must not become a thing the shell
    /// opens either — that is Enter's job, and the difference is the whole safety of the chord.</summary>
    [Fact]
    public void CtrlPageDownOnAPlainFileDoesNothing()
    {
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("a.txt", "A");
        t.Open();
        t.PutCursorOn('L', "a.txt");

        Descend(t);
        t.Session.PumpFrame();

        Assert.Equal(t.Left, t.Dir('L'));
    }
}
