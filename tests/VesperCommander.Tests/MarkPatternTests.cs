using System.Collections.Generic;
using System.IO;
using System.Linq;
using HultUI.Core.Events;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>Ctrl+Shift+A: a prompt takes a pattern (*.txt, report*, .md) and marks the
/// files it matches — additive, files only, matching the find dialog's rules.</summary>
public class MarkPatternTests
{
    [Fact]
    public void APatternMarksTheFilesItMatchesAndClosesThePrompt()
    {
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("a.txt", "one");
        t.WriteLeft("b.txt", "two");
        t.WriteLeft("notes.md", "three");
        t.Open();

        t.Press(KeyCode.A, KeyModifiers.Ctrl | KeyModifiers.Shift);
        Assert.NotNull(t.Field<object?>("_namePrompt"));

        Commit(t, "*.txt");

        Assert.Equal(new[] { "a.txt", "b.txt" }, Marks(t).Order());
        Assert.Null(t.Field<object?>("_namePrompt"));
    }

    /// <summary>Marks add to what is already marked, like marking by hand.</summary>
    [Fact]
    public void MarkingByPatternIsAdditive()
    {
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("a.txt", "one");
        t.WriteLeft("notes.md", "two");
        t.Open();

        t.PutCursorOn('L', "notes.md");
        t.Press(KeyCode.Space);                       // mark it by hand first
        t.Press(KeyCode.A, KeyModifiers.Ctrl | KeyModifiers.Shift);
        Commit(t, "*.txt");

        Assert.Equal(new[] { "a.txt", "notes.md" }, Marks(t).Order());
    }

    /// <summary>A pattern speaks about files — a folder whose name happens to match
    /// stays unmarked.</summary>
    [Fact]
    public void FoldersAreNotMarkedEvenWhenTheirNameMatches()
    {
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("real.txt", "one");
        Directory.CreateDirectory(Path.Combine(t.Left, "folder.txt"));
        t.Open();

        t.Press(KeyCode.A, KeyModifiers.Ctrl | KeyModifiers.Shift);
        Commit(t, "*.txt");

        Assert.Equal(new[] { "real.txt" }, Marks(t).Order());
    }

    /// <summary>A leading-dot pattern is an extension filter, like the find dialog's.</summary>
    [Fact]
    public void ALeadingDotPatternFiltersByExtension()
    {
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("notes.md", "one");
        t.WriteLeft("readme.txt", "two");
        t.Open();

        t.Press(KeyCode.A, KeyModifiers.Ctrl | KeyModifiers.Shift);
        Commit(t, ".md");

        Assert.Equal(new[] { "notes.md" }, Marks(t).Order());
    }

    [Fact]
    public void NoMatchSaysSoAndKeepsThePromptOpen()
    {
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("a.txt", "one");
        t.Open();

        t.Press(KeyCode.A, KeyModifiers.Ctrl | KeyModifiers.Shift);
        Commit(t, "*.zip");

        Assert.NotNull(t.Field<object?>("_namePrompt"));
        Assert.Contains("No files", t.Field<string?>("_namePromptError") ?? "");
        Assert.Empty(Marks(t));
    }

    /// <summary>The prompt commits through the same field + Enter path the user drives —
    /// the value is set the way Pack tests set theirs, then Enter commits.</summary>
    private static void Commit(DiffViewTests.Fixture t, string pattern)
    {
        t.SetField("_namePromptValue", pattern);
        t.Press(KeyCode.Enter);
    }

    private static HashSet<string> Marks(DiffViewTests.Fixture t) =>
        (HashSet<string>)t.Invoke("Marks", 'L')!;
}
