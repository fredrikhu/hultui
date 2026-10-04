using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Porta.Pty;
using XTerm;
using XTerm.Options;

namespace XtermNet;

/// <summary>
/// One terminal session wired the way HultUI.Terminal would wire it, so questions 7 and 8 measure
/// the real shape rather than a test harness:
/// <list type="bullet">
/// <item>a dedicated reader thread doing blocking reads (the Avalonia host measured 137 ms vs
/// 7.5 s time-to-first-output against a pooled ReadAsync, and lost output under load), feeding
/// <c>Write(bytes)</c> under one lock;</item>
/// <item>a dedicated writer thread draining a queue, so neither the UI thread nor the reader ever
/// blocks on the pty's input side (question 8 asks whether that matters);</item>
/// <item>everything else (the "UI thread") reading the terminal under the same lock.</item>
/// </list>
/// </summary>
internal sealed class Session : IDisposable
{
    public readonly Terminal Term;
    public readonly IPtyConnection Pty;
    public readonly object Gate = new();
    public readonly Stopwatch Clock = Stopwatch.StartNew();
    public readonly List<int> ChunkSizes = [];
    public readonly List<double> WriteMs = [];
    public long BytesRead;
    public double FirstByteMs = -1;
    public int? ExitCode;
    public double ExitedMs = -1;
    public Action<Session, ReadOnlyMemory<byte>>? OnChunk;

    private readonly BlockingCollection<byte[]> _out = new();
    private readonly Thread _reader, _writer;

    private Session(Terminal term, IPtyConnection pty, Action<Session, ReadOnlyMemory<byte>>? onChunk)
    {
        Term = term;
        Pty = pty;
        OnChunk = onChunk;   // before the reader starts, or the first chunks go unseen
        // Replies (DA, DSR, ...) are raised inside Write, on the reader thread, under the lock.
        // They go to the writer queue: writing them inline would block the reader on the
        // pty's input side.
        Term.DataReceived += (_, e) => Send(e.Data);
        Pty.ProcessExited += (_, e) =>
        {
            ExitCode = e.ExitCode;
            ExitedMs = Clock.Elapsed.TotalMilliseconds;
            lock (Gate) Monitor.PulseAll(Gate);
        };
        _reader = new Thread(ReadLoop) { IsBackground = true, Name = "pty-read" };
        _writer = new Thread(WriteLoop) { IsBackground = true, Name = "pty-write" };
        _reader.Start();
        _writer.Start();
    }

    /// <param name="portaQuoting">Porta.Pty's own Windows quoting, which wraps EVERY argument in quotes
    /// (and doubles embedded ones): <c>cmd.exe "/c" "exit 3"</c>, where cmd does not see a switch.
    /// Off by default; the command line is built here with the MSVCRT rules instead and passed verbatim.</param>
    public static Session Start(string app, string[] args, int cols, int rows, int scrollback = 5000, string? cwd = null,
        Action<Session, ReadOnlyMemory<byte>>? onChunk = null, bool portaQuoting = false)
    {
        var term = new Terminal(Probe.Options(cols, rows, scrollback));
        var verbatim = OperatingSystem.IsWindows() && !portaQuoting;
        var pty = PtyProvider.SpawnAsync(new PtyOptions
        {
            App = app,
            CommandLine = verbatim ? args.Select(QuoteArg).ToArray() : args,
            VerbatimCommandLine = verbatim,
            Cols = cols,
            Rows = rows,
            Cwd = cwd ?? Environment.CurrentDirectory,
            Name = "xterm-256color",
            Environment = new Dictionary<string, string> { ["TERM"] = "xterm-256color" },
        }, CancellationToken.None).GetAwaiter().GetResult();
        return new Session(term, pty, onChunk);
    }

    /// <summary>One argument for CommandLineToArgvW / the MSVCRT: bare when it can be, otherwise
    /// quoted, with backslashes doubled only where they precede a quote.</summary>
    public static string QuoteArg(string arg)
    {
        if (arg.Length > 0 && arg.IndexOfAny([' ', '\t', '"']) < 0) return arg;
        var sb = new StringBuilder("\"");
        var slashes = 0;
        foreach (var ch in arg)
        {
            if (ch == '\\') { slashes++; continue; }
            sb.Append('\\', ch == '"' ? slashes * 2 + 1 : slashes).Append(ch);
            slashes = 0;
        }
        return sb.Append('\\', slashes * 2).Append('"').ToString();
    }

