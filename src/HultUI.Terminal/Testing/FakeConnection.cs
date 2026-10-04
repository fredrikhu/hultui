using System.Text;

namespace HultUI.Terminal.Testing;

/// <summary>
/// A scripted connection for tests: <see cref="Emit(string)"/> is what the program writes,
/// <see cref="Received"/> is everything the terminal sent it, and <see cref="End"/> is the program
/// exiting. No process, no threads of its own; the controller's reader and writer run against it
/// exactly as against a real connection.
/// </summary>
public sealed class FakeConnection : ITerminalConnection
{
    private readonly TerminalPipe _output = new();
    private readonly RecordingStream _input;
    private readonly List<(int Columns, int Rows)> _resizes = [];
    private readonly ConnectionExit _exit = new();

    public FakeConnection(TerminalConnectionTraits traits = TerminalConnectionTraits.None)
    {
        Traits = traits;
        _input = new RecordingStream(this);
    }

    public Stream Output => _output;
    public Stream Input => _input;
    public TerminalConnectionTraits Traits { get; }

    public event Action<int?>? Exited
    {
        add => _exit.Add(value);
        remove => _exit.Remove(value);
    }

    /// <summary>Raised on the controller's writer thread after each write reaches the program.</summary>
    public event Action? InputArrived;

    /// <summary>The program writes <paramref name="text"/> (as UTF-8). Any thread.</summary>
    public void Emit(string text) => _output.Write(Encoding.UTF8.GetBytes(text));

    /// <summary>The program writes <paramref name="bytes"/>. Any thread.</summary>
    public void Emit(ReadOnlySpan<byte> bytes) => _output.Write(bytes);

    /// <summary>The program exits: its output ends and <see cref="Exited"/> is raised with
    /// <paramref name="exitCode"/>.</summary>
    public void End(int? exitCode = 0)
    {
        _output.Complete();
        _exit.Raise(exitCode);
    }

    /// <summary>The program exits with <paramref name="exitCode"/> while something else keeps its
    /// output open, as a background job it started can hold a pseudoterminal. <see cref="End"/>
    /// still ends the output.</summary>
    public void ExitKeepingOutputOpen(int? exitCode = 0)
    {
        _exit.Raise(exitCode);
    }

    /// <summary>Everything the terminal has sent the program, decoded as UTF-8.</summary>
    public string Received
    {
        get { lock (_input.Gate) return Encoding.UTF8.GetString(_input.Bytes.ToArray()); }
    }

    /// <summary>Returns <see cref="Received"/> and forgets it.</summary>
    public string TakeReceived()
    {
        lock (_input.Gate)
        {
            var text = Encoding.UTF8.GetString(_input.Bytes.ToArray());
            _input.Bytes.Clear();
            return text;
        }
    }

    /// <summary>Every size the terminal has told the program, oldest first.</summary>
    public IReadOnlyList<(int Columns, int Rows)> Resizes
    {
        get { lock (_resizes) return _resizes.ToArray(); }
    }

    public bool IsTerminated { get; private set; }
    public bool IsDisposed { get; private set; }

    public void Resize(int columns, int rows)
    {
        lock (_resizes) _resizes.Add((columns, rows));
    }

    public void Terminate()
    {
        IsTerminated = true;
        End(null);
    }

    public void Dispose()
    {
        IsDisposed = true;
        _output.Complete();
    }

    private sealed class RecordingStream(FakeConnection owner) : Stream
    {
        public readonly object Gate = new();
        public readonly List<byte> Bytes = [];

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            lock (Gate)
                foreach (var b in buffer) Bytes.Add(b);
            owner.InputArrived?.Invoke();
        }

        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override bool CanRead => false;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
