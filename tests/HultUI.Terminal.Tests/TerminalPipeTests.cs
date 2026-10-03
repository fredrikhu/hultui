using Xunit;

namespace HultUI.Terminal.Tests;

/// <summary><see cref="TerminalPipe"/>: blocking reads, completion, and a capacity's back-pressure.</summary>
public class TerminalPipeTests
{
    [Fact]
    public void ARead_ReturnsWhatWasWritten_InOrder_AndZeroOnceCompletedAndEmpty()
    {
        var pipe = new TerminalPipe();
        pipe.Write("abc"u8);
        pipe.Write("de"u8);
        pipe.Complete();

        var buffer = new byte[4];
        Assert.Equal(3, pipe.Read(buffer));          // one write at a time
        Assert.Equal(2, pipe.Read(buffer));
        Assert.Equal(0, pipe.Read(buffer));
        pipe.Write("ignored"u8);                     // dropped, not thrown
        Assert.Equal(0, pipe.Read(buffer));
    }

    [Fact]
    public async Task AWaitingRead_WakesOnCompletion()
    {
        var pipe = new TerminalPipe();
        var read = Task.Run(() => pipe.Read(new byte[8]));
        Thread.Sleep(50);
        Assert.False(read.IsCompleted);
        pipe.Complete();
        await Within(read);
        Assert.Equal(0, await read);
    }

    [Fact]
    public async Task WithACapacity_AWriteWaitsForTheReader()
    {
        var pipe = new TerminalPipe(capacity: 4);
        pipe.Write("abcd"u8);                        // fills it
        var write = Task.Run(() => pipe.Write("ef"u8));
        Thread.Sleep(50);
        Assert.False(write.IsCompleted);

        var buffer = new byte[2];
        Assert.Equal(2, pipe.Read(buffer));          // room again
        await Within(write);
    }

    [Fact]
    public async Task WithACapacity_AWaitingWrite_IsReleasedByCompletion()
    {
        var pipe = new TerminalPipe(capacity: 1);
        pipe.Write("a"u8);
        var write = Task.Run(() => pipe.Write("b"u8));
        Thread.Sleep(50);
        pipe.Complete();
        await Within(write);
    }

    [Fact]
    public void ANegativeCapacity_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new TerminalPipe(-1));

    private static async Task Within(Task task) =>
        Assert.Same(task, await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(5))));
}
