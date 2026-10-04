using HultUI.Headless;
using Xunit;

namespace HultUI.Terminal.Pty.Tests;

/// <summary>
/// A real program under a real pseudoterminal, behind a <see cref="TerminalController"/>, under a
/// headless session so the controller's posts reach a UI thread. Waits are on the screen's text,
/// pumped, with generous limits: a pseudoconsole on a loaded CI runner is slow to start.
/// </summary>
internal sealed class PtyTerm : IDisposable
{
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private PtyTerm(HeadlessSession session, PtyConnection connection, TerminalController controller)
    {
        Session = session;
        Connection = connection;
        Controller = controller;
    }

    public HeadlessSession Session { get; }
    public PtyConnection Connection { get; }
    public TerminalController Controller { get; }

    public static PtyTerm Start(PtyLaunch launch, int columns = 80, int rows = 24)
    {
        var session = HeadlessSession.Create(200, 100);
        try
        {
            var connection = PtyConnection.Start(launch, columns, rows);
            var controller = new TerminalController(connection);
            controller.Resize(columns, rows);
            return new PtyTerm(session, connection, controller);
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    /// <summary>The test child (HultUI.Terminal.Pty.Tests.Child) in <paramref name="mode"/>.</summary>
    public static PtyTerm Child(params string[] arguments) => Start(ChildLaunch(arguments));

    public static PtyLaunch ChildLaunch(params string[] arguments) =>
        new(Dotnet, [ChildDll, .. arguments]);

    /// <summary>Beside this test project: tests/&lt;name&gt;/bin/&lt;config&gt;/&lt;tfm&gt;/, as this assembly is.</summary>
    public static string ChildDll
    {
        get
        {
            const string name = "HultUI.Terminal.Pty.Tests.Child";
            var tfm = new DirectoryInfo(AppContext.BaseDirectory);
            var config = tfm.Parent!;
            var tests = config.Parent!.Parent!.Parent!;
            var dll = Path.Combine(tests.FullName, name, "bin", config.Name, tfm.Name, name + ".dll");
            Assert.True(File.Exists(dll), "not built: " + dll);
            return dll;
        }
    }

    public static string Dotnet => PtyLaunch.FindProgram("dotnet")
        ?? throw new InvalidOperationException("dotnet is not on PATH");

    public string Screen => Controller.ReadScreen();

    /// <summary>Pumps until the screen satisfies <paramref name="condition"/>; fails with the
    /// screen as it was.</summary>
    public void WaitFor(Func<string, bool> condition, TimeSpan? timeout = null, string? what = null)
    {
        try
        {
            Session.PumpUntil(() => condition(Screen), timeout ?? Patience);
        }
        catch (TimeoutException)
        {
            Assert.Fail($"timed out waiting for {what ?? "the screen"}; it shows:\n{Screen}");
        }
    }

    public void WaitForText(string text, TimeSpan? timeout = null) =>
        WaitFor(s => s.Contains(text, StringComparison.Ordinal), timeout, $"'{text}'");

    public void WaitForExit(TimeSpan? timeout = null)
    {
        try
        {
            Session.PumpUntil(() => Controller.State == TerminalState.Exited, timeout ?? Patience);
        }
        catch (TimeoutException)
        {
            Assert.Fail($"the program did not end; the screen shows:\n{Screen}");
        }
    }

    public void Dispose()
    {
        Controller.Dispose();
        Session.Dispose();
    }
}

/// <summary>A fact that runs only where <paramref name="program"/> is on PATH (and, when asked,
/// only on Windows).</summary>
internal sealed class ProgramFactAttribute : FactAttribute
{
    public ProgramFactAttribute(string program, bool windowsOnly = false)
    {
        if (windowsOnly && !OperatingSystem.IsWindows()) Skip = "Windows only";
        else if (PtyLaunch.FindProgram(program) is null) Skip = $"{program} is not on PATH";
    }
}
