using System.Runtime.InteropServices;

namespace HultUI.Text;

/// <summary>
/// UAX #9 embedding levels via native ICU's <c>ubidi_*</c> API — the same library
/// and binding mechanism LineBreaker already uses for UAX #14. One call per
/// paragraph of RTL-capable text (the pipeline's cheap classifier keeps Latin text
/// away from here entirely).
/// </summary>
internal static class BidiAnalyzer
{
    private static ubidi_open_delegate? _open;
    private static ubidi_setPara_delegate? _setPara;
    private static ubidi_getLevels_delegate? _getLevels;
    private static ubidi_close_delegate? _close;

    /// <summary>
    /// Per-UTF-16-unit embedding levels (even = LTR, odd = RTL). On any ICU
    /// failure the whole text degrades to the paragraph direction — wrong ordering
    /// for mixed text, but rendered text rather than an exception in a paint path.
    /// </summary>
    public static unsafe byte[] Levels(string text, bool rtlParagraph)
    {
        var fallbackLevel = (byte)(rtlParagraph ? 1 : 0);
        var levels = new byte[text.Length];
        if (text.Length == 0) return levels;

        try
        {
            _open ??= IcuLibrary.GetDelegate<ubidi_open_delegate>("ubidi_open");
            _setPara ??= IcuLibrary.GetDelegate<ubidi_setPara_delegate>("ubidi_setPara");
            _getLevels ??= IcuLibrary.GetDelegate<ubidi_getLevels_delegate>("ubidi_getLevels");
            _close ??= IcuLibrary.GetDelegate<ubidi_close_delegate>("ubidi_close");

            IntPtr bidi = _open();
            if (bidi == IntPtr.Zero) { Array.Fill(levels, fallbackLevel); return levels; }
            try
            {
                fixed (char* pText = text)
                {
                    // The text must stay pinned for the object's whole lifetime —
                    // getLevels reads it — hence everything inside one fixed block.
                    int status = 0;
                    _setPara(bidi, pText, text.Length, fallbackLevel, IntPtr.Zero, ref status);
                    if (status > 0) { Array.Fill(levels, fallbackLevel); return levels; }

                    status = 0;
                    byte* resolved = _getLevels(bidi, ref status);
                    if (status > 0 || resolved == null) { Array.Fill(levels, fallbackLevel); return levels; }

                    Marshal.Copy((IntPtr)resolved, levels, 0, text.Length);
                    return levels;
                }
            }
            finally
            {
                _close(bidi);
            }
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            Array.Fill(levels, fallbackLevel);
            return levels;
        }
    }

    private delegate IntPtr ubidi_open_delegate();
    private unsafe delegate void ubidi_setPara_delegate(
        IntPtr bidi, char* text, int length, byte paraLevel, IntPtr embeddingLevels, ref int status);
    private unsafe delegate byte* ubidi_getLevels_delegate(IntPtr bidi, ref int status);
    private delegate void ubidi_close_delegate(IntPtr bidi);
}
