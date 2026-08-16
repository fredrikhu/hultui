using System.Runtime.InteropServices;
using HultUI.Core.Styling;

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
    private static ubidi_getParaLevel_delegate? _getParaLevel;
    private static ubidi_close_delegate? _close;

    /// <summary>ICU's "decide the paragraph direction from the first strong
    /// character, defaulting to left-to-right" pseudo-level (UBIDI_DEFAULT_LTR).</summary>
    private const byte DefaultLtr = 0xFE;

    /// <summary>
    /// Per-UTF-16-unit embedding levels (even = LTR, odd = RTL), plus the
    /// paragraph direction that was resolved — which for
    /// <see cref="TextDirection.Auto"/> is the answer to UAX #9 rules P2/P3 and is
    /// not knowable before the analysis. On any ICU failure the whole text
    /// degrades to the paragraph direction: wrong ordering for mixed text, but
    /// rendered text rather than an exception in a paint path.
    /// </summary>
    public static unsafe (byte[] Levels, bool RtlParagraph) Levels(string text, TextDirection direction)
    {
        bool autoDetect = direction == TextDirection.Auto;
        bool fallbackRtl = direction == TextDirection.Rtl || (autoDetect && FirstStrongIsRtl(text));
        var fallbackLevel = (byte)(fallbackRtl ? 1 : 0);
        byte requested = autoDetect ? DefaultLtr : fallbackLevel;

        var levels = new byte[text.Length];
        if (text.Length == 0) return (levels, fallbackRtl);

        (byte[], bool) Degrade()
        {
            Array.Fill(levels, fallbackLevel);
            return (levels, fallbackRtl);
        }

        try
        {
            _open ??= IcuLibrary.GetDelegate<ubidi_open_delegate>("ubidi_open");
            _setPara ??= IcuLibrary.GetDelegate<ubidi_setPara_delegate>("ubidi_setPara");
            _getLevels ??= IcuLibrary.GetDelegate<ubidi_getLevels_delegate>("ubidi_getLevels");
            _getParaLevel ??= IcuLibrary.GetDelegate<ubidi_getParaLevel_delegate>("ubidi_getParaLevel");
            _close ??= IcuLibrary.GetDelegate<ubidi_close_delegate>("ubidi_close");

            IntPtr bidi = _open();
            if (bidi == IntPtr.Zero) return Degrade();
            try
            {
                fixed (char* pText = text)
                {
                    // The text must stay pinned for the object's whole lifetime —
                    // getLevels reads it — hence everything inside one fixed block.
                    int status = 0;
                    _setPara(bidi, pText, text.Length, requested, IntPtr.Zero, ref status);
                    if (status > 0) return Degrade();

                    status = 0;
                    byte* resolved = _getLevels(bidi, ref status);
                    if (status > 0 || resolved == null) return Degrade();

                    Marshal.Copy((IntPtr)resolved, levels, 0, text.Length);
                    return (levels, (_getParaLevel(bidi) & 1) == 1);
                }
            }
            finally
            {
                _close(bidi);
            }
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return Degrade();
        }
    }

    /// <summary>
    /// UAX #9 P2/P3 in miniature, for the path where ICU is unavailable: the first
    /// strong character decides, and text with none reads left to right. The real
    /// rule skips isolate runs, which this does not — it is a degraded path, and
    /// its answer only has to be better than always guessing Ltr.
    /// </summary>
    private static bool FirstStrongIsRtl(string text)
    {
        foreach (char c in text)
        {
            // Hebrew, Arabic, Syriac, Thaana, N'Ko, Samaritan and the Arabic
            // presentation forms — the strong-RTL blocks of the BMP.
            if ((c >= '֐' && c <= 'ࣿ') || (c >= 'יִ' && c <= '﷿')
                || (c >= 'ﹰ' && c <= '﻿'))
                return true;
            if (char.IsLetter(c)) return false;   // a strong LTR letter settles it
        }
        return false;
    }

    private delegate IntPtr ubidi_open_delegate();
    private unsafe delegate void ubidi_setPara_delegate(
        IntPtr bidi, char* text, int length, byte paraLevel, IntPtr embeddingLevels, ref int status);
    private unsafe delegate byte* ubidi_getLevels_delegate(IntPtr bidi, ref int status);
    private delegate byte ubidi_getParaLevel_delegate(IntPtr bidi);
    private delegate void ubidi_close_delegate(IntPtr bidi);
}
