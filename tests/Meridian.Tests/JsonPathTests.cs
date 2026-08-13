using Meridian.Core;
using Xunit;

namespace Meridian.Tests;

public class JsonPathTests
{
    private const string Charge = """
        {
          "id": "ch_9f2",
          "amount": 2499,
          "captured": true,
          "refunds": { "total_count": 0, "items": [ { "id": "re_1" }, { "id": "re_2" } ] },
          "tags": null
        }
        """;

    [Theory]
    [InlineData("$.id", "ch_9f2")]                     // string: unquoted
    [InlineData("$.amount", "2499")]                   // number: raw
    [InlineData("$.captured", "true")]                 // bool: raw
    [InlineData("$.tags", "null")]                     // null: raw
    [InlineData("$.refunds.total_count", "0")]         // nested
    [InlineData("$.refunds.items[0].id", "re_1")]      // index
    [InlineData("$.refunds.items[1].id", "re_2")]
    [InlineData("$['id']", "ch_9f2")]                  // bracket-name form
    public void PathsSelectValues(string path, string expected)
    {
        var result = JsonPath.Evaluate(path, Charge);
        Assert.Null(result.Error);
        Assert.Equal(expected, result.First);
    }

    [Fact]
    public void StarFansOutOverAnArray()
    {
        var result = JsonPath.Evaluate("$.refunds.items[*].id", Charge);
        Assert.Equal(["re_1", "re_2"], result.Matches);
    }

    [Fact]
    public void AnObjectValueIsItsRawJson()
    {
        var result = JsonPath.Evaluate("$.refunds", Charge);
        Assert.Contains("\"total_count\"", result.First);
    }

    [Theory]
    [InlineData("$.missing")]
    [InlineData("$.refunds.items[9]")]
    [InlineData("$.id.deeper")]        // string has no properties
    public void SelectingNothingIsAStateNotAnError(string path)
    {
        var result = JsonPath.Evaluate(path, Charge);
        Assert.Null(result.Error);
        Assert.False(result.HasMatch);
    }

    [Theory]
    [InlineData("id", "$")]                  // must start with $
    [InlineData("$.a[", "unclosed")]
    [InlineData("$.a[x]", "not an index")]
    [InlineData("$..a", "empty name")]       // recursive descent is out of the subset
    public void BadPathsSayWhy(string path, string fragment)
    {
        var result = JsonPath.Evaluate(path, Charge);
        Assert.NotNull(result.Error);
        Assert.Contains(fragment, result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(result.Error, JsonPath.Validate(path));
    }

    [Fact]
    public void NonJsonInputIsAnError()
    {
        Assert.NotNull(JsonPath.Evaluate("$.a", "<html>nope</html>").Error);
    }
}