    public void Send(string text) => _out.Add(Encoding.UTF8.GetBytes(text));
    public void Send(byte[] bytes) => _out.Add(bytes);

    public void Resize(int cols, int rows)
    {
        lock (Gate) Term.Resize(cols, rows);
        Pty.Resize(cols, rows);
    }

    /// <summary>Re-tests <paramref name="condition"/> under the lock each time the reader has written a
    /// chunk (it pulses the lock), so a wait ends within microseconds of the bytes landing rather than
    /// on the next tick of Windows' 15.6 ms sleep timer. The elapsed ms, or -1 on timeout.</summary>
    public double WaitFor(Func<Terminal, bool> condition, int timeoutMs = 10_000)
    {
        var sw = Stopwatch.StartNew();
        lock (Gate)
        {
            while (!condition(Term))
            {
                var left = timeoutMs - (int)sw.ElapsedMilliseconds;
                if (left <= 0) return -1;
                Monitor.Wait(Gate, Math.Min(left, 50));   // 50: conditions on state outside the terminal (ExitCode)
            }
        }
        return sw.Elapsed.TotalMilliseconds;
    }

    public string Screen() { lock (Gate) return Probe.Screen(Term); }

    public bool ScreenContains(string text) { lock (Gate) return VisibleText(Term).Contains(text); }

    public static string VisibleText(Terminal t)
    {
        var sb = new StringBuilder();
        for (var r = 0; r < t.Rows; r++) sb.AppendLine(t.GetLine(t.Buffer.YDisp + r));
        return sb.ToString();
    }

    private void ReadLoop()
    {
        var buf = new byte[0x40000];
        var sw = new Stopwatch();
        try
        {
            while (true)
            {
                var n = Pty.ReaderStream.Read(buf, 0, buf.Length);
                if (n <= 0) break;
                if (FirstByteMs < 0) FirstByteMs = Clock.Elapsed.TotalMilliseconds;
                BytesRead += n;
                sw.Restart();
                lock (Gate)
                {
                    Term.Write(buf.AsSpan(0, n));
                    ChunkSizes.Add(n);
                    WriteMs.Add(sw.Elapsed.TotalMilliseconds);
                    Monitor.PulseAll(Gate);
                }
                OnChunk?.Invoke(this, buf.AsMemory(0, n));
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException) { }
    }

    private void WriteLoop()
    {
        try
        {
            foreach (var bytes in _out.GetConsumingEnumerable())
            {
                Pty.WriterStream.Write(bytes, 0, bytes.Length);
                Pty.WriterStream.Flush();
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException) { }
    }

    public void Dispose()
    {
        _out.CompleteAdding();
        try { Pty.Kill(); } catch { }
        Pty.Dispose();
        Term.Dispose();
    }
}

/// <summary>The programs the ConPTY questions run under the pseudoconsole.</summary>
internal static class Children
{
    /// <summary>Prints READY, then echoes every line it reads with a prefix, so an echo is told apart
    /// from the console's own; "END" stops it.</summary>
    public static int Echo()
    {
        Console.Out.Write("READY\n");
        Console.Out.Flush();
        string? line;
        var n = 0;
        while ((line = Console.In.ReadLine()) is not null)
        {
            n++;
            if (line == "END") { Console.Out.Write($"GOT END after {n - 1} lines\n"); break; }
        }
        Console.Out.Flush();
        return 0;
    }

    /// <summary>Writes <paramref name="mib"/> MiB of 80-column text as fast as stdout takes it.</summary>
    public static int Flood(int mib)
    {
        var line = Encoding.ASCII.GetBytes("the quick brown fox jumps over the lazy dog 0123456789 ABCDEFGHIJKLMNOPQRSTU\r\n");
        using var stdout = Console.OpenStandardOutput();
        var block = new byte[line.Length * 800];
        for (var i = 0; i < 800; i++) line.CopyTo(block, i * line.Length);
        long total = (long)mib << 20, written = 0;
        var sw = Stopwatch.StartNew();
        while (written < total) { stdout.Write(block); written += block.Length; }
        stdout.Flush();
        var done = Encoding.ASCII.GetBytes($"FLOOD-DONE {written} bytes {sw.ElapsedMilliseconds} ms\r\n");
        stdout.Write(done);
        stdout.Flush();
        return 0;
    }
}
