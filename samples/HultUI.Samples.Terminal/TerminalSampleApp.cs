using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Terminal;
using HultUI.Terminal.Pty;

namespace HultUI.Samples.Terminal;

// Inside a HultUI.* namespace bare `Text` binds to the HultUI.Text namespace.
using Text = HultUI.Core.Builders.Text;

/// <summary>What the sample's terminal runs.</summary>
public enum SampleProgram
{
    /// <summary>The user's shell under a pseudoterminal (<see cref="PtyLaunch.DefaultShell"/>).</summary>
    Shell,

    /// <summary>The in-process <see cref="DemoShell"/>, which needs no pseudoterminal.</summary>
    Demo,
}

/// <summary>
/// The window: a header with what the controller's signals say (the program's title, the grid's
/// size, the scrollback, whether the program is running) over the terminal itself. Built as a
/// class with <see cref="Mount"/> so it can be hosted headless.
/// </summary>
public sealed class TerminalSampleApp : IDisposable
{
    private static readonly int[] ScrollbackChoices = [1_000, 5_000, 50_000];
    private static readonly string[] ProgramNames = ["Shell", "Demo shell"];
    private static readonly Color HeaderBackground = new(31, 31, 31);
    private static readonly Color Muted = new(150, 150, 150);

    public TerminalSampleApp(SampleProgram program = SampleProgram.Shell)
    {
        Program = new Signal<SampleProgram>(program);
        Session = new Signal<TerminalController>(Start(program));
    }

    /// <summary>What the next <see cref="Restart"/> runs.</summary>
    public Signal<SampleProgram> Program { get; }

    /// <summary>The running session; replaced by <see cref="Restart"/>.</summary>
    public Signal<TerminalController> Session { get; }

    /// <summary>How many times the program rang the bell.</summary>
    public Signal<int> Bells { get; } = new(0);

    /// <summary>Why the shell could not start, when the demo stands in for it.</summary>
    public Signal<string?> Problem { get; } = new(null);

    public TerminalElement Terminal { get; private set; } = null!;

    /// <summary>The demo program behind the running session, when that is what runs.</summary>
    public DemoShell? Demo { get; private set; }

    /// <summary>The shell behind the running session, when that is what runs.</summary>
    public PtyConnection? Shell { get; private set; }

    private TerminalController Start(SampleProgram program)
    {
        Demo = null;
        Shell = null;
        ITerminalConnection connection;
        if (program == SampleProgram.Shell)
        {
            try
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                connection = Shell = PtyConnection.Start(PtyLaunch.DefaultShell(Directory.Exists(home) ? home : null));
                Problem.Value = null;
            }
            catch (Exception ex) when (ex is FileNotFoundException or InvalidOperationException or IOException)
            {
                // No pseudoterminal here: show what the terminal can do anyway, and say why.
                Problem.Value = "No shell: " + ex.Message;
                connection = (Demo = new DemoShell()).Connection;
            }
        }
        else
        {
            connection = (Demo = new DemoShell()).Connection;
        }

        var controller = new TerminalController(connection, new TerminalOptions { Scrollback = 5_000 });
        controller.Bell += () => Bells.Value++;
        controller.LinkActivated += OpenLink;
        return controller;
    }

    /// <summary>
    /// Ctrl+click on a link the program wrote. Web addresses only: the link is whatever the
    /// program said, and handed to the system a <c>file:</c> link to a program would run it.
    /// </summary>
    private static void OpenLink(TerminalLink link)
    {
        if (!Uri.TryCreate(link.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return;
        try
        {
            if (OperatingSystem.IsWindows())
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            else
                System.Diagnostics.Process.Start(OperatingSystem.IsMacOS() ? "open" : "xdg-open", uri.AbsoluteUri);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }

    /// <summary>Ends the program, if it is still running, and starts <see cref="Program"/> in its place.</summary>
    public void Restart()
    {
        var old = Session.Value;
        var next = Start(Program.Value);
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
            string what = app.Problem.Value ?? (app.Shell is { } shell ? Path.GetFileName(shell.Program) : "demo shell");

            var program = Dropdown.New(ProgramNames, (int)app.Program.Value);
            program.AccessibleName = "Program";
            program.OnSelectionChanged = i =>
            {
                app.Program.Value = (SampleProgram)i;
                app.Restart();
            };

            int chosen = Array.IndexOf(ScrollbackChoices, c.Scrollback);
            var scrollback = Dropdown.New(ScrollbackChoices.Select(n => $"{n:N0} lines").ToList(), chosen);
            scrollback.AccessibleName = "Scrollback";
            scrollback.OnSelectionChanged = i => app.Session.Value.Scrollback = ScrollbackChoices[i];

            var restart = Button.New(c.State == TerminalState.Exited ? "Start again" : "Restart", ButtonVariant.Secondary)
                .OnClick(_ => app.Restart());

            return Flex.Row(gap: 12).Padding(new Thickness(6f, 10f)).AlignItems(AlignItems.Center)
                .Background(HeaderBackground)
                .Children(
                    Text.New(title).FontSize(13f).TextColor(Color.White).FlexGrow(1).MaxLines(1),
                    Text.New($"{what}  ·  {c.Columns}×{c.Rows}  ·  {state}{where}{bells}").FontSize(12f).TextColor(Muted),
                    program,
                    Text.New("Scrollback").FontSize(12f).TextColor(Muted),
                    scrollback,
                    restart);
        }
    }
}
