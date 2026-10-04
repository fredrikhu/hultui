using System;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>A test of something only Windows has — a drive letter, a <c>\\server\share</c>
/// path, the shell's Properties sheet and context menu. Elsewhere it reports as skipped,
/// with the reason, rather than passing without having run.</summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute(string reason)
    {
        if (!OperatingSystem.IsWindows()) Skip = "Windows-only: " + reason;
    }
}

/// <inheritdoc cref="WindowsFactAttribute"/>
public sealed class WindowsTheoryAttribute : TheoryAttribute
{
    public WindowsTheoryAttribute(string reason)
    {
        if (!OperatingSystem.IsWindows()) Skip = "Windows-only: " + reason;
    }
}
