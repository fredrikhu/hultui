using VesperCommander;
using Xunit;

namespace VesperCommander.Tests;

public class FindEngineTests
{
    private static FindEngine.Options Opt(string pattern, bool matchCase = false, bool regex = false) =>
        new(pattern, null, Recursive: false, matchCase, regex, Hidden: false);

    [Theory]
    [InlineData("list", "listing.rs", true)]
    [InlineData("list", "main.rs", false)]
    [InlineData("LIST", "listing.rs", true)]     // case-insensitive by default
    public void Substring_Matches(string pattern, string name, bool expected) =>
        Assert.Equal(expected, FindEngine.NameMatches(name, Opt(pattern)));

    [Fact]
    public void Substring_CaseSensitive_WhenRequested()
    {
        Assert.False(FindEngine.NameMatches("listing.rs", Opt("LIST", matchCase: true)));
        Assert.True(FindEngine.NameMatches("listing.rs", Opt("list", matchCase: true)));
    }

    [Theory]
    [InlineData("*.rs", "main.rs", true)]
    [InlineData("*.rs", "main.toml", false)]
    [InlineData("main.*", "main.rs", true)]
    [InlineData("m?in.rs", "main.rs", true)]
    [InlineData("m?in.rs", "mtin.rs", true)]
    [InlineData("m?in.rs", "moain.rs", false)]   // ? is exactly one char
    public void Glob_Matches(string pattern, string name, bool expected) =>
        Assert.Equal(expected, FindEngine.NameMatches(name, Opt(pattern)));

    [Theory]
    [InlineData(@"^main\.\w+$", "main.rs", true)]
    [InlineData(@"^main\.\w+$", "xmain.rs", false)]
    public void Regex_Matches(string pattern, string name, bool expected) =>
        Assert.Equal(expected, FindEngine.NameMatches(name, Opt(pattern, regex: true)));

    [Fact]
    public void InvalidRegex_MatchesNothing() =>
        Assert.False(FindEngine.NameMatches("main.rs", Opt("(unclosed", regex: true)));

    [Fact]
    public void EmptyPattern_MatchesEverything() =>
        Assert.True(FindEngine.NameMatches("anything.xyz", Opt("")));

    [Theory]
    [InlineData(".zip", "archive.zip", true)]
    [InlineData(".zip", "archive.ZIP", true)]      // case-insensitive by default
    [InlineData(".zip", "archive.zip.bak", false)] // must be the actual extension
    [InlineData(".zip", "gzip.txt", false)]        // not a substring match
    [InlineData(".tar.gz", "backup.tar.gz", true)] // compound extension
    [InlineData(".rs", "main.rs", true)]
    [InlineData(".rs", "main.rst", false)]
    public void LeadingDot_MatchesByExtension(string pattern, string name, bool expected) =>
        Assert.Equal(expected, FindEngine.NameMatches(name, Opt(pattern)));

