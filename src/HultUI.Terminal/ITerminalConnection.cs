namespace HultUI.Terminal;

/// <summary>
/// What a terminal's bytes travel over: a local process under a pseudoconsole
/// (<c>HultUI.Terminal.Pty</c>), an SSH channel, a serial port, a test script.
/// </summary>
/// <remarks>
/// <para><b>The controller owns the threads.</b> It reads <see cref="Output"/> on a thread of its
/// own, blocking, and writes <see cref="Input"/> on another; a connection starts neither. Nothing
/// else may read or write the two streams: a reader that also wrote could stop draining the
/// program while the program waits for it to, which deadlocks for good (the spike measured it
/// at a 256 KiB paste).</para>
/// <para><b>Ending.</b> <see cref="Terminate"/> ends the program; <see cref="Output"/> then
/// reaches its end, which is how the controller learns a program is gone whichever side ended
/// it. <see cref="Exited"/> carries the exit code, when the connection has one.</para>
/// </remarks>
public interface ITerminalConnection : IDisposable
{
    /// <summary>What the program writes. Read only by the controller's reader thread; a read
    /// returns 0 once the program is gone.</summary>
    Stream Output { get; }

    /// <summary>What the program reads. Written only by the controller's writer thread.</summary>
    Stream Input { get; }

    /// <summary>What the controller needs to know about the other end. Read when used, so a
    /// connection that learns them as it connects (<see cref="DeferredConnection"/>) may report
    /// them from then on.</summary>
    TerminalConnectionTraits Traits { get; }

    /// <summary>Tells the program the grid's new size. Called on the UI thread, always together
    /// with the emulator's own resize.</summary>
    void Resize(int columns, int rows);

    /// <summary>Ends the program, so <see cref="Output"/> reaches its end. Safe to call more than
    /// once, and after the program has already ended.</summary>
    void Terminate();

    /// <summary>The program ended, with its exit code when the connection knows one. Raised at
    /// most once, on any thread, and a handler added after the end is called at once: a quick
    /// program can end before the controller made for it listens. <see cref="ConnectionExit"/>
    /// does both.</summary>
    event Action<int?>? Exited;
}

/// <summary>What a <see cref="ITerminalConnection"/> tells the controller about its other end.</summary>
[Flags]
public enum TerminalConnectionTraits
{
    None = 0,

    /// <summary>
    /// The other end keeps its own copy of the screen and reflows it on a resize, repainting
    /// nothing — Windows' ConPTY. The emulator always reflows the cursor's own line
    /// (<c>ReflowCursorLine</c>), which is what such a connection needs; the trait is recorded so
    /// diagnostics can say which kind of end a session had.
    /// </summary>
    ReflowsItself = 1,

    /// <summary>
    /// The other end reads keys more reliably as win32-input-mode events (<c>CSI ? 9001 h</c>)
    /// than as VT bytes — ConPTY, where a bare ESC races with the key after it. While the
    /// program has that mode on, Escape, Alt with a key, Ctrl+Space and Ctrl+Backspace are sent
    /// that way; everything else stays VT, and typed text UTF-8.
    /// </summary>
    WantsWin32Input = 2,
}
