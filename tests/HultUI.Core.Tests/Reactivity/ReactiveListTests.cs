using HultUI.Core.Reactivity;
using Xunit;

namespace HultUI.Core.Tests.Reactivity;

[Collection("Reactive")]
public class ReactiveListTests
{
    [Fact]
    public void Add_TriggersEffect()
    {
        var list = new ReactiveList<string>(["a", "b"]);
        int runCount = 0;

        var effect = new Effect(() =>
        {
            _ = list.Count; // track dependency
            runCount++;
        });

        Assert.Equal(1, runCount); // initial run

        list.Add("c");
        Assert.Equal(2, runCount);
        Assert.Equal(3, list.Count);
    }

    [Fact]
    public void RemoveAt_TriggersEffect()
    {
        var list = new ReactiveList<string>(["a", "b", "c"]);
        int runCount = 0;

        var effect = new Effect(() =>
        {
            _ = list.Count;
            runCount++;
        });

        list.RemoveAt(1);
        Assert.Equal(2, runCount);
        Assert.Equal(2, list.Count);
        Assert.Equal("c", list[1]);
    }

    [Fact]
    public void RemoveAll_TriggersEffect()
    {
        var list = new ReactiveList<int>([1, 2, 3, 4, 5]);
        int runCount = 0;

        var effect = new Effect(() =>
        {
            _ = list.Count;
            runCount++;
        });

        int removed = list.RemoveAll(x => x % 2 == 0);
        Assert.Equal(2, removed);
        Assert.Equal(2, runCount);
        Assert.Equal(3, list.Count);
    }

    [Fact]
    public void RemoveAll_NoMatch_DoesNotTrigger()
    {
        var list = new ReactiveList<int>([1, 3, 5]);
        int runCount = 0;

        var effect = new Effect(() =>
        {
            _ = list.Count;
            runCount++;
        });

        list.RemoveAll(x => x % 2 == 0);
        Assert.Equal(1, runCount); // only initial run
    }

    [Fact]
    public void IndexerSet_TriggersEffect()
    {
        var list = new ReactiveList<string>(["a", "b"]);
        int runCount = 0;

        var effect = new Effect(() =>
        {
            foreach (var _ in list) { } // track via iteration
            runCount++;
        });

        list[0] = "z";
        Assert.Equal(2, runCount);
        Assert.Equal("z", list[0]);
    }

    [Fact]
    public void Clear_TriggersEffect()
    {
        var list = new ReactiveList<int>([1, 2, 3]);
        int runCount = 0;

        var effect = new Effect(() =>
        {
            _ = list.Count;
            runCount++;
        });

        list.Clear();
        Assert.Equal(2, runCount);
        Assert.Empty(list);
    }

    [Fact]
    public void Clear_Empty_DoesNotTrigger()
    {
        var list = new ReactiveList<int>();
        int runCount = 0;

        var effect = new Effect(() =>
        {
            _ = list.Count;
            runCount++;
        });

        list.Clear();
        Assert.Equal(1, runCount); // only initial run
    }

    [Fact]
    public void Iteration_TracksDependency()
    {
        var list = new ReactiveList<string>(["a"]);
        string? lastItem = null;

        var effect = new Effect(() =>
        {
            lastItem = list.LastOrDefault();
        });

        Assert.Equal("a", lastItem);

        list.Add("b");
        Assert.Equal("b", lastItem);
    }

    [Fact]
    public void Insert_TriggersEffect()
    {
        var list = new ReactiveList<string>(["a", "c"]);
        int runCount = 0;

        var effect = new Effect(() =>
        {
            _ = list.Count;
            runCount++;
        });

        list.Insert(1, "b");
        Assert.Equal(2, runCount);
        Assert.Equal("b", list[1]);
    }

    [Fact]
    public void Remove_TriggersEffect()
    {
        var list = new ReactiveList<string>(["a", "b"]);
        int runCount = 0;

        var effect = new Effect(() =>
        {
            _ = list.Count;
            runCount++;
        });

        bool removed = list.Remove("a");
        Assert.True(removed);
        Assert.Equal(2, runCount);

        removed = list.Remove("nonexistent");
        Assert.False(removed);
        Assert.Equal(2, runCount); // no change
    }
}
