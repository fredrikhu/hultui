using System.Collections.Concurrent;
using System.Text;
using HultUI.Core.Reactivity;
using HultUI.Core.Threading;
using HultUI.Terminal.Emulation;

namespace HultUI.Terminal;

/// <summary>A terminal session's state.</summary>
public enum TerminalState
{
    /// <summary>The program is running, or has not said otherwise.</summary>
    Running,

    /// <summary>The program has ended; <see cref="TerminalController.ExitCode"/> says how, when
    /// the connection knows.</summary>
    Exited,
}

/// <summary>
/// One terminal session: the emulator, the connection its bytes travel over, and the two threads
/// between them. The properties are signal-backed, so reading one inside a component's
/// <c>Build</c> tracks it. Show it with <see cref="TerminalElement"/>.
/// </summary>
/// <remarks>
/// <para><b>Threads.</b> A reader thread of its own does blocking reads of the program's output
/// and feeds the emulator under one lock; it wakes the UI only after letting go of the lock, since
/// a frame waits for that lock and a thread a frame waits for must not post to it
/// (docs/THREADING.md). A writer thread of its own sends everything to the program — keys,
/// pastes, the emulator's replies — so neither the UI thread nor the reader ever blocks on the
/// program reading its input. The spike measured both rules: a paste written by the reader
/// deadlocked for good at 256 KiB, and one written on the UI thread froze it for 0.9 s.</para>
/// <para><b>Ending.</b> <see cref="Exited"/> is raised once, after the program's output has
/// ended, with the exit code when the connection knows one — or, when the program has exited and
/// something else holds its output open (a background job on a pseudoterminal), a second after
/// the exit. <see cref="Dispose"/> ends the program first, in the order that cannot hang: the
/// writer stops, the program is ended, its output is read to the end, and only then is the
/// connection closed. A controller disposed by its application reports no exit.</para>
/// <para><b>One element per controller.</b> The element sets the grid's size from its own;
/// two elements would fight over it.</para>
/// </remarks>
public sealed class TerminalController : IDisposable
{
    /// <summary>What one cell of scrollback costs, measured in the spike (24.7 bytes, rounded up).</summary>
    internal const int BytesPerCell = 25;

    private readonly ITerminalConnection _connection;
    private readonly ITerminalEmulator _emulator;
    private readonly object _gate = new();
    private readonly BlockingCollection<byte[]> _outbox = new();
    private readonly ConcurrentQueue<TerminalEvent> _uiEvents = new();
    private readonly ManualResetEventSlim _exitKnown = new();
    private readonly Thread _reader;
    private readonly Thread _writer;
    private int _wakePending;
    private long _bytesRead;
    private int _exitReported;
    private int? _exitCodeFromConnection;
    private volatile bool _disposed;

    private readonly Signal<string> _title = new("");
    private readonly Signal<TerminalState> _state = new(TerminalState.Running);
    private readonly Signal<int?> _exitCode = new(null);
    private readonly Signal<string?> _workingDirectory = new(null);
    private readonly Signal<int> _scrollback;
    private readonly Signal<long?> _maxScrollbackBytes;
    private readonly Signal<int> _columns = new(80);
    private readonly Signal<int> _rows = new(24);

    /// <param name="connection">What the bytes travel over. The controller owns it from here:
    /// it reads and writes it, and disposes it.</param>
    /// <param name="options">How the terminal starts; null takes the defaults.</param>
    public TerminalController(ITerminalConnection connection, TerminalOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        _connection = connection;
        Options = options ?? new TerminalOptions();
        _scrollback = new(Options.Scrollback);
        _maxScrollbackBytes = new(Options.MaxScrollbackBytes);

        _emulator = new XtermEmulator(_columns.Value, _rows.Value, EffectiveScrollback(_columns.Value),
            Options.Theme, Options.TermName);
        _emulator.Event += OnEmulatorEvent;
        _connection.Exited += OnConnectionExited;

        _writer = new Thread(WriteLoop) { IsBackground = true, Name = "terminal-write" };
        _reader = new Thread(ReadLoop) { IsBackground = true, Name = "terminal-read" };
        _writer.Start();
        _reader.Start();
    }

