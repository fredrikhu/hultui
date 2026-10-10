using HultUI.Syntax;
using TextMateSharp.Grammars;
using Xunit;

namespace HultUI.Syntax.Tests;

/// <summary>
/// <see cref="SyntaxHighlighter.CanHighlight"/> answers from a table of the bundled grammars'
/// extensions. It used to ask TextMate's registry, and the first file list that showed a code
/// file built the registry on the UI thread for it: 4.6–5.3 MB and 7–10 ms in one frame of
/// Vesper (plan 17). The table must answer what the registry answers.
/// </summary>
public class GrammarTableTests
{
    private static readonly RegistryOptions Bundled = new(ThemeName.DarkPlus);

    /// <summary>What CanHighlight answered when it asked the registry.</summary>
    private static bool RegistryAnswer(string path) =>
        SyntaxHighlighter.ResolveExtension(path) is { } ext && Bundled.GetScopeByExtension(ext) != null;

    [Fact]
    public void CanHighlight_AnswersAsTheRegistryDoes_ForEveryExtensionItLists()
    {
        var listed = Bundled.GetAvailableLanguages().SelectMany(l => l.Extensions ?? []).Distinct().ToList();
        Assert.True(listed.Count > 250, $"the registry lists only {listed.Count} extensions");

        var paths = new List<string>();
        foreach (var ext in listed)
        {
            // The extension, in capitals, and near misses on either side of it.
            foreach (var e in new[] { ext, ext.ToUpperInvariant(), ext + "x", ext[..^1], ".x" + ext[1..] })
            {
                Assert.True(Bundled.GetScopeByExtension(e) != null == BundledGrammarExtensions.All.Contains(e),
                    $"the table says {BundledGrammarExtensions.All.Contains(e)} for '{e}', the registry the opposite");
                paths.Add("file" + e);
            }
        }
        // Through the file names and aliases CanHighlight resolves before it looks anything up.
        paths.AddRange(["Solution.slnx", "app.config", "events.jsonl", "a.code-workspace", "Dockerfile",
            "Containerfile", "Makefile", "GNUmakefile", "Jenkinsfile", "Gemfile", "README", "LICENSE",
            "Cargo.toml", "Main.kt", "App.sln", "notes.txt", "photo.png", "PROGRAM.CS", "x.", ".gitignore"]);

        foreach (var path in paths)
            Assert.True(RegistryAnswer(path) == SyntaxHighlighter.CanHighlight(path),
                $"CanHighlight(\"{path}\") is {SyntaxHighlighter.CanHighlight(path)}, the registry says {RegistryAnswer(path)}");
    }

    [Fact]
    public void TheTable_ListsNothingTheRegistryHasNoGrammarFor()
    {
        foreach (var ext in BundledGrammarExtensions.All)
            Assert.True(Bundled.GetScopeByExtension(ext) != null, $"'{ext}' has no grammar in the registry");
    }

    [Fact]
    public void CanHighlight_NeverTouchesTheRegistry()
    {
        int before = SyntaxHighlighter.RegistryUsesOnThisThread;
        foreach (var path in new[] { "Program.cs", "notes.txt", "Dockerfile", "a.slnx", "photo.png", "x.neverseen" })
            SyntaxHighlighter.CanHighlight(path);
        Assert.Equal(before, SyntaxHighlighter.RegistryUsesOnThisThread);

        // A tokenize does need it, which shows the count sees a use when there is one.
        SyntaxHighlighter.TokenizeLines("a.cs", "var x = 1;");
        Assert.True(SyntaxHighlighter.RegistryUsesOnThisThread > before);
    }
}
