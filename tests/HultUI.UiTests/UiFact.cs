using Xunit;

namespace HultUI.UiTests;

/// <summary>
/// Whether the real-window UI-automation tests are allowed to run.
///
/// <para>These launch actual sample windows and drive them through UI Automation, which
/// makes them unlike every other suite here: they need a desktop session and a GPU
/// context, they take over the foreground, and they cannot share a machine with someone
/// using it. That last point is the practical one — a plain <c>dotnet test HultUI.slnx</c>
/// used to throw sample windows over whatever the developer was doing.</para>
///
/// <para>So they are opt-in. Set <c>HULTUI_UITESTS=1</c> to run them; without it they
/// report as skipped, which keeps them visible in the run rather than quietly absent the
/// way a name filter leaves them.</para>
/// </summary>
internal static class UiTestGate
{
    internal const string Variable = "HULTUI_UITESTS";

    internal static bool Enabled =>
        Environment.GetEnvironmentVariable(Variable) is { Length: > 0 } v
        && !v.Equals("0", StringComparison.Ordinal)
        && !v.Equals("false", StringComparison.OrdinalIgnoreCase);

    internal const string Reason =
        "Real-window UI-automation test: launches sample windows and takes the foreground. "
        + "Set " + Variable + "=1 to run.";
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
