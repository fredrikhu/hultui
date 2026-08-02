using VesperCommander;
using Xunit;

namespace VesperCommander.Tests;

public class SyncEngineTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0);
    private const SyncEngine.CompareMode DateSize = SyncEngine.CompareMode.DateSize;
    private const SyncEngine.CompareMode Content = SyncEngine.CompareMode.Content;

    [Fact]
    public void OnlyLeft_WhenRightAbsent()
    {
        var r = SyncEngine.Classify(true, 10, T0, false, 0, default, DateSize, null);
        Assert.Equal(SyncEngine.Rel.OnlyLeft, r);
    }

    [Fact]
    public void OnlyRight_WhenLeftAbsent()
    {
        var r = SyncEngine.Classify(false, 0, default, true, 10, T0, DateSize, null);
        Assert.Equal(SyncEngine.Rel.OnlyRight, r);
    }

    [Fact]
    public void Equal_WhenSameSizeAndTime()
    {
        var r = SyncEngine.Classify(true, 100, T0, true, 100, T0, DateSize, null);
        Assert.Equal(SyncEngine.Rel.Equal, r);
    }

    [Fact]
    public void LeftNewer_WhenLeftTimeAhead()
    {
        var r = SyncEngine.Classify(true, 100, T0.AddMinutes(5), true, 90, T0, DateSize, null);
        Assert.Equal(SyncEngine.Rel.LeftNewer, r);
    }

    [Fact]
    public void RightNewer_WhenRightTimeAhead()
    {
        var r = SyncEngine.Classify(true, 90, T0, true, 100, T0.AddMinutes(5), DateSize, null);
        Assert.Equal(SyncEngine.Rel.RightNewer, r);
    }

    [Fact]
    public void Conflict_WhenSameTimeDifferentSize()
    {
        var r = SyncEngine.Classify(true, 100, T0, true, 120, T0, DateSize, null);
        Assert.Equal(SyncEngine.Rel.Conflict, r);
    }

    [Fact]
    public void MtimeToleranceWindow_CountsAsEqualBySize()
    {
        // Within the 2s tolerance and same size ⇒ Equal (clock jitter shouldn't split them).
        var r = SyncEngine.Classify(true, 100, T0, true, 100, T0.AddSeconds(1), DateSize, null);
        Assert.Equal(SyncEngine.Rel.Equal, r);
    }

    [Fact]
    public void ContentMode_EqualWhenBytesMatch()
    {
        var r = SyncEngine.Classify(true, 100, T0, true, 100, T0.AddHours(9), Content, contentEqual: true);
        Assert.Equal(SyncEngine.Rel.Equal, r);
    }

    [Fact]
    public void ContentMode_ConflictWhenBytesDiffer()
    {
        var r = SyncEngine.Classify(true, 100, T0, true, 100, T0, Content, contentEqual: false);
        Assert.Equal(SyncEngine.Rel.Conflict, r);
    }
}
