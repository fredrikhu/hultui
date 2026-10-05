using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace HultUI.Platform.SDL3;

/// <summary>
/// Keeps the input method out of a window while a password field has its keyboard. SDL gives
/// a password field the same IME as any other on Windows — its backend reads no text-input
/// type there — so a Chinese or Japanese input method composed a password in its candidate
/// window and learned it for its suggestions. A native <c>ES_PASSWORD</c> edit control leaves
/// its window with no input context, and so does this.
///
/// <para>The context detached is SDL's, which it keeps and associates again when text input
/// next starts — on the next ordinary field, and whenever the window regains focus. Nothing
/// here destroys it.</para>
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsInputMethod
{
    [DllImport("imm32.dll")]
    private static extern IntPtr ImmAssociateContext(IntPtr hwnd, IntPtr context);

    public static void Detach(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero) ImmAssociateContext(hwnd, IntPtr.Zero);
    }
}
