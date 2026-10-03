using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using HultUI.Terminal;

namespace HultUI.Samples.Terminal;

/// <summary>
/// A pretend shell that runs in this process, on threads of its own, over two
/// <see cref="TerminalPipe"/>s: it reads what the terminal sends and writes escape sequences back,
/// as a real program behind a pseudoconsole would. It shows what the terminal does without
/// needing one — colours, styles, box drawing, wide characters, the scrollback, a full-screen
/// program on the alternate screen, mouse reports, titles, the clipboard. Type <c>help</c>.
/// </summary>
/// <remarks>A real shell needs a pseudoconsole, which is the next package
/// (<c>HultUI.Terminal.Pty</c>, docs/design/TERMINAL.md step 4).</remarks>
public sealed class DemoShell
{
    private const string Esc = "\x1b";
    private const string Prompt = Esc + "[1;32mdemo" + Esc + "[0m:" + Esc + "[1;34m~" + Esc + "[0m$ ";

    // Bounded, as a real pipe is: seq waits for the terminal rather than queueing its output.
    private readonly TerminalPipe _toTerminal = new(capacity: 64 * 1024);
    private readonly TerminalPipe _fromTerminal = new();
    private readonly BlockingCollection<string> _keys = new();
    private readonly StringBuilder _line = new();
    private readonly List<string> _history = [];
    private int _historyAt;
    private volatile int _columns = 80, _rows = 24;
    private long _bytesWritten;

    public DemoShell()
    {
        Connection = new StreamConnection(_toTerminal, _fromTerminal, Resize, Terminate);
        new Thread(ReadKeys) { IsBackground = true, Name = "demo-shell-input" }.Start();
        new Thread(Run) { IsBackground = true, Name = "demo-shell" }.Start();
    }

    /// <summary>The connection to give a <see cref="TerminalController"/>.</summary>
    public StreamConnection Connection { get; }

    /// <summary>Bytes written to the terminal so far. With the pipe bounded, within 64 KiB of
    /// what the terminal has read.</summary>
    public long BytesWritten => Interlocked.Read(ref _bytesWritten);

    private void Resize(int columns, int rows)
    {
        _columns = columns;
        _rows = rows;
        _keys.Add(""); // wake a full-screen program to redraw
    }

    private void Terminate()
    {
        _keys.CompleteAdding();
        _fromTerminal.Complete();
    }

    // ──── Input ────

    /// <summary>Reads what the terminal sends: one write is one key, one paste or one report,
    /// which is how the controller's writer sends them.</summary>
    private void ReadKeys()
    {
        var buffer = new byte[64 * 1024];
        var decoder = Encoding.UTF8.GetDecoder();
        var chars = new char[buffer.Length];
        try
        {
            int n;
            while ((n = _fromTerminal.Read(buffer, 0, buffer.Length)) > 0)
            {
                int c = decoder.GetChars(buffer, 0, n, chars, 0);
                if (c > 0) _keys.Add(new string(chars, 0, c));
            }
        }
        catch (InvalidOperationException) { }
        _keys.CompleteAdding();
    }

    /// <summary>The next chunk of input, or null at the end. With a timeout, "" when it ran out.</summary>
    private string? NextInput(int timeoutMs = Timeout.Infinite)
    {
        try
        {
            if (_keys.TryTake(out var chunk, timeoutMs)) return chunk;
            return _keys.IsCompleted ? null : "";
        }
        catch (InvalidOperationException) { return null; }
    }

    private void Write(string text)
    {
        if (_toTerminal.IsCompleted) return;
        var bytes = Encoding.UTF8.GetBytes(text);
        _toTerminal.Write(bytes);
        Interlocked.Add(ref _bytesWritten, bytes.Length);
    }

    // ──── The shell ────

    private void Run()
    {
        int code = 0;
        try
        {
            Write(Esc + "]0;Demo shell" + "\a");
            Write("HultUI.Terminal demo shell. Type " + Bold("help") + " for what it can show.\r\n\r\n" + Prompt);
            code = Loop();
        }
        finally
        {
            Connection.ReportExit(code);
            _toTerminal.Complete();
        }
    }

