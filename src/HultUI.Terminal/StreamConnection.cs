namespace HultUI.Terminal;

/// <summary>
/// A connection over any two streams: what the program writes (<see cref="Output"/>) and what it
/// reads (<see cref="Input"/>). A serial port, an SSH shell channel, an in-process program over a
/// pair of <see cref="TerminalPipe"/>s.
/// </summary>
/// <remarks>
/// A stream knows nothing of window sizes or exit codes, so both are the owner's: pass
/// <c>resize</c> to hear the grid's size (an SSH channel sends a window-change request), and call
/// <see cref="ReportExit"/> when the program's end is known. Without it the
/// end of <see cref="Output"/> ends the session, with no exit code.
/// </remarks>
public sealed class StreamConnection : ITerminalConnection
{
    private readonly Action<int, int>? _resize;
    private readonly Action? _terminate;
    private int _exited;
    private int _disposed;

    /// <param name="output">What the program writes; the terminal reads it.</param>
    /// <param name="input">What the program reads; the terminal writes it.</param>
    /// <param name="resize">Told the grid's size whenever it changes. UI thread.</param>
    /// <param name="terminate">Ends the program. Without it, <see cref="Terminate"/> closes both
    /// streams, which ends a <see cref="TerminalPipe"/> and most real streams.</param>
    /// <param name="traits">What the other end is.</param>
    public StreamConnection(Stream output, Stream input, Action<int, int>? resize = null, Action? terminate = null,
        TerminalConnectionTraits traits = TerminalConnectionTraits.None)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(input);
        Output = output;
        Input = input;
        _resize = resize;
        _terminate = terminate;
        Traits = traits;
    }

    public Stream Output { get; }
    public Stream Input { get; }
    public TerminalConnectionTraits Traits { get; }

    public event Action<int?>? Exited;

    public void Resize(int columns, int rows) => _resize?.Invoke(columns, rows);

    public void Terminate()
    {
        if (_terminate is not null)
        {
            _terminate();
            return;
        }
        if (Output is TerminalPipe pipe) pipe.Complete();
        else Close(Output);
        Close(Input);
    }

    /// <summary>The program ended. Raises <see cref="Exited"/> once; later calls do nothing.</summary>
    public void ReportExit(int? exitCode)
    {
        if (Interlocked.Exchange(ref _exited, 1) == 0) Exited?.Invoke(exitCode);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Close(Output);
        Close(Input);
    }

    private static void Close(Stream stream)
    {
        try { stream.Dispose(); }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }
}
