using HultUI.Core.Events;
using HultUI.Headless;
using HultUI.Terminal.Emulation;
using HultUI.Terminal.Testing;
using Xunit;

namespace HultUI.Terminal.Tests;

/// <summary>
/// A connection made in the background: the terminal shows what it is connecting to at once,
/// keeps what is typed and the size it is given, then hands over to the program; a failure is
/// written into the terminal and ends the session; terminating cancels the connecting.
/// </summary>
public sealed class DeferredConnectionTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly HeadlessSession _session = HeadlessSession.Create(400, 300);
    private readonly TaskCompletionSource<ITerminalConnection> _made = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CancellationToken _token;

    public void Dispose() => _session.Dispose();

    private DeferredConnection Deferred(string? connecting = "Connecting to example…", Func<Exception, string>? describe = null) =>
        new(async ct => { _token = ct; return await _made.Task; }, connecting, describe);

    private void WaitFor(Func<bool> condition) => _session.PumpUntil(condition, Patience);

    private static string Screen(TerminalController controller) => controller.ReadScreen();

    [Fact]
    public void TheTerminalShows_WhatItIsConnectingTo_ThenTheProgram()
    {
        using var controller = new TerminalController(Deferred());
        controller.Resize(80, 24);
        WaitFor(() => Screen(controller).Contains("Connecting to example…"));

        controller.Send("typed early");
        controller.Resize(100, 30);
        var program = new FakeConnection();
        _made.SetResult(program);

        program.Emit("hello from the program");
        WaitFor(() => Screen(controller).Contains("hello from the program"));
        WaitFor(() => program.Received.Contains("typed early"));
        Assert.Equal((100, 30), program.Resizes[^1]);

        controller.Send(" and later");
        WaitFor(() => program.Received.EndsWith("typed early and later", StringComparison.Ordinal));

        program.End(4);
        WaitFor(() => controller.State == TerminalState.Exited);
        Assert.Equal(4, controller.ExitCode);
    }

    [Fact]
    public void AFailure_IsWrittenIntoTheTerminal_AndEndsTheSession()
    {
        var deferred = new DeferredConnection(_ => throw new InvalidOperationException("no route to host"),
            "Connecting to example…", ex => "Could not connect: " + ex.Message);
        using var controller = new TerminalController(deferred);
        controller.Resize(80, 24);

        WaitFor(() => controller.State == TerminalState.Exited);
        Assert.Contains("Connecting to example…\nCould not connect: no route to host", Screen(controller));
        Assert.Null(controller.ExitCode);
        Assert.IsType<InvalidOperationException>(deferred.Failure);
        Assert.Null(deferred.Connection);
    }

    [Fact]
    public void Terminating_WhileConnecting_CancelsIt_AndAConnectionThatComesAnyway_IsClosed()
    {
        var deferred = Deferred();
        int? exit = -1;
        deferred.Exited += code => exit = code;
        SpinWait.SpinUntil(() => _token.CanBeCanceled, Patience);

        deferred.Terminate();
        Assert.True(_token.IsCancellationRequested);
        Assert.Null(exit);
        Assert.Equal("Connecting to example…\r\n", ReadToEnd(deferred.Output)); // the notice, then the end

        var late = new FakeConnection();
        _made.SetResult(late);
        Assert.True(SpinWait.SpinUntil(() => late.IsDisposed, Patience));
        Assert.Null(deferred.Connection);
        Assert.Null(deferred.Failure); // cancelled is not failed
    }

    /// <summary>A pseudoterminal is best started at the size it is shown at: resized as its
    /// program starts, Git for Windows' programs can lose the first key (TERMINAL.md, problem 22).
    /// So this one is made only once the terminal has its size, at that size, and not told the
    /// same size again afterwards.</summary>
    [Fact]
    public void AConnectionMadeAtTheTerminalsSize_WaitsForIt_AndIsNotToldItAgain()
    {
        (int Columns, int Rows)? madeAt = null;
        var program = new FakeConnection();
        var deferred = new DeferredConnection((size, _) =>
        {
            madeAt = size;
            return Task.FromResult<ITerminalConnection>(program);
        });
        using var controller = new TerminalController(deferred);

        _session.PumpFrames(3);
        Assert.Null(madeAt);   // no size yet, so no connection

        controller.Resize(100, 30);
        WaitFor(() => deferred.Connection is not null);
        Assert.Equal((100, 30), madeAt);
        Assert.Empty(program.Resizes);

        controller.Resize(90, 30);
        Assert.Equal([(90, 30)], program.Resizes);
    }

    /// <summary>The controller starts at 80 × 24, and a grid of that size was no change, so the
    /// connection never heard of it: a connection waiting for its size waited for good.</summary>
    [Fact]
    public void TheFirstGrid_ReachesTheConnection_EvenAtTheSizeTheControllerStartsAt()
    {
        (int Columns, int Rows)? madeAt = null;
        var deferred = new DeferredConnection((size, _) =>
        {
            madeAt = size;
            return Task.FromResult<ITerminalConnection>(new FakeConnection());
        });
        using var controller = new TerminalController(deferred);
        Assert.Equal((80, 24), (controller.Columns, controller.Rows));

        controller.Resize(80, 24);

        WaitFor(() => deferred.Connection is not null);
        Assert.Equal((80, 24), madeAt);
    }

    private static string ReadToEnd(Stream stream)
    {
        using var all = new MemoryStream();
        stream.CopyTo(all);
        return System.Text.Encoding.UTF8.GetString(all.ToArray());
    }

    [Fact]
    public void TheTraits_AreTheConnections_OnceItIsMade_AndTheControllerReadsThemThen()
    {
        var deferred = Deferred(connecting: null);
        using var controller = new TerminalController(deferred);
        controller.Resize(80, 24);
        Assert.Equal(TerminalConnectionTraits.None, deferred.Traits);

        var program = new FakeConnection(TerminalConnectionTraits.WantsWin32Input);
        _made.SetResult(program);
        WaitFor(() => deferred.Connection is not null);
        program.Emit("\x1b[?9001h"); // the program asks for win32-input-mode keys, as ConPTY does
        WaitFor(() => controller.EncodeKey(new TerminalKey(KeyCode.Escape, KeyModifiers.None)) is { } sent && sent.EndsWith('_'));

        Assert.Equal(TerminalConnectionTraits.WantsWin32Input, deferred.Traits);
    }
}