    public TerminalOptions Options { get; }

    /// <summary>The connection, for what only its kind offers.</summary>
    public ITerminalConnection Connection => _connection;

    // ──── State ────

    /// <summary>The window title the program set (OSC 0 or 2); empty until it sets one.</summary>
    public string Title => _title.Value;

    public TerminalState State => _state.Value;

    /// <summary>The program's exit code once it has ended, when the connection knows it.</summary>
    public int? ExitCode => _exitCode.Value;

    /// <summary>The working directory the shell reported (OSC 7); null until it does.</summary>
    public string? WorkingDirectory => _workingDirectory.Value;

    /// <summary>The grid's size, as the element last set it.</summary>
    public int Columns => _columns.Value;
    public int Rows => _rows.Value;

    /// <summary>
    /// Lines kept above the screen; starts at <see cref="TerminalOptions.Scrollback"/>. Takes effect
    /// at once: shrinking drops the oldest lines first, growing keeps everything. Applied to the
    /// normal screen — set while a full-screen program has the alternate screen, it applies when the
    /// program returns. 0 turns scrollback off; values above <see cref="TerminalOptions.MaxScrollback"/>
    /// are clamped; negative values throw. UI thread.
    /// </summary>
    public int Scrollback
    {
        get => _scrollback.Value;
        set
        {
            _scrollback.Value = TerminalOptions.ValidateScrollback(value);
            ApplyScrollback();
        }
    }

    /// <summary>
    /// A memory budget for the scrollback, in bytes, or null for none; starts at
    /// <see cref="TerminalOptions.MaxScrollbackBytes"/>. When set, the lines kept are the smaller of
    /// <see cref="Scrollback"/> and what the budget holds at the current width. UI thread.
    /// </summary>
    public long? MaxScrollbackBytes
    {
        get => _maxScrollbackBytes.Value;
        set
        {
            _maxScrollbackBytes.Value = TerminalOptions.ValidateBudget(value);
            ApplyScrollback();
        }
    }

    /// <summary>The lines actually kept at the current width: <see cref="Scrollback"/>, cut down to
    /// what <see cref="MaxScrollbackBytes"/> holds.</summary>
    public int EffectiveScrollbackLines => EffectiveScrollback(Columns);

    // ──── Events (UI thread) ────

    /// <summary>The program ended, with its exit code when the connection knows one. Raised once.</summary>
    public event Action<int?>? Exited;

    /// <summary>The program rang the bell.</summary>
    public event Action? Bell;

    /// <summary>
    /// The program asked to put text on the clipboard (OSC 52). Set
    /// <see cref="TerminalClipboardRequest.Cancel"/> to refuse; otherwise the element puts it on its
    /// window's clipboard. Reading the clipboard (OSC 52 with <c>?</c>) is never allowed.
    /// </summary>
    public event Action<TerminalClipboardRequest>? ClipboardWriteRequested;

    // ──── Element plumbing (UI thread) ────

    /// <summary>The screen changed. Coalesced: at most one per frame however much arrived.</summary>
    internal event Action? ContentChanged;

    /// <summary>The program switched between the normal and the alternate screen.</summary>
    internal event Action<bool>? ScreenSwitched;

    /// <summary>OSC 52 text the application let through.</summary>
    internal event Action<string>? ClipboardWrite;

    /// <summary>The program set the title.</summary>
    internal event Action<string>? TitleChanged;

    // ──── Sending ────

    /// <summary>Sends <paramref name="text"/> to the program as UTF-8, through the writer thread.
    /// Any thread; ignored once the controller is disposed.</summary>
    public void Send(string text)
    {
        if (!string.IsNullOrEmpty(text)) Enqueue(Encoding.UTF8.GetBytes(text));
    }

    /// <summary>Sends <paramref name="bytes"/> to the program, through the writer thread.
    /// Any thread; ignored once the controller is disposed.</summary>
    public void Send(ReadOnlySpan<byte> bytes)
    {
        if (!bytes.IsEmpty) Enqueue(bytes.ToArray());
    }

