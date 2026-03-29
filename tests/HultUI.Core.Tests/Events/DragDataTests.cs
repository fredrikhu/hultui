using HultUI.Core.Events;
using Xunit;

namespace HultUI.Core.Tests.Events;

public class DragDataTests
{
    [Fact]
    public void Set_And_TryGet_ReturnsValue()
    {
        var data = new DragData();
        data.Set("id", 42);

        Assert.True(data.TryGet<int>("id", out var value));
        Assert.Equal(42, value);
    }

    [Fact]
    public void TryGet_WrongType_ReturnsFalse()
    {
        var data = new DragData();
        data.Set("id", 42);

        Assert.False(data.TryGet<string>("id", out _));
    }

    [Fact]
    public void TryGet_MissingKey_ReturnsFalse()
    {
        var data = new DragData();
        Assert.False(data.TryGet<int>("nope", out _));
    }

    [Fact]
    public void Has_ReturnsCorrectly()
    {
        var data = new DragData();
        Assert.False(data.Has("key"));

        data.Set("key", "value");
        Assert.True(data.Has("key"));
    }

    [Fact]
    public void Files_AccumulatesCorrectly()
    {
        var data = new DragData();
        Assert.False(data.HasFiles);

        data.Files.Add("/tmp/file1.txt");
        data.Files.Add("/tmp/file2.txt");

        Assert.True(data.HasFiles);
        Assert.Equal(2, data.Files.Count);
    }

    [Fact]
    public void Text_Property()
    {
        var data = new DragData();
        Assert.False(data.HasText);

        data.Text = "hello";
        Assert.True(data.HasText);
        Assert.Equal("hello", data.Text);
    }

    [Fact]
    public void Set_OverwritesPreviousValue()
    {
        var data = new DragData();
        data.Set("key", 1);
        data.Set("key", 2);

        Assert.True(data.TryGet<int>("key", out var value));
        Assert.Equal(2, value);
    }
}
