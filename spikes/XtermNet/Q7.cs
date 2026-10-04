using System.Diagnostics;
using Porta.Pty;
using XTerm;
using XTerm.Input;
using static XtermNet.Probe;

namespace XtermNet;

/// <summary>
/// Question 7: real programs through a real pseudoconsole. A shell (pwsh on Windows, bash
/// elsewhere: start, echo latency per keystroke, a resize the shell sees, exit codes), full-screen
/// editors (vim; on Windows also Microsoft Edit, a native TUI with no Cygwin layer: alternate
/// screen, a resize it redraws for, a file written), and a flood (what arrives, how fast, and how
/// long each <c>Write</c> holds the lock the UI thread waits on).
/// Variants: <c>shell</c>, <c>vim</c>, <c>edit</c>, <c>flood [MiB]</c>, <c>exit</c>; none runs them all.
/// </summary>
internal static class Q7
{
    private const string E = "\x1b";

    public static int Run(string[] args)
    {
        var only = args.FirstOrDefault();
        Log($"OS {Environment.OSVersion}, ConPTY implementation: {PtyProvider.PseudoConsoleImplementation}");
        if (only is null or "shell") Shell();
        if (only is null or "vim") { Vim(win32Esc: false); if (OperatingSystem.IsWindows()) Vim(win32Esc: true); }
        if ((only is null or "edit") && OperatingSystem.IsWindows()) MsEdit();
        if (only is null or "flood") Flood(args.Length > 1 ? int.Parse(args[1]) : 64);
        if (only is null or "exit") Exit();
        return 0;
    }

    /// <summary>The shell question's program and the commands it understands: pwsh on Windows, bash elsewhere.</summary>
    private sealed record ShellKind(string Name, string App, string[] Args, string Cwd, string Prompt, string EchoFormat,
        string SizeCommand, Func<string, bool> SaysEightyByTwentyFour);

    private static ShellKind PickShell() => OperatingSystem.IsWindows()
        ? new("pwsh", Which("pwsh", @"C:\Program Files\PowerShell\7\pwsh.exe") ?? Which("powershell")!, ["-NoLogo", "-NoProfile"], @"C:\",
            "PS ", "Write-Output '{0}'", "$Host.UI.RawUI.WindowSize.Width; $Host.UI.RawUI.WindowSize.Height\r",
            text => text.Contains("\n80") && text.Contains("\n24"))
        : new("bash", Which("bash", "/bin/bash")!, ["--norc", "--noprofile", "-i"], "/",
            "$ ", "echo '{0}'", "stty size\r", text => text.Contains("\n24 80"));

