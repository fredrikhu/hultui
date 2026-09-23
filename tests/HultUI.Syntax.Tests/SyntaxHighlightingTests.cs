using System.Linq;
using HultUI.Syntax;
using Xunit;

namespace HultUI.Syntax.Tests;

/// <summary>
/// Which file types the preview colours. TextMateSharp bundles 64 languages keyed by
/// extension; anything outside that table arrives unhighlighted unless it is aliased
/// to a grammar that genuinely parses it.
/// </summary>
public class SyntaxHighlightingTests
{
    private const string Xml = "<Solution>\n  <!-- comment -->\n  <Project Path=\"a.csproj\" />\n</Solution>\n";
    private const string Json = "{\n  \"solution\": { \"path\": \"a.sln\" }\n}\n";

    /// <summary>True when the file resolved to a grammar AND that grammar coloured something.</summary>
    private static bool Highlights(string fileName, string text)
    {
        var lines = SyntaxHighlighter.TokenizeLines(fileName, text);
        return lines is not null && lines.Any(l => l.Count > 0);
    }

    [Theory]
    [InlineData("Solution.slnx")]        // aliased: the XML solution format
    [InlineData("app.config")]           // aliased: app/web/packages/NuGet.config
    [InlineData("Strings.resx")]
    [InlineData("app.manifest")]
    [InlineData("ci.runsettings")]
    [InlineData("Info.plist")]
    [InlineData("Project.csproj")]       // already in the bundle — pinned so an alias
    [InlineData("Directory.Build.targets")]  // rewrite can't quietly drop them
    [InlineData("Directory.Build.props")]
    public void XmlFamilyHighlights(string fileName)
    {
        Assert.True(Highlights(fileName, Xml), fileName);
    }

    [Theory]
    [InlineData("Solution.slnf")]        // aliased: solution filter
    [InlineData("events.jsonl")]
    [InlineData("settings.json")]        // already in the bundle
    public void JsonFamilyHighlights(string fileName)
    {
        Assert.True(Highlights(fileName, Json), fileName);
    }

    [Theory]
    [InlineData("build.ps1")]
    [InlineData("Module.psm1")]
    [InlineData("Module.psd1")]
    public void PowerShellHighlights(string fileName)
    {
        // Bundled outright — no alias needed, and it already worked before the
        // alias table existed.
        Assert.True(Highlights(fileName, "# comment\n$x = Get-ChildItem -Path 'C:\\'\n"), fileName);
    }

    [Theory]
    [InlineData("Dockerfile", "FROM alpine:3\nRUN echo hi\n")]
    [InlineData("Makefile", "all:\n\techo hi\n")]
    // Groovy leaves bare identifiers at the default foreground, so the sample needs
    // a comment or a string for there to be anything to colour.
    [InlineData("Jenkinsfile", "// build\npipeline { agent 'any' }\n")]
    [InlineData("Gemfile", "source 'https://rubygems.org'\n")]
    public void ExtensionlessFilesResolveByName(string fileName, string text)
    {
        // Two of the most-opened files in any repository carry their language in the
        // name; extension-only lookup showed them flat grey.
        Assert.True(Highlights(fileName, text), fileName);
    }

    [Fact]
    public void AliasesShareTheGrammarOfTheirTarget()
    {
        // .slnx really is XML, so it must colour identically to the same text in a
        // .xml file — an alias that resolved to some other grammar would still pass
        // a bare "did it colour anything" check.
        var viaAlias = SyntaxHighlighter.SpansForText("Solution.slnx", Xml);
        var viaXml = SyntaxHighlighter.SpansForText("Solution.xml", Xml);

        Assert.NotEmpty(viaXml);
        Assert.Equal(viaXml, viaAlias);
    }

    [Theory]
    // The set Vesper's preview used to gate on (IconKind.Code) — a hand-kept list of
    // 25 extensions for choosing a glyph, which quietly decided highlighting too.
    [InlineData("a.cs")] [InlineData("a.json")] [InlineData("a.xml")] [InlineData("a.md")]
    // …and the ones that list omitted, which is why they coloured in Junction's diff
    // but not in Vesper's preview. CanHighlight is now the single answer for both.
    [InlineData("a.csproj")] [InlineData("a.slnx")] [InlineData("a.props")]
    [InlineData("a.targets")] [InlineData("a.ps1")] [InlineData("a.config")]
    [InlineData("a.fs")] [InlineData("a.swift")] [InlineData("a.lua")] [InlineData("a.dart")]
    [InlineData("Dockerfile")] [InlineData("Makefile")]
    public void CanHighlight_IsTheSingleGateBothAppsAskNow(string fileName)
    {
        Assert.True(SyntaxHighlighter.CanHighlight(fileName), fileName);
    }

    [Theory]
    [InlineData("photo.png")]
    [InlineData("archive.zip")]
    [InlineData("notes.unknownext")]
    public void CanHighlight_IsFalseWithoutAGrammar(string fileName)
    {
        Assert.False(SyntaxHighlighter.CanHighlight(fileName), fileName);
    }

    [Fact]
    public void CanHighlight_AgreesWithWhetherTokenizingProducesAnything()
    {
        // The predicate must not promise colour the tokenizer won't deliver, or a
        // caller that branches on it renders an empty highlight path.
        foreach (var name in new[] { "a.cs", "a.slnx", "a.ps1", "Dockerfile", "photo.png" })
        {
            bool claimed = SyntaxHighlighter.CanHighlight(name);
            bool resolved = SyntaxHighlighter.TokenizeLines(name, "// x\n<a/>\n$y = 1\nFROM alpine\n") is not null;
            Assert.Equal(claimed, resolved);
        }
    }

    [Theory]
    [InlineData("Cargo.toml")]
    [InlineData("Main.kt")]
    [InlineData("MySolution.sln")]
    public void FormatsWithNoBundledGrammarStayPlain(string fileName)
    {
        // Documented ceiling, not an oversight: TextMateSharp ships no TOML, Kotlin,
        // or .sln grammar. Aliasing them to a lookalike (TOML onto INI, say) would
        // mis-colour more than it coloured, so they are deliberately left alone.
        Assert.False(Highlights(fileName, "key = \"value\"\n[section]\n"), fileName);
    }

    [Theory]
    [InlineData("csharp", "var x = \"s\"; // c")]   // a grammar's id
    [InlineData("C#", "var x = \"s\"; // c")]       // one of its aliases
    [InlineData("cs", "var x = \"s\"; // c")]       // its extension, as a fence often spells it
    [InlineData("bash", "echo \"hi\" # c")]
    [InlineData("yml", "key: \"value\" # c")]
    [InlineData("json", "{ \"a\": 1 }")]
    public void AMarkdownFenceNamesItsLanguage(string language, string code)
    {
        Assert.NotEmpty(SyntaxHighlighter.SpansForLanguage(language, code));
    }

    [Theory]
    [InlineData("")]
    [InlineData("nosuchlanguage")]
    [InlineData("../cs")]
    [InlineData("a.cs")]
    public void AFenceWithNoKnownLanguageStaysPlain(string language)
    {
        Assert.Empty(SyntaxHighlighter.SpansForLanguage(language, "var x = \"s\"; // c"));
    }
}