    /// <summary>
    /// Pastes <paramref name="text"/>: bracketed when the program asked (DECSET 2004), with ESC
    /// stripped so pasted text cannot end the bracket early, then sent like any other input.
    /// UI thread.
    /// </summary>
    public void Paste(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        lock (_gate)
        {
            if (_disposed) return;
            _emulator.Paste(text);
        }
        DrainUiEvents();
    }

    /// <summary>
    /// The visible screen as text: one line per row, with trailing blanks and the blank rows at
    /// the bottom dropped, and a wide character once. What a check that a program ran, or a
    /// screen reader, reads. Any thread; empty once disposed.
    /// </summary>
    public string ReadScreen()
    {
        var snapshot = new TerminalSnapshot();
        if (!CopySnapshot(snapshot)) return "";

        var sb = new StringBuilder();
        int lastLine = 0;
        for (int row = 0; row < snapshot.Rows; row++)
        {
            if (row > 0) sb.Append('\n');
            int lineStart = sb.Length;
            foreach (var cell in snapshot.Row(row))
            {
                if (cell.Width == 0) continue; // the second half of a wide character
                if (cell.Cluster is { } cluster) sb.Append(cluster);
                else if (cell.CodePoint == 0) sb.Append(' ');
                else sb.Append((Rune.IsValid(cell.CodePoint) ? new Rune(cell.CodePoint) : Rune.ReplacementChar).ToString());
            }
            int end = sb.Length;
            while (end > lineStart && sb[end - 1] == ' ') end--;
            sb.Length = end;
            if (end > lineStart) lastLine = sb.Length;
        }
        sb.Length = lastLine;
        return sb.ToString();
    }

    private void Enqueue(byte[] bytes)
    {
        if (_disposed) return;
        try { _outbox.Add(bytes); }
        catch (InvalidOperationException) { } // completed by Dispose: the program is going away
    }

    // ──── For the element (UI thread) ────

    /// <summary>The grid's size without tracking it: for drawing, not for a component's Build.</summary>
    internal (int Columns, int Rows) GridSize => (_columns.Peek(), _rows.Peek());

    internal bool IsDisposed => _disposed;

    /// <summary>Bytes of output fed to the emulator so far. Counted after the wake is asked for,
    /// so a test that sees its bytes here and then no <see cref="WakePending"/> knows the UI
    /// thread has seen them too.</summary>
    internal long BytesRead => Interlocked.Read(ref _bytesRead);

    internal bool WakePending => Volatile.Read(ref _wakePending) != 0;

    /// <summary>Copies the screen into <paramref name="snapshot"/>. Once disposed there is no
    /// screen to copy: returns false and leaves it as it was.</summary>
    internal bool CopySnapshot(TerminalSnapshot snapshot)
    {
        lock (_gate)
        {
            if (_disposed) return false;
            _emulator.CopySnapshot(snapshot);
            return true;
        }
    }

    /// <summary>Resizes the emulator and tells the connection, together, always: ConPTY reflows
    /// its own copy and the two must agree.</summary>
    internal void Resize(int columns, int rows)
    {
        columns = Math.Max(1, columns);
        rows = Math.Max(1, rows);
        if (columns == _columns.Peek() && rows == _rows.Peek()) return;
        lock (_gate)
        {
            if (_disposed) return;
            _emulator.Resize(columns, rows);
            _emulator.SetScrollback(EffectiveScrollback(columns));
        }
        _connection.Resize(columns, rows);
        ReactiveRuntime.Batch(() =>
        {
            _columns.Value = columns;
            _rows.Value = rows;
        });
        DrainUiEvents();
        ContentChanged?.Invoke();
    }

    // Each of these is answered under the lock, and answers nothing once disposed: the element can
    // outlive its controller by a frame.

    /// <summary>The cell in device pixels, for the window-size reports.</summary>
    internal void SetCellPixels(double width, double height)
    {
        lock (_gate)
        {
            if (!_disposed) _emulator.SetCellPixels(width, height);
        }
    }