    [Fact]
    public void ContentHits_CountsOccurrences()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmp, "panic here\nno match\npanic again\n");
            var o = new FindEngine.Options("", "panic", false, false, false, false);
            Assert.Equal(2, FindEngine.ContentHits(tmp, o));
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void ContentHits_ZeroForBinary()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tmp, new byte[] { (byte)'a', 0, (byte)'b', (byte)'a' });   // NUL ⇒ binary
            var o = new FindEngine.Options("", "a", false, false, false, false);
            Assert.Equal(0, FindEngine.ContentHits(tmp, o));
        }
        finally { File.Delete(tmp); }
    }

    // ── Folder excludes ──────────────────────────────────────────────────
    private static FindEngine.Options Excl(string list, bool matchCase = false, bool regex = false) =>
        new("", null, Recursive: true, matchCase, regex, Hidden: true, ExcludeFolders: list);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(";")]
    [InlineData(" ; ;; ")]
    public void ExcludeMatchers_BlankListYieldsNoMatchers(string? list) =>
        Assert.Empty(FindEngine.ExcludeMatchers(Excl(list!)));

    [Fact]
    public void ExcludeMatchers_DropsBlanksAndTrimsEntries()
    {
        // Trailing semicolon and padded entries — a blank one would become the empty
        // pattern, which matches everything and would prune the whole tree.
        var m = FindEngine.ExcludeMatchers(Excl(" .git ; .vs ; "));
        Assert.Equal(2, m.Length);
        Assert.Equal(".git", m[0].Pattern);
        Assert.Equal(".vs", m[1].Pattern);
        Assert.False(FindEngine.FolderExcluded("src", m));
    }

    [Theory]
    [InlineData(".git;.vs", ".git", true)]
    [InlineData(".git;.vs", ".vs", true)]
    [InlineData(".git;.vs", "src", false)]
    [InlineData(".g*", ".github", true)]          // glob
    [InlineData(".g*", "src", false)]
    [InlineData("node_modules", "node_modules", true)]
    [InlineData("bin", "bin", true)]
    [InlineData("bin", "binaries", true)]         // substring, same as the pattern field
    [InlineData("bin", "src", false)]
    public void FolderExcluded_UsesTheDialogsOwnMatching(string list, string name, bool expected) =>
        Assert.Equal(expected, FindEngine.FolderExcluded(name, FindEngine.ExcludeMatchers(Excl(list))));

    [Fact]
    public void FolderExcluded_IgnoresRegexMode()
    {
        // "^bin$" is a regex that matches "bin" and a substring that doesn't. Flipping Regex
        // on for the filename pattern must not re-interpret the folder list.
        var m = FindEngine.ExcludeMatchers(Excl("^bin$", regex: true));
        Assert.False(FindEngine.FolderExcluded("bin", m));
        Assert.True(FindEngine.FolderExcluded("x^bin$y", m));
    }

    [Fact]
    public void FolderExcluded_HonorsMatchCase()
    {
        Assert.True(FindEngine.FolderExcluded("BIN", FindEngine.ExcludeMatchers(Excl("bin"))));
        Assert.False(FindEngine.FolderExcluded("BIN", FindEngine.ExcludeMatchers(Excl("bin", matchCase: true))));
    }

    // ── Walking a real tree: depth bound and pruning ─────────────────────
    /// <summary>A throwaway folder tree; each argument is a "/"-separated relative file path
    /// whose folders are created on the way.</summary>
    private sealed class Tree : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "vcfind_" + Guid.NewGuid().ToString("N"));

        public Tree(params string[] files)
        {
            foreach (var rel in files)
            {
                var full = Path.Combine(Root, rel.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllText(full, "x");
            }
        }

        public void Dispose() { try { Directory.Delete(Root, true); } catch { } }
    }

    /// <summary>Walk to completion and return the hit names. Hidden is on throughout: .NET
    /// marks dot-prefixed entries hidden on Unix, and these fixtures use ".git".</summary>
    private static async Task<List<string>> Walk(string root, FindEngine.Options o)
    {
        var hits = new List<string>();
        await FindEngine.Run(root, o, h => hits.Add(h.Name), (_, _) => { }, _ => { },
            CancellationToken.None);
        hits.Sort(StringComparer.Ordinal);
        return hits;
    }

    private static FindEngine.Options Walked(string pattern, int maxDepth = 0, string? exclude = null) =>
        new(pattern, null, Recursive: true, MatchCase: false, Regex: false, Hidden: true,
            MaxDepth: maxDepth, ExcludeFolders: exclude);

    [Fact]
    public async Task MaxDepth_ZeroIsUnbounded()
    {
        using var t = new Tree("a.txt", "one/b.txt", "one/two/c.txt", "one/two/three/d.txt");
        Assert.Equal(["a.txt", "b.txt", "c.txt", "d.txt"], await Walk(t.Root, Walked("*.txt")));
    }

    [Fact]
    public async Task MaxDepth_OneStopsAtTheImmediateSubfolders()
    {
        // "1 level" = this folder and its subfolders; their subfolders are not entered.
        using var t = new Tree("a.txt", "one/b.txt", "one/two/c.txt", "one/two/three/d.txt");
        Assert.Equal(["a.txt", "b.txt"], await Walk(t.Root, Walked("*.txt", maxDepth: 1)));
    }

    [Fact]
    public async Task MaxDepth_CountsLevelsBelowTheRoot()
    {
        using var t = new Tree("a.txt", "one/b.txt", "one/two/c.txt", "one/two/three/d.txt");
        Assert.Equal(["a.txt", "b.txt", "c.txt"], await Walk(t.Root, Walked("*.txt", maxDepth: 2)));
        Assert.Equal(["a.txt", "b.txt", "c.txt", "d.txt"], await Walk(t.Root, Walked("*.txt", maxDepth: 9)));
    }

    [Fact]
    public async Task Exclude_PrunesTheFolderAtEveryLevel()
    {
        using var t = new Tree(
            "a.txt", ".git/x.txt", "src/b.txt", "src/.git/y.txt", "src/deep/.git/z.txt");
        Assert.Equal(["a.txt", "b.txt"], await Walk(t.Root, Walked("*.txt", exclude: ".git;.vs")));
    }

    [Fact]
    public async Task Exclude_DoesNotReportTheFolderItself()
    {
        using var t = new Tree("skipme/x.txt", "src/skipme/y.txt");
        Assert.Empty(await Walk(t.Root, Walked("skipme", exclude: "skipme")));
    }

    [Fact]
    public async Task Exclude_NeverDropsAFileOfTheSameName()
    {
        // The list is about folders. A file called "notes" (or notes.txt) still matches.
        using var t = new Tree("notes.txt", "src/notes.txt", "notes/hidden.txt");
        Assert.Equal(["notes.txt", "notes.txt"], await Walk(t.Root, Walked("notes", exclude: "notes")));
    }

    [Fact]
    public async Task Exclude_EmptyListChangesNothing()
    {
        using var t = new Tree("a.txt", "one/b.txt");
        Assert.Equal(["a.txt", "b.txt"], await Walk(t.Root, Walked("*.txt", exclude: " ; ")));
    }

    // ── Archives follow the same rules ───────────────────────────────────
    /// <summary>An archive's members continue the on-disk depth count, so a zip sitting in the
    /// search root has its top-level members at level 1 and a member folder's contents at 2.</summary>
    private static string MakeZip(string dir)
    {
        var zip = Path.Combine(dir, "assets.zip");
        using var z = System.IO.Compression.ZipFile.Open(zip, System.IO.Compression.ZipArchiveMode.Create);
        foreach (var name in new[] { "readme.md", "textures/palette.md", "textures/hi_res/stone.md" })
        {
            using var s = new StreamWriter(z.CreateEntry(name).Open());
            s.Write("data");
        }
        return zip;
    }

    private static FindEngine.Options Arc(string pattern, int maxDepth = 0, string? exclude = null) =>
        new(pattern, null, Recursive: true, MatchCase: false, Regex: false, Hidden: true,
            Archives: true, SevenZip: false, MaxDepth: maxDepth, ExcludeFolders: exclude);

    [Fact]
    public async Task Archive_DepthBoundsMembersLikeFolders()
    {
        using var t = new Tree("a.md");
        MakeZip(t.Root);
        Assert.Equal(["a.md", "palette.md", "readme.md", "stone.md"], await Walk(t.Root, Arc("*.md")));
        Assert.Equal(["a.md", "readme.md"], await Walk(t.Root, Arc("*.md", maxDepth: 1)));
        Assert.Equal(["a.md", "palette.md", "readme.md"], await Walk(t.Root, Arc("*.md", maxDepth: 2)));
    }

    [Fact]
    public async Task Archive_ExcludesPruneMemberFolders()
    {
        using var t = new Tree("a.md");
        MakeZip(t.Root);
        Assert.Equal(["a.md", "readme.md"], await Walk(t.Root, Arc("*.md", exclude: "textures")));
    }
}
