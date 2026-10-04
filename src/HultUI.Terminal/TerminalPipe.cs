namespace HultUI.Terminal;

/// <summary>
/// An in-memory, one-way byte stream between two threads: one writes, the other reads, and a
/// read blocks until there is something to read or the writer has <see cref="Complete"/>d it.
/// What an in-process program and a <see cref="StreamConnection"/> need, and what tests use.
/// </summary>
/// <remarks>
/// Unbounded unless given a capacity. A terminal's two directions are both drained by threads
/// that exist for the purpose, so the terminal never needs one; a program that writes faster
/// than a terminal reads does, as a real pipe would make it wait. Without one, <c>seq 1e7</c>
/// in-process queues its whole output in memory.
/// </remarks>
public sealed class TerminalPipe : Stream
{
    private readonly object _gate = new();
    private readonly Queue<byte[]> _chunks = new();
    private readonly int _capacity;
    private long _buffered;
    private byte[]? _current;
    private int _offset;
    private bool _completed;

    /// <summary>An unbounded pipe: a write never waits.</summary>
    public TerminalPipe() { }

    /// <param name="capacity">Bytes held unread before a write waits for the reader; 0 for no
    /// limit. A write is taken whole once there is room, so the pipe can hold up to one write
    /// more than this.</param>
    public TerminalPipe(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        _capacity = capacity;
    }

    /// <summary>No more bytes will be written. A read that finds the pipe empty returns 0, and
    /// one already waiting wakes and returns 0; a write waiting for room wakes and is dropped.</summary>
    public void Complete()
    {
        lock (_gate)
        {
            _completed = true;
            Monitor.PulseAll(_gate);
        }
    }

    /// <summary>Whether <see cref="Complete"/> has been called.</summary>
    public bool IsCompleted
    {
        get { lock (_gate) return _completed; }
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (buffer.IsEmpty) return 0;
        lock (_gate)
        {
            while (_current is null)
            {
                if (_chunks.TryDequeue(out var next)) { _current = next; _offset = 0; break; }
                if (_completed) return 0;
                Monitor.Wait(_gate);
            }

            int n = Math.Min(buffer.Length, _current.Length - _offset);
            _current.AsSpan(_offset, n).CopyTo(buffer);
            _offset += n;
            if (_offset == _current.Length) _current = null;
            _buffered -= n;
            if (_capacity > 0) Monitor.PulseAll(_gate); // room for a waiting writer
            return n;
        }
    }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (buffer.IsEmpty) return;
        lock (_gate)
        {
            while (_capacity > 0 && _buffered >= _capacity && !_completed) Monitor.Wait(_gate);
            // Writes after completion are dropped, not thrown: the far end has gone, and a
            // terminal racing its own teardown should not die of it.
            if (_completed) return;
            _chunks.Enqueue(buffer.ToArray());
            _buffered += buffer.Length;
            Monitor.PulseAll(_gate);
        }
    }

    public override void Flush() { }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Complete();
        base.Dispose(disposing);
    }

    public override bool CanRead => true;
    public override bool CanWrite => true;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