    private int Loop()
    {
        while (NextInput() is { } chunk)
        {
            foreach (var token in Tokens(chunk))
            {
                if (token.StartsWith(Esc + "[200~", StringComparison.Ordinal))
                {
                    // A bracketed paste: its text goes into the line, its line breaks as spaces.
                    var text = token[6..^6].ReplaceLineEndings(" ");
                    _line.Append(text);
                    Write(text);
                    continue;
                }
                switch (token)
                {
                    case "\r":
                        Write("\r\n");
                        var command = _line.ToString().Trim();
                        _line.Clear();
                        if (command.Length > 0) { _history.Add(command); _historyAt = _history.Count; }
                        if (Execute(command) is { } exit) return exit;
                        Write(Prompt);
                        break;
                    case "\x7f" or "\b":
                        if (_line.Length > 0) { _line.Length--; Write("\b \b"); }
                        break;
                    case "\x03": // Ctrl+C
                        _line.Clear();
                        Write("^C\r\n" + Prompt);
                        break;
                    case "\x0c": // Ctrl+L
                        Write(Esc + "[H" + Esc + "[2J" + Prompt + _line);
                        break;
                    case "\x04" when _line.Length == 0: // Ctrl+D
                        Write("exit\r\n");
                        return 0;
                    case Esc + "[A" or Esc + "OA":
                        Recall(-1);
                        break;
                    case Esc + "[B" or Esc + "OB":
                        Recall(+1);
                        break;
                    default:
                        if (token.Length > 0 && token[0] >= ' ' && token[0] != '\x7f')
                        {
                            _line.Append(token);
                            Write(token);
                        }
                        break;
                }
            }
        }
        return 0;
    }

    private void Recall(int step)
    {
        if (_history.Count == 0) return;
        _historyAt = Math.Clamp(_historyAt + step, 0, _history.Count);
        var text = _historyAt < _history.Count ? _history[_historyAt] : "";
        Write("\r" + Esc + "[K" + Prompt + text);
        _line.Clear().Append(text);
    }

    /// <summary>Splits input into keys: escape sequences whole, printable runs whole.</summary>
    private static IEnumerable<string> Tokens(string s)
    {
        int i = 0;
        while (i < s.Length)
        {
            if (s[i] == '\x1b' && i + 1 < s.Length)
            {
                if (s.AsSpan(i).StartsWith(Esc + "[200~"))
                {
                    int end = s.IndexOf(Esc + "[201~", i, StringComparison.Ordinal);
                    int stop = end < 0 ? s.Length : end + 6;
                    yield return end < 0 ? s[i..] + Esc + "[201~" : s[i..stop];
                    i = stop;
                    continue;
                }
                int j = i + 2;
                if (s[i + 1] is '[' or 'O')
                {
                    while (j < s.Length && s[j] is >= '0' and <= '?' or ';' or '<') j++;
                    j = Math.Min(j + 1, s.Length);
                }
                yield return s[i..j];
                i = j;
            }
            else if (s[i] < ' ' || s[i] == '\x7f')
            {
                yield return s[i].ToString();
                i++;
            }
            else
            {
                int j = i;
                while (j < s.Length && s[j] >= ' ' && s[j] != '\x7f') j++;
                yield return s[i..j];
                i = j;
            }
        }
    }

    /// <summary>Runs a command; returns an exit code when the shell should end.</summary>
    private int? Execute(string command)
    {
        var parts = command.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;
        string arg = parts.Length > 1 ? parts[1] : "";
        switch (parts[0])
        {
            case "help": Help(); break;
            case "colors" or "colours": Colours(); break;
            case "styles": Styles(); break;
            case "boxes": Boxes(); break;
            case "unicode": Unicode(); break;
            case "seq": Seq(int.TryParse(arg, out var n) ? n : 1000); break;
            case "title": Write(Esc + "]2;" + arg + "\a"); break;
            case "cd": Write(Esc + "]7;file://localhost/" + arg.TrimStart('/') + "\a"); break;
            case "bell": Write("\a"); break;
            case "copy": Write(Esc + "]52;c;" + Convert.ToBase64String(Encoding.UTF8.GetBytes(arg)) + "\a"); break;
            case "size": Write($"{_columns} columns × {_rows} rows\r\n"); break;
            case "clear": Write(Esc + "[H" + Esc + "[2J" + Esc + "[3J"); break;
            case "top": Top(); break;
            case "mouse": Mouse(); break;
            case "flood": Flood(double.TryParse(arg, out var s) ? s : 5); break;
            case "exit": return int.TryParse(arg, out var code) ? code : 0;
            default: Write($"{parts[0]}: not a command here. Try {Bold("help")}.\r\n"); break;
        }
        return null;
    }