    private static void Shell()
    {
        var sh = PickShell();
        Log("");
        Log($"── {sh.Name} ──");
        // From the root so the prompt is short and the probe line does not wrap.
        using var s = Session.Start(sh.App, sh.Args, 120, 30, cwd: sh.Cwd);
        var prompt = s.WaitFor(t => Session.VisibleText(t).Contains(sh.Prompt), 15_000);
        Log($"spawn -> first byte {s.FirstByteMs:0} ms, -> prompt {s.Clock.Elapsed.TotalMilliseconds:0} ms ({(prompt < 0 ? "TIMED OUT" : "ok")})");
        s.WaitFor(_ => false, 500);   // let PSReadLine settle

        // Echo latency: one character at a time; the time until it shows left of the cursor.
        // (Left of the cursor because PSReadLine draws grey history predictions to its right.)
        var command = string.Format(sh.EchoFormat, "latency probe");
        var lat = new List<double>();
        for (var i = 0; i < command.Length; i++)
        {
            var typed = command[..(i + 1)];
            var sw = Stopwatch.StartNew();
            s.Send(command[i].ToString());
            var ms = s.WaitFor(t => LeftOfCursor(t).EndsWith(typed), 3000);
            if (ms < 0) { Log($"  char {i} '{command[i]}' never echoed; screen:\n{s.Screen()}"); break; }
            lat.Add(sw.Elapsed.TotalMilliseconds);
        }
        Log($"keystroke -> echo in grid: {Stats(lat)}");
        s.Send("\r");
        var ran = s.WaitFor(t => Session.VisibleText(t).Contains("\nlatency probe"), 5000);
        Log($"Enter -> output {ran:0} ms");

        // Question 3's cut: XTerm.NET does not reflow the cursor's own line. Type a command longer
        // than the new width, narrow, and see whether the pseudoconsole's repaint puts it back.
        var longCmd = string.Format(sh.EchoFormat, string.Concat(Enumerable.Range(0, 9).Select(i => $"segment{i}-")));
        s.Send(longCmd);
        s.WaitFor(t => Session.VisibleText(t).Replace("\r", "").Replace("\n", "").Contains("segment8-"), 3000);
        s.WaitFor(_ => false, 200);
        // The engine first, alone, with the reader held off by the lock; then the pseudoconsole.
        string engineOnly;
        lock (s.Gate)
        {
            s.Term.Resize(60, 30);
            engineOnly = Joined(s.Term).Contains(longCmd) ? "whole" : $"cut to '{Joined(s.Term)[^40..]}'";
            Log($"{longCmd.Length + 8}-char prompt line at 120 cols; after Term.Resize(60) alone: {engineOnly}");
            Log(Probe.Screen(s.Term));
        }
        var swp = Stopwatch.StartNew();
        s.Pty.Resize(60, 30);
        var repainted = s.WaitFor(t => Joined(t).Contains(longCmd), 2000);
        Log($"after Pty.Resize(60): whole command on screen {(repainted < 0 ? "NEVER (cut stays)" : $"{swp.Elapsed.TotalMilliseconds:0} ms after the call")}");
        // The next keystrokes land where the PSEUDOCONSOLE's cursor is; they read as the command's
        // continuation only if the engine put its cursor in the same place.
        s.Send("XYZ");
        var continued = s.WaitFor(t => Joined(t).Contains(longCmd + "XYZ"), 2000);
        Log($"typing after the narrowing continues the command: {(continued < 0 ? "NO" : "yes")}");
        Log(s.Screen());
        s.Send("\x03");   // Ctrl+C: abandon the line
        s.WaitFor(_ => false, 300);

        // Resize, then ask the shell what it sees.
        var swr = Stopwatch.StartNew();
        s.Resize(80, 24);
        var resizeMs = swr.Elapsed.TotalMilliseconds;
        s.WaitFor(_ => false, 300);
        s.Send(sh.SizeCommand);
        var seen = s.WaitFor(t => sh.SaysEightyByTwentyFour(Session.VisibleText(t)), 5000);
        Log($"Resize(80,24) call {resizeMs:0.0} ms; shell reports 80x24: {(seen >= 0 ? "yes" : "NO")}");
        Log(s.Screen());
        Log($"reads: {s.ChunkSizes.Count}, {s.BytesRead} bytes; Write under lock {Stats(s.WriteMs)}");
    }

    /// <param name="win32Esc">Send Escape the way ConPTY asked for it with <c>CSI ? 9001 h</c>
    /// (win32-input-mode: <c>CSI Vk;Sc;Uc;Kd;Cs;Rc _</c>, down then up) instead of a bare ESC byte,
    /// which the pseudoconsole cannot tell from the start of an Alt+key sequence.</param>
    private static void Vim(bool win32Esc)
    {
        Log("");
        Log($"── vim{(OperatingSystem.IsWindows() ? " (Git for Windows)" : "")}, Escape sent as {(win32Esc ? "win32-input-mode key events" : "a bare ESC byte")} ──");
        var vim = Which("vim", @"C:\Program Files\Git\usr\bin\vim.exe", "/usr/bin/vim");
        if (vim is null) { Log("no vim found; skipped"); return; }
        var file = Path.Combine(Path.GetTempPath(), $"xtermnet-q7-{Environment.ProcessId}.txt");
        File.Delete(file);
        // -n: no swap file, or a run that is killed leaves one that stops the next at a prompt.
        using var s = Session.Start(vim, ["-u", "NONE", "-i", "NONE", "-N", "-n", file], 100, 30);
        var ready = s.WaitFor(t => t.GetLine(t.Buffer.YDisp + 1).TrimEnd() == "~" && t.GetLine(t.Buffer.YDisp + 28).TrimEnd() == "~", 15_000);
        bool alt; lock (s.Gate) alt = s.Term.IsAlternateBufferActive;
        Log($"vim up in {s.Clock.Elapsed.TotalMilliseconds:0} ms ({(ready < 0 ? "TIMED OUT" : "ok")}); alternate buffer: {alt}");
        Log(s.Screen());

        s.Send("ihello from vim");
        s.WaitFor(t => t.GetLine(t.Buffer.YDisp).StartsWith("hello from vim"), 3000);
        bool win32Mode; lock (s.Gate) win32Mode = s.Term.Win32InputMode;
        s.Send(win32Esc
            ? Win32InputGenerator.GenerateKeyPress(0x1B, 0x01, '\x1b', Win32InputGenerator.ControlKeyState.None)
            : E);
        var normal = s.WaitFor(t => !Session.VisibleText(t).Contains("-- INSERT --"), 2000);
        Log($"terminal's Win32InputMode (ConPTY sent ?9001h): {win32Mode}; Escape -> insert mode left {(normal < 0 ? "NO" : $"after {normal:0} ms")}");

        // A command before the resize, as a control for the one after it.
        s.Send(":echo 'before'\r");
        var before = s.WaitFor(t => t.GetLine(t.Buffer.YDisp + 29).StartsWith("before "), 3000);
        Log($":echo before the resize answered: {(before >= 0 ? "yes" : "NO")}");

        s.Resize(80, 20);
        var redrawnRows = s.WaitFor(t => t.GetLine(t.Buffer.YDisp + 19).TrimEnd() != "~", 3000);
        s.WaitFor(_ => false, 300);
        s.Send(":echo &columns &lines\r");
        var redrawn = s.WaitFor(t => t.GetLine(t.Buffer.YDisp + 19).StartsWith("80 20 "), 3000);
        Log($"after Resize(80,20): last row repainted {(redrawnRows < 0 ? "NO" : $"after {redrawnRows:0} ms")}; :echo &columns &lines answered '80 20': {(redrawn >= 0 ? "yes" : "NO")}");
        if (redrawn < 0)
        {
            // Is it the first keystroke after a resize that goes missing? Try again.
            s.Send(win32Esc ? Win32InputGenerator.GenerateKeyPress(0x1B, 0x01, '\x1b', Win32InputGenerator.ControlKeyState.None) : E);
            s.WaitFor(_ => false, 300);
            s.Send(":echo &columns &lines\r");
            var again = s.WaitFor(t => t.GetLine(t.Buffer.YDisp + 19).StartsWith("80 20 "), 3000);
            Log($"  second attempt: {(again >= 0 ? "answered" : "NO")}");
        }
        Log(s.Screen());

        s.Send(":wq\r");
        var exited = s.WaitFor(_ => s.ExitCode is not null, 5000);
        lock (s.Gate) alt = s.Term.IsAlternateBufferActive;
        var content = File.Exists(file) ? File.ReadAllText(file).TrimEnd() : "(no file)";
        Log($":wq -> exit code {s.ExitCode} ({(exited < 0 ? "TIMED OUT" : $"{exited:0} ms")}); file says '{content}'; alternate buffer after: {alt}");
        Log($"reads: {s.ChunkSizes.Count}, {s.BytesRead} bytes; Write under lock {Stats(s.WriteMs)}");
        File.Delete(file);
    }

