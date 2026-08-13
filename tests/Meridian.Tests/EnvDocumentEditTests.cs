using Meridian.Core;
using Xunit;

namespace Meridian.Tests;

/// <summary>The env edit API's contract, same as ReqDocument's: rewrite only
/// your own line, keep every other byte.</summary>
public class EnvDocumentEditTests
{
    [Fact]
    public void SetValueRewritesTheKeysLineInPlace()
    {
        var doc = EnvDocument.Parse("# local\r\nbase_url=http://old\r\ntimeout_ms=30000\r\n");
        doc.SetValue("base_url", "http://new");

        Assert.Equal("# local\r\nbase_url=http://new\r\ntimeout_ms=30000\r\n", doc.Serialize());
        Assert.True(doc.Edited);
    }

    [Fact]
    public void SetValueAppendsWhenTheKeyIsNew()
    {
        var doc = EnvDocument.Parse("a=1\n");
        doc.SetValue("b", "2");

        Assert.Equal("a=1\nb=2\n", doc.Serialize());
    }

    [Fact]
    public void SetValueOnADuplicateKeyRewritesTheWinningLine()
    {
        // The later line wins at parse time, so that is the one an edit targets.
        var doc = EnvDocument.Parse("a=1\nb=2\na=3\n");
        doc.SetValue("a", "9");

        Assert.Equal("a=1\nb=2\na=9\n", doc.Serialize());
        Assert.Equal("9", doc.Values["a"]);
    }

    [Fact]
    public void AppendingAfterAFileWithNoFinalNewlineWorks()
    {
        var doc = EnvDocument.Parse("a=1");
        doc.SetValue("b", "2");

        Assert.Equal("a=1\nb=2\n", doc.Serialize());
    }

    [Fact]
    public void MarkSecretAppendsTheFlagOnce()
    {
        var doc = EnvDocument.Parse("token=placeholder\n");
        doc.MarkSecret("token");
        doc.MarkSecret("token");

        Assert.Equal("token=placeholder\nsecret token\n", doc.Serialize());
        Assert.True(doc.IsSecret("token"));
    }

    [Fact]
    public void MarkSavedClearsEdited()
    {
        var doc = EnvDocument.Parse("a=1\n");
        doc.SetValue("a", "2");
        doc.MarkSaved();
        Assert.False(doc.Edited);
    }
}