    internal string? EncodeKey(in TerminalKey key)
    {
        lock (_gate) return _disposed ? null : _emulator.EncodeKey(key);
    }

    internal string? EncodeMouse(in TerminalMouse mouse)
    {
        lock (_gate) return _disposed ? null : _emulator.EncodeMouse(mouse);
    }

    internal string? EncodeFocus(bool focused)
    {
        lock (_gate) return _disposed ? null : _emulator.EncodeFocus(focused);
    }

    internal void ScrollViewport(int lines)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _emulator.ScrollViewport(lines);
        }
        ContentChanged?.Invoke();
    }

    internal void ScrollToBottom()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _emulator.ScrollToBottom();
        }
        ContentChanged?.Invoke();
    }

    internal string GetText(TerminalPosition start, TerminalPosition end)
    {
        lock (_gate) return _disposed ? "" : _emulator.GetText(start, end);
    }

    internal (TerminalPosition Start, TerminalPosition End) WordAt(TerminalPosition at)
    {
        lock (_gate) return _disposed ? (at, at with { Column = at.Column + 1 }) : _emulator.WordAt(at);
    }

    internal (TerminalPosition Start, TerminalPosition End) LineAt(TerminalPosition at)
    {
        lock (_gate) return _disposed ? (at, at with { Column = at.Column + 1 }) : _emulator.LineAt(at);
    }

    // ──── Scrollback ────

    private int EffectiveScrollback(int columns)
    {
        int lines = _scrollback.Value;
        if (_maxScrollbackBytes.Value is { } budget)
            lines = (int)Math.Min(lines, budget / ((long)Math.Max(1, columns) * BytesPerCell));
        return lines;
    }

    private void ApplyScrollback()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _emulator.SetScrollback(EffectiveScrollback(_columns.Peek()));
        }
        ContentChanged?.Invoke();
    }

    // ──── The reader ────

    private void ReadLoop()
    {
        var buffer = new byte[256 * 1024];
        try
        {
            while (true)
            {
                int n = _connection.Output.Read(buffer, 0, buffer.Length);
                if (n <= 0) break;
                lock (_gate)
                {
                    if (_disposed) continue; // drain to the end so a pseudoconsole can close
                    _emulator.Feed(buffer.AsSpan(0, n));
                }
                // After the lock, never under it: the frame this wakes takes the same lock.
                Wake();
                Interlocked.Add(ref _bytesRead, n);
            }
        }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
        OnOutputEnded();
    }

    /// <summary>How long the output may stay open after the program has exited before the exit is
    /// reported anyway: a background process it started can hold a pseudoterminal open.</summary>
    internal static readonly TimeSpan OpenOutputGrace = TimeSpan.FromSeconds(1);

    private void OnConnectionExited(int? code)
    {
        _exitCodeFromConnection = code;
        _exitKnown.Set();
        // Normally the output ends right after and the reader reports the exit. When something
        // else still holds the output open (`sleep 60 &` in a shell that then exits), report it
        // anyway, and leave the input open to whatever still reads it.
        _ = Task.Delay(OpenOutputGrace).ContinueWith(static (_, state) =>
        {
            var self = (TerminalController)state!;
            if (!self._disposed) self.ReportExit(self._exitCodeFromConnection);
        }, this, TaskScheduler.Default);
    }

    /// <summary>
    /// The output ended. The exit code can arrive a little after the end of output (a child is
    /// reaped after its pipe closes), so wait briefly for it rather than report a wrong one — but
    /// never so long that a connection with no exit code holds the session open.
    /// </summary>
    private void OnOutputEnded()
    {
        if (!_disposed) _exitKnown.Wait(TimeSpan.FromMilliseconds(500));
        _outbox.CompleteAdding(); // nothing reads the input any more
        ReportExit(_exitKnown.IsSet ? _exitCodeFromConnection : null);
    }

    /// <summary>Reports the end of the session once, on the UI thread, unless the controller
    /// was disposed first: an application that closed the session has seen its end.</summary>
    private void ReportExit(int? code)
    {
        if (Interlocked.Exchange(ref _exitReported, 1) != 0) return;
        Post(() =>
        {
            if (_disposed) return;
            ReactiveRuntime.Batch(() =>
            {
                _exitCode.Value = code;
                _state.Value = TerminalState.Exited;
            });
            Exited?.Invoke(code);
        });
    }

    // ──── The writer ────

    private void WriteLoop()
    {
        try
        {
            foreach (var bytes in _outbox.GetConsumingEnumerable())
            {
                _connection.Input.Write(bytes, 0, bytes.Length);
                _connection.Input.Flush();
            }
        }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }

    // ──── Emulator → UI thread ────

    /// <summary>Raised inside <c>Feed</c>, <c>Paste</c> or <c>Resize</c>, under the lock. Replies
    /// go straight to the writer; the rest wait for the UI thread.</summary>
    private void OnEmulatorEvent(TerminalEvent e)
    {
        if (e is TerminalEvent.Reply reply) Send(reply.Text);
        else _uiEvents.Enqueue(e);
    }

    /// <summary>
    /// Asks for one visit from the UI thread, however many reads arrive before it. Through the
    /// thread pool: the reader is a thread a live-resize frame waits for (on the lock), and such a
    /// thread must not post itself (docs/THREADING.md).
    /// </summary>
    private void Wake()
    {
        if (Interlocked.Exchange(ref _wakePending, 1) == 0)
            ThreadPool.UnsafeQueueUserWorkItem(static c => c.Post(c.OnWake), this, preferLocal: false);
    }

    private void OnWake()
    {
        Volatile.Write(ref _wakePending, 0);
        DrainUiEvents();
        ContentChanged?.Invoke();
    }

    /// <summary>Runs on the UI thread: now when this is it, otherwise on the next frame. Never
    /// called with the lock held — see <see cref="Wake"/>.</summary>
    private void Post(Action action)
    {
        if (UIThread.IsCurrent) action();
        else Application.InvokeOnMainThread(action);
    }

    private void DrainUiEvents()
    {
        while (_uiEvents.TryDequeue(out var e))
        {
            if (_disposed) continue;
            switch (e)
            {
                case TerminalEvent.Title t:
                    _title.Value = t.Text;
                    TitleChanged?.Invoke(t.Text);
                    break;
                case TerminalEvent.Directory d:
                    _workingDirectory.Value = d.Path;
                    break;
                case TerminalEvent.Bell:
                    Bell?.Invoke();
                    break;
                case TerminalEvent.ClipboardWrite c:
                    var request = new TerminalClipboardRequest(c.Text);
                    ClipboardWriteRequested?.Invoke(request);
                    if (!request.Cancel) ClipboardWrite?.Invoke(c.Text);
                    break;
                case TerminalEvent.ScreenSwitched s:
                    ScreenSwitched?.Invoke(s.Alternate);
                    break;
            }
        }
    }

    // ──── Teardown ────

    /// <summary>
    /// Ends the program and the session, in the order that cannot hang: stop writing, end the
    /// program, read its output to the end, then close the connection. The waits are bounded, so a
    /// connection that never ends its output still lets the UI go.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _outbox.CompleteAdding();
        _writer.Join(TimeSpan.FromMilliseconds(500));

        try { _connection.Terminate(); }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException) { }

        _reader.Join(TimeSpan.FromSeconds(2));
        _connection.Exited -= OnConnectionExited;
        _connection.Dispose();

        // _exitKnown is not disposed: a reader that outlived the join above may still look at it,
        // and an exception on that thread would end the process.
        lock (_gate) _emulator.Dispose();
    }
}

/// <summary>A program's request to put text on the clipboard (OSC 52).</summary>
public sealed class TerminalClipboardRequest(string text)
{
    /// <summary>The text the program asked to copy.</summary>
    public string Text { get; } = text;

    /// <summary>Set to refuse the request.</summary>
    public bool Cancel { get; set; }
}