    private static string Bold(string s) => Esc + "[1m" + s + Esc + "[22m";

    private void Help()
    {
        (string, string)[] commands =
        [
            ("colors", "the 16 colours, the 256-colour palette and true colour"),
            ("styles", "bold, dim, italic, underlines, strikethrough, overline, inverse"),
            ("boxes", "box drawing and block elements, drawn as geometry"),
            ("unicode", "wide characters, emoji, combining marks"),
            ("seq [n]", "n numbered lines, for the scrollback (wheel, Shift+PageUp)"),
            ("top", "a full-screen program on the alternate screen (q quits)"),
            ("mouse", "reports the mouse as a program sees it (q quits)"),
            ("title <text>", "sets the window title (OSC 2)"),
            ("cd <path>", "reports a working directory (OSC 7)"),
            ("copy <text>", "puts text on the clipboard (OSC 52)"),
            ("bell", "rings the bell"),
            ("size", "the grid's size"),
            ("flood [seconds]", "redraws the whole screen as fast as it can"),
            ("clear", "clears the screen and the scrollback"),
            ("exit [code]", "ends the shell"),
        ];
        foreach (var (name, what) in commands)
            Write($"  {Esc}[36m{name,-16}{Esc}[0m {what}\r\n");
        Write($"\r\n  Select with the mouse (double-click a word, triple-click a line), copy with\r\n" +
              $"  Ctrl+Shift+C, paste with Ctrl+Shift+V. Shift selects even when a program has the mouse.\r\n");
    }

