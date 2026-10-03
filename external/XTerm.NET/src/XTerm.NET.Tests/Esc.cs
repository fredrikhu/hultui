namespace XTerm.Tests;

/// <summary>
///     ANSI escape definitions and utility methods, for tests that drive a terminal the way a
///     program does.
/// </summary>
/// <remarks>
/// <para>Shaped after <c>Consolonia.Core.Text.Esc</c>: named constants for whole sequences and
/// builders for the ones that take arguments, with the coordinates zero-based on the way in.</para>
/// <para>Here rather than in each test class because the escape character had been written out in
/// twenty files, several of them with a private <c>Apc</c> beside it. A constant repeated per file
/// is one that can be got wrong per file, and a mistyped escape gives a test that passes while
/// driving the terminal with something other than what it claims to send.</para>
/// <para>In the test project rather than in the emulator because these are sequences to SEND, and
/// XTerm.NET's job is to receive them. Worth promoting to the library if consumers ever want them.
/// </para>
/// </remarks>
public static class Esc
{
    /// <summary>The string terminator that closes a DCS or APC sequence.</summary>
    public const string St = "\u001b\\";

    // screen buffer
    public const string ClearScreen = "\u001b[2J";
    public const string ClearLine = "\u001b[2K";

    /// <summary>A control sequence: ESC [ followed by the body.</summary>
    public static string Csi(string body)
    {
        return $"\u001b[{body}";
    }

    /// <summary>
    ///     Moves the cursor, taking the zero-based coordinates the buffer uses.
    /// </summary>
    /// <remarks>
    ///     CUP is one-based on the wire and the buffer is zero-based, so the conversion belongs
    ///     somewhere it can only be written once -- an off-by-one in a cursor address is the classic
    ///     way for a graphics test to assert against the wrong cell.
    /// </remarks>
    public static string SetCursorPosition(int x, int y)
    {
        return $"\u001b[{y + 1};{x + 1}H";
    }

    /// <summary>
    ///     An application programming command, which is how the Kitty graphics protocol arrives.
    /// </summary>
    /// <remarks>
    ///     The payload is separated by a semicolon and omitted entirely when there is none: a
    ///     trailing semicolon with nothing after it is a different sequence from no payload at all.
    /// </remarks>
    public static string Apc(string control, string payload = "")
    {
        return payload.Length == 0
            ? $"\u001b_G{control}{St}"
            : $"\u001b_G{control};{payload}{St}";
    }
}
