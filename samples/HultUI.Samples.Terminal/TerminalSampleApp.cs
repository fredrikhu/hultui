using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Terminal;

namespace HultUI.Samples.Terminal;

// Inside a HultUI.* namespace bare `Text` binds to the HultUI.Text namespace.
using Text = HultUI.Core.Builders.Text;

/// <summary>
/// The window: a header with what the controller's signals say (the program's title, the grid's
/// size, the scrollback, whether the program is running) over the terminal itself. Built as a
/// class with <see cref="Mount"/> so it can be hosted headless.
/// </summary>
public sealed class TerminalSampleApp : IDisposable
{
    private static readonly int[] ScrollbackChoices = [1_000, 5_000, 50_000];
    private static readonly Color HeaderBackground = new(31, 31, 31);
    private static readonly Color Muted = new(150, 150, 150);

    public TerminalSampleApp() => Session = new Signal<TerminalController>(Start());

    /// <summary>The running session; replaced by <see cref="Restart"/>.</summary>
    public Signal<TerminalController> Session { get; }

    /// <summary>How many times the program rang the bell.</summary>
    public Signal<int> Bells { get; } = new(0);

    public TerminalElement Terminal { get; private set; } = null!;

    /// <summary>The program behind the running session.</summary>
    public DemoShell Shell { get; private set; } = null!;

    private TerminalController Start()
    {
        var shell = Shell = new DemoShell();
        var controller = new TerminalController(shell.Connection, new TerminalOptions { Scrollback = 5_000 });
        controller.Bell += () => Bells.Value++;
        return controller;
    }

    /// <summary>Ends the program, if it is still running, and starts a new one in its place.</summary>
    public void Restart()
    {
        var old = Session.Value;
        var next = Start();
        Terminal.Controller = next;
        Session.Value = next;
        old.Dispose();
        Terminal.Focus();
    }

    public Element Mount()
    {
        Terminal = TerminalElement.New(Session.Value).FlexGrow(1).FlexBasis(0);
        Terminal.AutomationId = "terminal";
        return Flex.Column().Background(new Color(12, 12, 12)).Children(
            new Header(this).Mount().FlexShrink(0),
            // A little room around the grid, as terminal windows have.
            Flex.Column().Padding(new Thickness(6f, 8f)).FlexGrow(1).FlexBasis(0).Children(Terminal));
    }

    public void Dispose() => Session.Value.Dispose();

    private sealed class Header(TerminalSampleApp app) : Component
    {
        public override Element Build()
        {
            var c = app.Session.Value;
            string title = c.Title is { Length: > 0 } t ? t : "Terminal";
            string state = c.State == TerminalState.Exited
                ? $"exited with {(c.ExitCode is { } code ? code.ToString() : "no code")}"
                : "running";
            string where = c.WorkingDirectory is { } dir ? "  ·  " + dir : "";
            string bells = app.Bells.Value > 0 ? $"  ·  bell ×{app.Bells.Value}" : "";

            int chosen = Array.IndexOf(ScrollbackChoices, c.Scrollback);
            var scrollback = Dropdown.New(ScrollbackChoices.Select(n => $"{n:N0} lines").ToList(), chosen);
            scrollback.AccessibleName = "Scrollback";
            scrollback.OnSelectionChanged = i => app.Session.Value.Scrollback = ScrollbackChoices[i];

            var restart = Button.New(c.State == TerminalState.Exited ? "Start again" : "Restart", ButtonVariant.Secondary)
                .OnClick(_ => app.Restart());

            return Flex.Row(gap: 12).Padding(new Thickness(6f, 10f)).AlignItems(AlignItems.Center)
                .Background(HeaderBackground)
                .Children(
                    Text.New(title).FontSize(13f).TextColor(Color.White).FlexGrow(1),
                    Text.New($"{c.Columns}×{c.Rows}  ·  {state}{where}{bells}").FontSize(12f).TextColor(Muted),
                    Text.New("Scrollback").FontSize(12f).TextColor(Muted),
                    scrollback,
                    restart);
        }
    }
}
