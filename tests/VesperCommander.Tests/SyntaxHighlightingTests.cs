using System.Linq;
using Xunit;

namespace VesperCommander.Tests;

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
}
