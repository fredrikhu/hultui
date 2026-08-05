using VesperCommander;
using Xunit;
using O = VesperCommander.RenameEngine.Options;

namespace VesperCommander.Tests;

public class RenameEngineTests
{
    // ── Token expansion ──────────────────────────────────────────────────
    [Theory]
    [InlineData("[N]", "DSC_2201")]
    [InlineData("[N2-5]", "SC_2")]        // chars 2–5 (1-based)
    [InlineData("[N2-]", "SC_2201")]      // char 2 to end
    [InlineData("[E]", "nef")]
    [InlineData("[C]", "001")]            // counter 1, digits 3
    [InlineData("[C:5]", "00001")]        // inline pad override
    [InlineData("[P]", "iceland")]
    [InlineData("[[x]]", "[x]")]          // escaped brackets
    [InlineData("[Z]", "[Z]")]            // unknown token → literal
    [InlineData("holiday_[N2-]_[C]", "holiday_SC_2201_001")]
    public void Expand_Tokens(string mask, string expected) =>
        Assert.Equal(expected, RenameEngine.Expand(mask, "DSC_2201", "nef", "iceland", counter: 1, counterDigits: 3));

    [Fact]
    public void UsesCounter_DetectsC() =>
        Assert.True(RenameEngine.UsesCounter(new O(NameMask: "x_[C]")) &&
                    !RenameEngine.UsesCounter(new O(NameMask: "[N]")));

    // ── Pipeline ─────────────────────────────────────────────────────────
    private static RenameEngine.Preview One(string name, O o) =>
        RenameEngine.Build([(name, $@"C:\photos\iceland\{name}")], o);

    private static string NewName(string name, O o) => One(name, o).Rows[0].NewName;

    [Fact]
    public void Pipeline_CounterAndKeepsExtension() =>
        Assert.Equal("iceland_001.jpg", NewName("DSC_2201.jpg", new O(NameMask: "iceland_[C]")));

    [Fact]
    public void Pipeline_ExtensionMaskReplaces() =>
        Assert.Equal("iceland_001.bak", NewName("DSC_2201.jpg", new O(NameMask: "iceland_[C]", ExtMask: "bak")));

    [Fact]
    public void Pipeline_FindReplaceOnName() =>
        Assert.Equal("reyk_2201.jpg", NewName("DSC_2201.jpg", new O(Find: "DSC_", Replace: "reyk_")));

    [Fact]
    public void Pipeline_CaseThenTrimThenCollapse() =>
        Assert.Equal("My Summer Notes.txt",
            NewName("my  summer notes.txt",
                new O(Case: RenameEngine.CaseMode.Title, TrimEnds: true, CollapseSpaces: true)));

    [Fact]
    public void Pipeline_CounterStepAndDigits()
    {
        var o = new O(NameMask: "p[C]", CounterStart: 10, CounterStep: 10, CounterDigits: 4);
        var p = RenameEngine.Build([("a.txt", @"C:\d\a.txt"), ("b.txt", @"C:\d\b.txt"), ("c.txt", @"C:\d\c.txt")], o);
        Assert.Equal(new[] { "p0010.txt", "p0020.txt", "p0030.txt" }, p.Rows.Select(r => r.NewName));
    }

    // ── Conflict classification ──────────────────────────────────────────
    [Fact]
    public void Conflict_Unchanged() =>
        Assert.Equal(RenameEngine.Status.Unchanged, NewNameStatus("a.txt", new O(NameMask: "[N]")));

    [Fact]
    public void Conflict_EmptyAndIllegal()
    {
        // "noext" has no extension → an empty name mask (keeping the empty ext) yields "".
        Assert.Equal(RenameEngine.Status.Empty, NewNameStatus("noext", new O(NameMask: "")));
        Assert.Equal(RenameEngine.Status.Illegal, NewNameStatus("a.txt", new O(NameMask: "a?b")));
    }

    [Fact]
    public void Conflict_CollisionWhenResultsMatch()
    {
        var p = RenameEngine.Build([("a.md", @"C:\d\a.md"), ("b.md", @"C:\d\b.md")], new O(NameMask: "final"));
        Assert.All(p.Rows, r => Assert.Equal(RenameEngine.Status.Collision, r.Status));
        Assert.Equal(2, p.Conflicts);
        Assert.Equal(0, p.WillRename);
    }

    [Fact]
    public void Conflict_ExistsOnDisk()
    {
        var dir = FreshDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "a.txt"), "a");
            File.WriteAllText(Path.Combine(dir, "occupied.txt"), "x");   // not in the batch
            var p = RenameEngine.Build([("a.txt", Path.Combine(dir, "a.txt"))], new O(NameMask: "occupied"));
            Assert.Equal(RenameEngine.Status.Exists, p.Rows[0].Status);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void MalformedRegex_ReportsErrorNoRows()
    {
        var p = RenameEngine.Build([("a.txt", @"C:\d\a.txt")], new O(Find: "([0-9", Regex: true));
        Assert.NotNull(p.RegexError);
        Assert.Empty(p.Rows);
    }

    // ── Two-phase apply (swap) ───────────────────────────────────────────
    [Fact]
    public void Apply_SwapsViaTempNames()
    {
        var dir = FreshDir();
        try
        {
            var a = Path.Combine(dir, "a.txt"); var b = Path.Combine(dir, "b.txt");
            File.WriteAllText(a, "A"); File.WriteAllText(b, "B");
            var rows = new[]
            {
                new RenameEngine.Row("a.txt", a, "b.txt", RenameEngine.Status.Ok),
                new RenameEngine.Row("b.txt", b, "a.txt", RenameEngine.Status.Ok),
            };
            var res = RenameEngine.Apply(rows);
            Assert.All(res, r => Assert.Null(r.Error));
            Assert.Equal("B", File.ReadAllText(a));   // a now holds b's old content
            Assert.Equal("A", File.ReadAllText(b));
        }
        finally { Directory.Delete(dir, true); }
    }

    private static RenameEngine.Status NewNameStatus(string name, O o) => One(name, o).Rows[0].Status;

    private static string FreshDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "vesper-rn-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }
}
