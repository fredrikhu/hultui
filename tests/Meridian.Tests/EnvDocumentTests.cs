using Meridian.Core;
using Xunit;

namespace Meridian.Tests;

public class EnvDocumentTests
{
    [Fact]
    public void KeysValuesAndSecretsParse()
    {
        var doc = EnvDocument.Parse("""
            # local
            base_url=http://localhost:8080
            timeout_ms=30000
            secret client_secret
            """);

        Assert.Empty(doc.Errors);
        Assert.Equal("http://localhost:8080", doc.Values["base_url"]);
        Assert.Equal("30000", doc.Values["timeout_ms"]);
        Assert.True(doc.IsSecret("client_secret"));
        Assert.False(doc.IsSecret("base_url"));
        Assert.Equal(["base_url", "timeout_ms", "client_secret"], doc.Keys);
    }

    [Fact]
    public void ValuesAreVerbatimAfterTheFirstEquals()
    {
        var doc = EnvDocument.Parse("query=a=b= c \n");
        Assert.Equal("a=b= c ", doc.Values["query"]);
    }

    [Fact]
    public void ADuplicateKeyKeepsTheLaterValueAndItsFirstPosition()
    {
        var doc = EnvDocument.Parse("a=1\nb=2\na=3\n");
        Assert.Equal("3", doc.Values["a"]);
        Assert.Equal(["a", "b"], doc.Keys);
    }

    [Fact]
    public void ALineWithoutEqualsIsALineNumberedError()
    {
        var doc = EnvDocument.Parse("a=1\nnot a pair\n");
        var error = Assert.Single(doc.Errors);
        Assert.Equal(2, error.Line);
    }

    [Fact]
    public void ABareSecretIsAnError()
    {
        var doc = EnvDocument.Parse("secret \n");
        Assert.Single(doc.Errors);
    }

    [Theory]
    [InlineData("# c\na=1\nsecret k\n\n")]
    [InlineData("a=1\r\nsecret k\r\n")]
    [InlineData("broken line\na=1")]
    public void SerializeReproducesTheInputExactly(string text)
    {
        Assert.Equal(text, EnvDocument.Parse(text).Serialize());
    }
}
