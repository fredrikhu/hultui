using Xunit;

namespace HultUI.UiTests;

/// <summary>
/// Whether the real-window UI-automation tests are allowed to run.
///
/// <para>These launch actual sample windows and drive them through UI Automation, which
/// makes them unlike every other suite here: they need a desktop session and a GPU
/// context, and they put windows on a desktop someone may be using. A plain
/// <c>dotnet test HultUI.slnx</c> used to throw sample windows over whatever the developer
/// was doing.</para>
///
/// <para>So they are opt-in. Set <c>HULTUI_UITESTS=1</c> to run them; without it they
/// report as skipped, which keeps them visible in the run rather than quietly absent the
/// way a name filter leaves them. Their windows never take the foreground
/// (<see cref="ForegroundGuard"/>).</para>
///
/// <para>A few tests need the foreground itself — keyboard focus and Tab traversal in a
/// hosted tree, focus inside a web page, a DevTools window the browser activates — and
/// cannot keep out of it. They run only with <c>HULTUI_UITESTS_FOREGROUND=1</c> as well,
/// on a desktop nobody is using; otherwise they skip, saying why.</para>
/// </summary>
internal static class UiTestGate
{
    internal const string Variable = "HULTUI_UITESTS";
    internal const string ForegroundVariable = "HULTUI_UITESTS_FOREGROUND";

    internal static bool Enabled => IsSet(Variable);

    /// <summary>The tests that take the foreground may run: both variables are set.</summary>
    internal static bool ForegroundEnabled => Enabled && IsSet(ForegroundVariable);

    private static bool IsSet(string variable) =>
        Environment.GetEnvironmentVariable(variable) is { Length: > 0 } v
        && !v.Equals("0", StringComparison.Ordinal)
        && !v.Equals("false", StringComparison.OrdinalIgnoreCase);

    internal const string Reason =
        "Real-window UI-automation test: launches windows (kept out of the foreground). "
        + "Set " + Variable + "=1 to run.";

    internal const string ForegroundReason =
        "Takes the foreground on purpose (keyboard focus, Tab traversal, a DevTools window), "
        + "so it runs only on a desktop nobody is using: set " + ForegroundVariable + "=1 as well as "
        + Variable + "=1 to run.";

    /// <summary>Why a test that takes the foreground skips, or null when it may run.</summary>
    internal static string? ForegroundSkip =>
        !Enabled ? Reason : !ForegroundEnabled ? ForegroundReason : null;
}

/// <summary>A <see cref="FactAttribute"/> that skips unless <see cref="UiTestGate"/> allows it.</summary>
public sealed class UiFactAttribute : FactAttribute
{
    public UiFactAttribute()
    {
        if (!UiTestGate.Enabled) Skip = UiTestGate.Reason;
    }
}

/// <summary>The <see cref="TheoryAttribute"/> counterpart of <see cref="UiFactAttribute"/>.</summary>
public sealed class UiTheoryAttribute : TheoryAttribute
{
    public UiTheoryAttribute()
    {
        if (!UiTestGate.Enabled) Skip = UiTestGate.Reason;
    }
}

/// <summary>
/// A real-window test that takes the foreground on purpose: it runs only with
/// <c>HULTUI_UITESTS_FOREGROUND=1</c> as well as <c>HULTUI_UITESTS=1</c>.
/// </summary>
public sealed class UiForegroundFactAttribute : FactAttribute
{
    public UiForegroundFactAttribute()
    {
        if (UiTestGate.ForegroundSkip is { } reason) Skip = reason;
    }
}
