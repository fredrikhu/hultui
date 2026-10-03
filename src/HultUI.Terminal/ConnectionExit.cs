namespace HultUI.Terminal;

/// <summary>
/// The <see cref="ITerminalConnection.Exited"/> event, done right for a connection to expose:
/// raised at most once, and a handler added after the program ended is called at once. A program
/// can end before anyone listens — <c>cmd /c echo hi</c> is gone in 60 ms, before the controller
/// it was started for exists — and a plain event would lose that exit for good.
/// </summary>
/// <example>
/// <code>
/// private readonly ConnectionExit _exit = new();
/// public event Action&lt;int?&gt;? Exited { add => _exit.Add(value); remove => _exit.Remove(value); }
/// // when the program ends:
/// _exit.Raise(code);
/// </code>
/// </example>
public sealed class ConnectionExit
{
    private readonly object _gate = new();
    private Action<int?>? _handlers;
    private bool _raised;
    private int? _code;

    /// <summary>Whether the program has ended.</summary>
    public bool HasExited
    {
        get { lock (_gate) return _raised; }
    }

    /// <summary>The exit code it ended with, when known.</summary>
    public int? ExitCode
    {
        get { lock (_gate) return _code; }
    }

    public void Add(Action<int?>? handler)
    {
        if (handler is null) return;
        int? code;
        lock (_gate)
        {
            if (!_raised)
            {
                _handlers += handler;
                return;
            }
            code = _code;
        }
        handler(code); // after the exit: at once, on the caller's thread
    }

    public void Remove(Action<int?>? handler)
    {
        lock (_gate) _handlers -= handler;
    }

    /// <summary>Records the end and calls the handlers. Returns false, doing nothing, when the
    /// end was already raised.</summary>
    public bool Raise(int? exitCode)
    {
        Action<int?>? handlers;
        lock (_gate)
        {
            if (_raised) return false;
            _raised = true;
            _code = exitCode;
            handlers = _handlers;
            _handlers = null;
        }
        handlers?.Invoke(exitCode);
        return true;
    }
}
