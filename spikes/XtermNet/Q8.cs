using System.Diagnostics;
using System.Text;
using static XtermNet.Probe;

namespace XtermNet;

/// <summary>
/// Question 8: which thread may write to the pty. A large paste written by the thread that reads
/// the pty can deadlock: the child stops reading input while its output is not being drained, and
/// the writer is the drainer. A paste written by the UI thread blocks the UI for as long as the
/// child takes to accept it. Both measured with a child that echoes what it reads; the design
/// answer is the writer queue <see cref="Session"/> already has.
/// Usage: <c>q8 a|b|c [KiB]</c>, one case per process, because case a can leave the process
/// unable to tear its pseudoconsole down.
/// </summary>
internal static class Q8
{
    public static int Run(string[] args)
    {
        var which = args.FirstOrDefault() ?? "b";
        var kib = args.Length > 1 ? int.Parse(args[1]) : 1024;
        var paste = BuildPaste(kib);
        var self = Environment.ProcessPath!;
        Log($"paste: {paste.Length} bytes, {paste.Count(b => b == '\r')} lines ending in END");

        switch (which)
        {
            case "a": ReaderWrites(self, paste); break;
            case "b": QueueWrites(self, paste); break;
            case "c": UiWrites(self, paste); break;
        }
        return 0;
    }

    /// <summary>a) From the reader thread, inline, as a naive host would in a read callback.</summary>
    private static void ReaderWrites(string self, byte[] paste)
    {
        var finished = new ManualResetEventSlim();
        var fired = 0;
        double writeMs = -1;
        var s = Session.Start(self, ["echo"], 120, 30, onChunk: (sess, _) =>
        {
            if (!sess.ScreenContains("READY") || Interlocked.Exchange(ref fired, 1) == 1) return;
            var sw = Stopwatch.StartNew();
            try { sess.Pty.WriterStream.Write(paste); sess.Pty.WriterStream.Flush(); }
            catch (Exception ex) { Log($"  write threw {ex.GetType().Name}"); }
            writeMs = sw.Elapsed.TotalMilliseconds;
            finished.Set();
        });
        var ok = finished.Wait(15_000);
        var got = ok ? s.WaitFor(t => Session.VisibleText(t).Contains("GOT END"), 30_000) : -1;
        Log($"a) reader thread writes inline: {(ok ? $"write returned after {writeMs:0} ms" : "write STILL BLOCKED after 15 s: deadlock")};"
            + $" child saw END: {(got >= 0 ? "yes" : "no")}");
        if (ok) { s.Dispose(); return; }

        s.Pty.Kill();
        Log($"   Kill() the child: the write {(finished.Wait(5000) ? "returned" : "is STILL blocked 5 s later")}");
        var disposed = Task.Run(s.Dispose).Wait(10_000);
        Log($"   Dispose (ClosePseudoConsole) with the reader stuck in that write: {(disposed ? "returned" : "HUNG for 10 s")}");
        Console.Out.Flush();
        Environment.Exit(0);
    }

    /// <summary>b) Through the writer queue, the design Session uses.</summary>
    private static void QueueWrites(string self, byte[] paste)
    {
        using var s = Session.Start(self, ["echo"], 120, 30);
        s.WaitFor(t => Session.VisibleText(t).Contains("READY"), 15_000);
        var sw = Stopwatch.StartNew();
        s.Send(paste);
        var enqueue = sw.Elapsed.TotalMilliseconds;
        var got = s.WaitFor(t => Session.VisibleText(t).Contains("GOT END"), 120_000);
        Log($"b) writer queue: Send() returned in {enqueue:0.000} ms; child saw END {(got < 0 ? "NEVER (120 s)" : $"after {sw.Elapsed.TotalMilliseconds:0} ms")};"
            + $" reader drained {s.BytesRead} bytes meanwhile");
        lock (s.Gate) Log($"   {Session.VisibleText(s.Term).Split('\n').FirstOrDefault(l => l.Contains("GOT END"))?.Trim()}");
    }

    /// <summary>c) From the "UI thread", synchronously, with the reader running: how long the UI would freeze.</summary>
    private static void UiWrites(string self, byte[] paste)
    {
        using var s = Session.Start(self, ["echo"], 120, 30);
        s.WaitFor(t => Session.VisibleText(t).Contains("READY"), 15_000);
        var sw = Stopwatch.StartNew();
        s.Pty.WriterStream.Write(paste);
        s.Pty.WriterStream.Flush();
        var blocked = sw.Elapsed.TotalMilliseconds;
        var got = s.WaitFor(t => Session.VisibleText(t).Contains("GOT END"), 120_000);
        Log($"c) UI thread writes synchronously: blocked {blocked:0} ms; child saw END {(got < 0 ? "NEVER (120 s)" : $"after {sw.Elapsed.TotalMilliseconds:0} ms")}");
    }

    private static byte[] BuildPaste(int kib)
    {
        var sb = new StringBuilder();
        for (var i = 0; sb.Length < kib * 1024; i++)
            sb.Append($"line {i:00000} ").Append('x', 88).Append('\r');
        sb.Append("END\r");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }
}