    /// <summary>Microsoft Edit: a native Windows TUI, so ConPTY without Cygwin's console layer in between.</summary>
    private static void MsEdit()
    {
        Log("");
        Log("── Microsoft Edit (native) ──");
        var edit = Which("edit");
        if (edit is null) { Log("no edit.exe; skipped"); return; }
        var file = Path.Combine(Path.GetTempPath(), $"xtermnet-q7-edit-{Environment.ProcessId}.txt");
        File.WriteAllText(file, "");
        using var s = Session.Start(edit, [file], 100, 30);
        var up = s.WaitFor(t => Session.VisibleText(t).Contains("File"), 10_000);
        bool alt; lock (s.Gate) alt = s.Term.IsAlternateBufferActive;
        Log($"edit up {(up < 0 ? "TIMED OUT" : $"in {s.Clock.Elapsed.TotalMilliseconds:0} ms")}; alternate buffer: {alt}");
        Log(s.Screen());

        s.Send("hello from edit");
        var typed = s.WaitFor(t => Session.VisibleText(t).Contains("hello from edit"), 3000);
        string lastRow; lock (s.Gate) lastRow = s.Term.GetLine(s.Term.Buffer.YDisp + 29).TrimEnd();
        Log($"typed text on screen: {(typed < 0 ? "NO" : "yes")}; status row 29: '{lastRow}'");

        // The status bar's first word, looked for on the new last row.
        var marker = lastRow.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "\0";
        s.Resize(80, 20);
        var moved = s.WaitFor(t => t.GetLine(t.Buffer.YDisp + 19).Contains(marker), 3000);
        Log($"after Resize(80,20) the status row ('{marker}' ...) is drawn on row 19: {(moved < 0 ? "NO" : $"after {moved:0} ms")}");
        s.Send(" after resize");
        var typed2 = s.WaitFor(t => Session.VisibleText(t).Contains("hello from edit after resize"), 3000);
        Log($"first keystrokes after the resize arrive: {(typed2 < 0 ? "NO" : "yes")}");
        Log(s.Screen());

        s.Send("\x13");   // Ctrl+S
        s.WaitFor(_ => false, 500);
        s.Send("\x11");   // Ctrl+Q
        var exited = s.WaitFor(_ => s.ExitCode is not null, 5000);
        lock (s.Gate) alt = s.Term.IsAlternateBufferActive;
        Log($"Ctrl+S, Ctrl+Q -> exit code {s.ExitCode} ({(exited < 0 ? "TIMED OUT" : $"{exited:0} ms")}); file says '{File.ReadAllText(file).TrimEnd()}'; alternate buffer after: {alt}");
        if (exited < 0) Log(s.Screen());
        File.Delete(file);
    }

