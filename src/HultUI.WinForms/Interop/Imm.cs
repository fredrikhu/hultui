using System.Runtime.InteropServices;
using System.Text;

namespace HultUI.WinForms.Interop;

/// <summary>
/// The slice of IMM32 needed to host an East Asian input method inside a control the
/// framework draws itself.
/// </summary>
/// <remarks>
/// Windows would normally draw the composition string and the candidate list for a
/// control. HultUI draws its own text, so the control takes the composition over
/// (<see cref="GCS_COMPSTR"/>) and renders it as the framework's preedit, while leaving
/// the candidate <em>window</em> to the IME — positioned at the caret through
/// <see cref="SetCompositionPosition"/>, or it appears at the control's corner.
/// </remarks>
internal static class Imm
{
    internal const int WM_IME_STARTCOMPOSITION = 0x010D;
    internal const int WM_IME_ENDCOMPOSITION = 0x010E;
    internal const int WM_IME_COMPOSITION = 0x010F;
    internal const int WM_CHAR = 0x0102;

    internal const int GCS_COMPSTR = 0x0008;
    internal const int GCS_CURSORPOS = 0x0080;
    internal const int GCS_RESULTSTR = 0x0800;

    private const int CFS_POINT = 0x0002;
    private const int CFS_EXCLUDE = 0x0080;

    [StructLayout(LayoutKind.Sequential)]
    private struct PointStruct { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RectStruct { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct CompositionForm
    {
        public int Style;
        public PointStruct CurrentPos;
        public RectStruct Area;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CandidateForm
    {
        public int Index;
        public int Style;
        public PointStruct CurrentPos;
        public RectStruct Area;
    }

    [DllImport("imm32.dll")] private static extern IntPtr ImmGetContext(IntPtr hwnd);
    [DllImport("imm32.dll")] private static extern bool ImmReleaseContext(IntPtr hwnd, IntPtr context);
    [DllImport("imm32.dll")] private static extern IntPtr ImmAssociateContext(IntPtr hwnd, IntPtr context);
    [DllImport("imm32.dll")] private static extern IntPtr ImmCreateContext();
    [DllImport("imm32.dll")] private static extern bool ImmDestroyContext(IntPtr context);
    [DllImport("imm32.dll")] private static extern bool ImmSetCompositionWindow(IntPtr context, ref CompositionForm form);
    [DllImport("imm32.dll")] private static extern bool ImmSetCandidateWindow(IntPtr context, ref CandidateForm form);
    [DllImport("imm32.dll")] private static extern bool ImmNotifyIME(IntPtr context, int action, int index, int value);
    [DllImport("imm32.dll", CharSet = CharSet.Unicode)]
    private static extern int ImmGetCompositionStringW(IntPtr context, int index, byte[]? buffer, int length);

    private const int NI_COMPOSITIONSTR = 0x0015;
    private const int CPS_CANCEL = 0x0004;

    /// <summary>
    /// Reads a composition string (<see cref="GCS_COMPSTR"/>) or the committed result
    /// (<see cref="GCS_RESULTSTR"/>). Returns null when the IME has nothing of that kind.
    /// </summary>
    internal static string? GetCompositionString(IntPtr hwnd, int flag)
    {
        var context = ImmGetContext(hwnd);
        if (context == IntPtr.Zero) return null;
        try
        {
            // Called once for the byte length, then again for the data — the documented
            // two-step, and the length is bytes rather than characters.
            int byteLength = ImmGetCompositionStringW(context, flag, null, 0);
            if (byteLength <= 0) return byteLength == 0 ? string.Empty : null;

            var buffer = new byte[byteLength];
            ImmGetCompositionStringW(context, flag, buffer, byteLength);
            return Encoding.Unicode.GetString(buffer);
        }
        finally { ImmReleaseContext(hwnd, context); }
    }

    /// <summary>The caret position inside the composition, in characters.</summary>
    internal static int GetCursorPosition(IntPtr hwnd)
    {
        var context = ImmGetContext(hwnd);
        if (context == IntPtr.Zero) return 0;
        try { return Math.Max(0, ImmGetCompositionStringW(context, GCS_CURSORPOS, null, 0)); }
        finally { ImmReleaseContext(hwnd, context); }
    }

    /// <summary>
    /// Puts the composition and candidate windows at the caret, in client pixels, so a
    /// candidate list appears under the text being typed rather than at the corner of
    /// the control.
    /// </summary>
    internal static void SetCompositionPosition(IntPtr hwnd, int x, int y, int width, int height)
    {
        var context = ImmGetContext(hwnd);
        if (context == IntPtr.Zero) return;
        try
        {
            var composition = new CompositionForm
            {
                Style = CFS_POINT,
                CurrentPos = new PointStruct { X = x, Y = y },
            };
            ImmSetCompositionWindow(context, ref composition);

            // CFS_EXCLUDE keeps the candidate list clear of the caret rectangle, so it
            // does not cover the text it is offering completions for.
            var candidate = new CandidateForm
            {
                Index = 0,
                Style = CFS_EXCLUDE,
                CurrentPos = new PointStruct { X = x, Y = y },
                Area = new RectStruct { Left = x, Top = y, Right = x + width, Bottom = y + height },
            };
            ImmSetCandidateWindow(context, ref candidate);
        }
        finally { ImmReleaseContext(hwnd, context); }
    }

    /// <summary>Cancels an in-flight composition, dropping whatever was being composed.</summary>
    internal static void CancelComposition(IntPtr hwnd)
    {
        var context = ImmGetContext(hwnd);
        if (context == IntPtr.Zero) return;
        try { ImmNotifyIME(context, NI_COMPOSITIONSTR, CPS_CANCEL, 0); }
        finally { ImmReleaseContext(hwnd, context); }
    }

    /// <summary>
    /// Arms or disarms the IME for a window.
    /// </summary>
    /// <remarks>
    /// Disarming matters as much as arming: an IME left associated over non-text UI
    /// eats dead keys and the IME-toggle hotkey, which is why the framework starts and
    /// stops text input on focus rather than leaving it on. The context is created once
    /// and kept, because destroying and recreating it loses the user's IME mode.
    /// </remarks>
    internal sealed class Association(IntPtr hwnd) : IDisposable
    {
        private IntPtr _context = ImmCreateContext();
        private bool _armed;

        internal void Arm()
        {
            if (_armed || _context == IntPtr.Zero) return;
            ImmAssociateContext(hwnd, _context);
            _armed = true;
        }

        internal void Disarm()
        {
            if (!_armed) return;
            ImmAssociateContext(hwnd, IntPtr.Zero);
            _armed = false;
        }

        /// <summary>No input context at all, armed or not: a window starts out holding the
        /// thread's default one, which <see cref="Disarm"/> leaves alone when nothing was armed.</summary>
        internal void Detach()
        {
            ImmAssociateContext(hwnd, IntPtr.Zero);
            _armed = false;
        }

        public void Dispose()
        {
            Disarm();
            if (_context != IntPtr.Zero) { ImmDestroyContext(_context); _context = IntPtr.Zero; }
        }
    }
}