    private void Colours()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 16; i++)
        {
            sb.Append($"{Esc}[{(i < 8 ? 30 + i : 90 + i - 8)}m{i,3} ");
            if (i == 7) sb.Append(Esc + "[0m\r\n");
        }
        sb.Append(Esc + "[0m\r\n\r\n");
        for (int i = 0; i < 256; i++)
        {
            sb.Append($"{Esc}[48;5;{i}m  ");
            if (i == 15 || (i > 15 && (i - 15) % 36 == 0)) sb.Append(Esc + "[0m\r\n");
        }
        sb.Append(Esc + "[0m\r\n");
        int width = Math.Min(_columns, 120);
        for (int x = 0; x < width; x++)
        {
            double h = 360.0 * x / width;
            var (r, g, b) = Hue(h);
            sb.Append($"{Esc}[48;2;{r};{g};{b}m ");
        }
        sb.Append(Esc + "[0m\r\n");
        Write(sb.ToString());
    }

    private static (int R, int G, int B) Hue(double h)
    {
        double x = 1 - Math.Abs(h / 60 % 2 - 1);
        (double r, double g, double b) = h switch
        {
            < 60 => (1d, x, 0d), < 120 => (x, 1d, 0d), < 180 => (0d, 1d, x),
            < 240 => (0d, x, 1d), < 300 => (x, 0d, 1d), _ => (1d, 0d, x),
        };
        return ((int)(r * 255), (int)(g * 255), (int)(b * 255));
    }

    private void Styles()
    {
        Write($"  {Esc}[1mbold{Esc}[0m  {Esc}[2mdim{Esc}[0m  {Esc}[3mitalic{Esc}[0m  {Esc}[1;3mbold italic{Esc}[0m\r\n" +
              $"  {Esc}[4munderline{Esc}[0m  {Esc}[4:3mcurly{Esc}[0m  {Esc}[58;5;196m{Esc}[4mred underline{Esc}[0m  {Esc}[9mstrikethrough{Esc}[0m  {Esc}[53moverline{Esc}[0m\r\n" +
              $"  {Esc}[7minverse{Esc}[0m  {Esc}[8mhidden{Esc}[0m (hidden)  {Esc}[1;31mbold red{Esc}[0m  {Esc}[2;34mdim blue{Esc}[0m\r\n" +
              $"  {Esc}]8;;https://github.com/tomlm/XTerm.NET{Esc}\\a hyperlink{Esc}]8;;{Esc}\\\r\n");
    }

    private void Boxes()
    {
        Write("  ┌──────┬──────┐  ┏━━━━━━┳━━━━━━┓  ╔══════╦══════╗  ╭──────╮\r\n" +
              "  │ thin │ cell │  ┃ bold ┃ cell ┃  ║ dbl  ║ cell ║  │round │\r\n" +
              "  ├──────┼──────┤  ┣━━━━━━╋━━━━━━┫  ╠══════╬══════╣  ╰──────╯\r\n" +
              "  └──────┴──────┘  ┗━━━━━━┻━━━━━━┛  ╚══════╩══════╝\r\n\r\n" +
              "  ▁▂▃▄▅▆▇█ ▏▎▍▌▋▊▉█  ░░▒▒▓▓██  ▖▗▘▙▚▛▜▝▞▟  ▀▄▀▄\r\n");
    }

    private void Unicode()
    {
        Write("  Wide:      日本語のテキスト  한국어  中文\r\n" +
              "  Emoji:     😀 🎉 👍🏽 👨‍👩‍👧 🇸🇪\r\n" +
              "  Combining: é ä ô ñ\r\n" +
              "  Symbols:   → ⇒ ∀ ∃ ∑ √ ∞ ≠ ≤ ≥ ★ ♥ ✓\r\n");
    }

    private void Seq(int count)
    {
        var sb = new StringBuilder();
        for (int i = 1; i <= count; i++)
        {
            sb.Append($"{Esc}[{31 + i % 6}m{i,7}{Esc}[0m  line {i} of {count}\r\n");
            if (sb.Length > 32 * 1024)
            {
                Write(sb.ToString());
                sb.Clear();
                if (_toTerminal.IsCompleted) return; // the terminal has gone
            }
        }
        Write(sb.ToString());
    }

    // ──── Full-screen programs ────

    /// <summary>A dashboard on the alternate screen, redrawn ten times a second inside
    /// synchronized updates, with a mouse-selectable row and arrow keys.</summary>
    private void Top()
    {
        Write(Esc + "[?1049h" + Esc + "[?25l" + Esc + "[?1000h" + Esc + "[?1006h");
        int selected = 0, frame = 0;
        var clock = Stopwatch.StartNew();
        var random = new Random(1);
        var load = new double[8];
        try
        {
            while (true)
            {
                for (int i = 0; i < load.Length; i++)
                    load[i] = Math.Clamp(load[i] + random.NextDouble() * 0.2 - 0.1, 0, 1);
                DrawTop(selected, frame++, clock.Elapsed, load);

                var input = NextInput(100);
                if (input is null) return;
                foreach (var key in Tokens(input))
                {
                    if (key is "q" or "\x03" or Esc) return;
                    if (key is Esc + "[A" or Esc + "OA") selected = Math.Max(0, selected - 1);
                    if (key is Esc + "[B" or Esc + "OB") selected = Math.Min(load.Length - 1, selected + 1);
                    if (key.StartsWith(Esc + "[<0;", StringComparison.Ordinal) && key.EndsWith('M'))
                    {
                        var fields = key[3..^1].Split(';');
                        // The first CPU row is the fifth on the screen; reports count from 1.
                        if (int.TryParse(fields[2], out var row) && row - 5 is >= 0 and var r && r < load.Length) selected = r;
                    }
                }
            }
        }
        finally
        {
            Write(Esc + "[?1006l" + Esc + "[?1000l" + Esc + "[?25h" + Esc + "[?1049l");
        }
    }

    private void DrawTop(int selected, int frame, TimeSpan elapsed, double[] load)
    {
        int cols = _columns, rows = _rows;
        var sb = new StringBuilder();
        sb.Append(Esc + "[?2026h" + Esc + "[H" + Esc + "[2J");
        sb.Append($"{Esc}[7m{Pad($" demo top — {cols}×{rows} — frame {frame} — {elapsed:mm\\:ss}", cols)}{Esc}[0m\r\n");
        sb.Append($"\r\n {Esc}[1mCPU{Esc}[0m   ↑/↓ or click a row to select it, q to quit\r\n\r\n");
        int bar = Math.Max(10, cols - 20);
        for (int i = 0; i < load.Length && 4 + i < rows; i++)
        {
            int filled = (int)(load[i] * bar);
            string colour = load[i] > 0.8 ? "31" : load[i] > 0.5 ? "33" : "32";
            string mark = i == selected ? Esc + "[7m" : "";
            sb.Append($"{mark} cpu{i} {Esc}[0m [{Esc}[{colour}m{new string('█', filled)}{Esc}[0m{new string('·', bar - filled)}] {load[i] * 100,3:F0}%\r\n");
        }
        sb.Append(Esc + "[?2026l");
        Write(sb.ToString());
    }

    private static string Pad(string s, int width) => s.Length >= width ? s[..width] : s + new string(' ', width - s.Length);

    /// <summary>Reports mouse events as a program reading SGR mouse reports sees them.</summary>
    private void Mouse()
    {
        Write(Esc + "[?1003h" + Esc + "[?1006h");
        Write("Mouse reporting on (any motion, SGR). Move, click, wheel; q quits. Shift+drag still selects.\r\n");
        try
        {
            while (NextInput() is { } input)
                foreach (var key in Tokens(input))
                {
                    if (key is "q" or "\x03") return;
                    if (key.StartsWith(Esc + "[<", StringComparison.Ordinal))
                    {
                        var f = key[3..^1].Split(';');
                        int b = int.Parse(f[0]);
                        string what = (b & 64) != 0 ? ((b & 1) != 0 ? "wheel down" : "wheel up")
                            : (b & 32) != 0 ? ((b & 3) == 3 ? "move" : "drag")
                            : key[^1] == 'M' ? $"press {(b & 3) switch { 0 => "left", 1 => "middle", _ => "right" }}"
                            : "release";
                        Write($"\r{Esc}[K  {what,-12} column {f[1],3}  row {f[2],3}");
                    }
                }
        }
        finally
        {
            Write("\r\n" + Esc + "[?1006l" + Esc + "[?1003l");
        }
    }

    /// <summary>
    /// The heaviest thing a terminal draws: every cell of the screen changing every frame, in
    /// runs of a dozen colours a row. Each frame is a synchronized update, so the terminal draws
    /// whole frames; the pipe is kept from running ahead of what is drawn by pacing at 120 a
    /// second.
    /// </summary>
    private void Flood(double seconds)
    {
        Write(Esc + "[?1049h" + Esc + "[?25l");
        var clock = Stopwatch.StartNew();
        int frame = 0;
        const string Glyphs = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789{}[]()<>=+-*/";
        var sb = new StringBuilder();
        while (clock.Elapsed.TotalSeconds < seconds)
        {
            int cols = _columns, rows = _rows;
            sb.Clear().Append(Esc + "[?2026h" + Esc + "[H");
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    if (c % 8 == 0) sb.Append($"{Esc}[38;5;{16 + (r * 7 + c / 8 + frame) % 216}m");
                    sb.Append(Glyphs[(r * 31 + c + frame) % Glyphs.Length]);
                }
                if (r < rows - 1) sb.Append("\r\n");
            }
            sb.Append(Esc + "[0m" + Esc + "[?2026l");
            Write(sb.ToString());
            frame++;
            var next = TimeSpan.FromSeconds(frame / 120.0);
            if (clock.Elapsed < next) Thread.Sleep(next - clock.Elapsed);
            if (NextInput(0) is null) break;
        }
        Write(Esc + "[?25h" + Esc + "[?1049l" + $"flood: {frame} frames in {clock.Elapsed.TotalSeconds:F1} s\r\n");
    }
}
