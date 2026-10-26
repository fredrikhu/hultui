using System.Diagnostics;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Terminal;
using HultUI.Terminal.Testing;

namespace PerfSuite;

static partial class Scenarios
{
    // The controller's reader and writer threads outlive a session (nothing unmounts it), so a
    // scenario's setup gives back the one the last session made.
    private static TerminalController? _lastTerminal;

    private static IEnumerable<Scenario> TerminalScenarios()
    {
        yield return new Scenario("terminal/coloured screen, forced frame",
            "A terminal in the platform's monospace face showing four `ls --color`-like listings, a colour change every few cells on every row, drawn again whole (a forced frame)",
            ctx =>
            {
                _lastTerminal?.Dispose();
                var connection = new FakeConnection();
                var controller = _lastTerminal = new TerminalController(connection);
                var element = TerminalElement.New(controller).FlexGrow(1);
                ctx.Mount(Flex.Column().FlexGrow(1).Children(element));

                const int Listings = 4;
                for (int i = 0; i < Listings; i++) connection.Emit(ColourListing + "demo$ \r\n");
                connection.Emit("demo$ ");
                // The program's output is read on the controller's own thread.
                var waited = Stopwatch.StartNew();
                while (!Shown(controller, Listings) && waited.Elapsed < TimeSpan.FromSeconds(10))
                {
                    ctx.Pump();
                    Thread.Sleep(5);
                }
                ctx.Pump(3);
                return new Op
                {
                    Run = _ => element.MarkPaintDirty(),
                    Check = _ => Shown(controller, Listings) ? null
                        : $"the terminal shows {controller.ReadScreen().Split('\n').Length} rows, not the {Listings} listings and the prompt",
                };
            });

        yield return new Scenario("terminal/dense colour screen, unique rows, forced frame",
            "A 1600×1000 terminal showing an htop- or diff-like screen, a colour change every two to four cells and every row different (numbered), drawn again whole (a forced frame): more colour stretches than the 2,048 blobs a context kept",
            ctx =>
            {
                _lastTerminal?.Dispose();
                var connection = new FakeConnection();
                var controller = _lastTerminal = new TerminalController(connection);
                var element = TerminalElement.New(controller).FlexGrow(1);
                ctx.Mount(Flex.Column().FlexGrow(1).Children(element));

                // More rows than the screen holds, so it is full of them.
                const int Rows = 80;
                string last = $"{Rows,5} ";
                connection.Emit(DenseRows(Rows) + "demo$ ");
                var waited = Stopwatch.StartNew();
                while (!DenseShown(controller, last) && waited.Elapsed < TimeSpan.FromSeconds(10))
                {
                    ctx.Pump();
                    Thread.Sleep(5);
                }
                ctx.Pump(3);
                return new Op
                {
                    Run = _ => element.MarkPaintDirty(),
                    Check = _ => DenseShown(controller, last) ? null
                        : $"the terminal does not show row {Rows} and the prompt",
                };
            }) { Width = 1600, Height = 1000 };
    }

    private static bool DenseShown(TerminalController controller, string lastRow)
    {
        string screen = controller.ReadScreen();
        return screen.EndsWith("demo$", StringComparison.Ordinal) && screen.Contains(lastRow, StringComparison.Ordinal);
    }

    /// <summary>An htop- or diff-like screen: rows of sixty short fields, each in one of eleven
    /// foreground colours, every row numbered so no two draw the same glyphs.</summary>
    private static string DenseRows(int rows)
    {
        const string Esc = "\x1b";
        string[] fields = ["PID", "42", "usr", "S", "0.3", "1.2", "R", "bash", "py", "x", "[]", "{}"];
        int[] fg = [31, 32, 33, 34, 35, 36, 37, 91, 92, 94, 96];
        var sb = new System.Text.StringBuilder();
        for (int row = 1; row <= rows; row++)
        {
            sb.Append($"{row,5} ");
            for (int k = 0; k < 60; k++)
                sb.Append(Esc).Append('[').Append(fg[(row + k) % fg.Length]).Append('m')
                    .Append(fields[(row * 5 + k) % fields.Length]).Append(Esc).Append("[0m ");
            sb.Append("\r\n");
        }
        return sb.ToString();
    }

    private static bool Shown(TerminalController controller, int listings)
    {
        string screen = controller.ReadScreen();
        return screen.EndsWith("demo$", StringComparison.Ordinal)
            && screen.Split('\n').Count(l => l.Trim() == "main") >= listings;
    }

    /// <summary>An <c>ls --color</c> or build-log like listing: eight rows of ten names, each in
    /// one of eleven foreground colours, then a branch name on green.</summary>
    private static readonly string ColourListing = MakeColourListing();

    private static string MakeColourListing()
    {
        const string Esc = "\x1b";
        string[] words = ["src", "bin", "obj", "README.md", "build.sh", "tests", "docs", "LICENSE", "app.json", "main.cs", "lib.so", "run.ps1"];
        int[] fg = [31, 32, 33, 34, 35, 36, 37, 91, 92, 94, 96];
        var sb = new System.Text.StringBuilder();
        for (int row = 0; row < 8; row++)
        {
            for (int k = 0; k < 10; k++)
                sb.Append(Esc).Append('[').Append(fg[(row + k) % fg.Length]).Append('m')
                    .Append(words[(row * 3 + k) % words.Length]).Append(Esc).Append("[0m  ");
            sb.Append("\r\n");
        }
        sb.Append("  ").Append(Esc).Append("[30;42m main ").Append(Esc).Append("[0m\r\n");
        return sb.ToString();
    }
}