    private static void Flood(int mib)
    {
        Log("");
        Log($"── flood: {mib} MiB of 80-column lines from a child ──");
        var self = Environment.ProcessPath!;

        // The child alone, into an anonymous pipe: what it can produce with nothing in the way.
        var psi = new ProcessStartInfo(self, ["flood", mib.ToString()]) { RedirectStandardOutput = true, UseShellExecute = false };
        var sw = Stopwatch.StartNew();
        using (var p = Process.Start(psi)!)
        {
            var buf = new byte[1 << 20];
            long n = 0; int r;
            var stream = p.StandardOutput.BaseStream;
            while ((r = stream.Read(buf)) > 0) n += r;
            p.WaitForExit();
            Log($"plain pipe, no terminal: {n} bytes in {sw.ElapsedMilliseconds} ms = {n / sw.Elapsed.TotalSeconds / (1 << 20):0} MiB/s");
        }

        // The same child under the pseudoconsole, into the terminal.
        using var s = Session.Start(self, ["flood", mib.ToString()], 120, 40, scrollback: 10_000);
        var done = s.WaitFor(t => Session.VisibleText(t).Contains("FLOOD-DONE"), 300_000);
        var exited = s.WaitFor(_ => s.ExitCode is not null, 10_000);
        var elapsed = s.Clock.Elapsed.TotalSeconds;
        double parseMs; lock (s.Gate) parseMs = s.WriteMs.Sum();
        var produced = (long)mib << 20;
        Log($"through ConPTY: child produced {produced} bytes; terminal received {s.BytesRead} bytes ({100.0 * s.BytesRead / produced:0.0} %) in {s.ChunkSizes.Count} reads");
        Log($"  wall {elapsed * 1000:0} ms ({(done < 0 ? "never saw FLOOD-DONE" : "saw FLOOD-DONE")}) = {produced / elapsed / (1 << 20):0.0} MiB/s of child output");
        Log($"  time inside Write (lock held): {parseMs:0} ms total = {100 * parseMs / (elapsed * 1000):0.0} % of wall");
        lock (s.Gate)
        {
            Log($"  read sizes: {Stats(s.ChunkSizes.Select(x => (double)x).ToList(), "B")}");
            Log($"  Write under lock per read: {Stats(s.WriteMs)}");
            Log($"  lines in buffer {s.Term.Buffer.Lines.Length}, last line '{s.Term.GetLine(s.Term.Buffer.YBase + s.Term.Buffer.Y).TrimEnd()}'");
        }
    }

    private static void Exit()
    {
        Log("");
        Log("── exit codes ──");
        var (cmd, flag) = OperatingSystem.IsWindows() ? (Which("cmd")!, "/c") : (Which("sh", "/bin/sh")!, "-c");
        foreach (var porta in OperatingSystem.IsWindows() ? new[] { true, false } : [false])
            foreach (var code in new[] { 0, 3, 255 })
            {
                using var s = Session.Start(cmd, [flag, $"exit {code}"], 80, 24, portaQuoting: porta);
                var ms = s.WaitFor(_ => s.ExitCode is not null, 10_000);
                var how = OperatingSystem.IsWindows() ? (porta ? " (Porta.Pty quoting)" : " (MSVCRT quoting, verbatim)") : "";
                Log($"{Path.GetFileNameWithoutExtension(cmd)} {flag} exit {code}{how}: ProcessExited code {s.ExitCode} after {ms:0} ms; Pty.ExitCode {s.Pty.ExitCode}");
            }
        {
            var sh = PickShell();
            using var s = Session.Start(sh.App, sh.Args, 80, 24);
            s.WaitFor(t => Session.VisibleText(t).Contains(sh.Prompt), 15_000);
            s.Pty.Kill();
            var ms = s.WaitFor(_ => s.ExitCode is not null, 10_000);
            Log($"Kill() on an idle {sh.Name}: ProcessExited code {s.ExitCode} after {ms:0} ms");
        }
    }

    /// <summary>The visible rows joined with no separators, so a wrapped command reads whole.</summary>
    private static string Joined(Terminal t)
    {
        var sb = new System.Text.StringBuilder();
        for (var r = 0; r < t.Rows; r++) sb.Append(t.GetLine(t.Buffer.YDisp + r).TrimEnd());
        return sb.ToString();
    }

    /// <summary>The text on the cursor's row, left of the cursor.</summary>
    private static string LeftOfCursor(Terminal t)
    {
        var b = t.Buffer;
        var line = b.Lines[b.YBase + b.Y];
        return line?.TranslateToString(false, 0, b.X) ?? "";
    }
}
