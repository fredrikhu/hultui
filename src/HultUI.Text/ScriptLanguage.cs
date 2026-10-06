using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace HultUI.Text;

/// <summary>
/// The language a character most likely belongs to, as a BCP 47 tag — the hint font
/// fallback needs to choose a face <i>designed</i> for a script rather than any face that
/// happens to contain it.
///
/// <para>Without it fontconfig answers a bare "who has U+05D0" with the first font that
/// covers the codepoint, which on Fedora was Liberation Mono: Hebrew in a sans-serif UI
/// came out monospaced although Noto Sans Hebrew was installed. The same query with
/// <c>he</c> returns Noto Sans Hebrew.</para>
///
/// <para>Script comes from ICU (<c>uscript_getScript</c>), and the language from CLDR's
/// likely-subtags data (<c>uloc_addLikelySubtags("und_Hebr")</c> → <c>he_Hebr_IL</c>), so
/// every script ICU knows is covered without a table here. Han is the exception and
/// follows the reader's culture instead; Japanese kana and Hangul resolve to <c>ja</c> and
/// <c>ko</c> on their own.</para>
/// </summary>
public static class ScriptLanguage
{
    // UScriptCode values with no language of their own: punctuation, digits and symbols
    // (Zyyy), combining marks that take their base's script (Zinh), and unassigned (Zzzz).
    private const int Common = 0, Inherited = 1, Unknown = 103;
    private const int Han = 17;

    private static readonly ConcurrentDictionary<int, string?> ByScript = new();
    private static uscript_getScript_delegate? _getScript;
    private static uscript_getShortName_delegate? _getShortName;
    private static uloc_addLikelySubtags_delegate? _addLikelySubtags;
    private static bool _unavailable;

    // The last culture's Han tag. Every Han character asks, and the culture almost never
    // changes, so one reference comparison replaces parsing its name per character.
    private static (CultureInfo Culture, string Tag)? _han;

    /// <summary>
    /// The likely language of <paramref name="codepoint"/>'s script, or null when the
    /// character belongs to no one script or ICU cannot say. Null means "no hint", which
    /// is what fallback did before, so a failure here costs quality and nothing else.
    ///
    /// <para>Han is the exception to "the script decides": the same ideograph is drawn
    /// differently in Simplified Chinese, Traditional Chinese, Japanese and Korean, and
    /// only the reader can say which is right. So Han follows
    /// <see cref="CultureInfo.CurrentUICulture"/> — see <see cref="HanLanguage"/>.</para>
    /// </summary>
    /// <param name="hanContext">The language the surrounding text settles for Han, from
    /// <see cref="CjkContextOf"/>. It wins over the culture: kanji in a sentence with kana are
    /// Japanese whoever reads them.</param>
    public static string? ForCodepoint(int codepoint, string? hanContext = null)
        => ForCodepoint(codepoint, CultureInfo.CurrentUICulture, hanContext);

    /// <inheritdoc cref="ForCodepoint(int, string?)"/>
    /// <param name="culture">Whose reading conventions decide Han when the text does not.</param>
    public static string? ForCodepoint(int codepoint, CultureInfo culture, string? hanContext = null)
    {
        if (_unavailable) return null;
        try
        {
            _getScript ??= IcuLibrary.GetDelegate<uscript_getScript_delegate>("uscript_getScript");
            int status = 0;
            int script = _getScript(codepoint, ref status);
            if (status > 0 || script is < 0 or Common or Inherited or Unknown) return null;
            if (script == Han)
            {
                if (hanContext is not null) return hanContext;
                if (_han is { } h && ReferenceEquals(h.Culture, culture)) return h.Tag;
                string tag = HanLanguage(culture);
                _han = (culture, tag);
                return tag;
            }
            return ByScript.GetOrAdd(script, LikelyLanguage);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            _unavailable = true;
            return null;
        }
    }

    /// <summary>
    /// What a paragraph's own text says its Han characters are: <c>ja</c> when it contains
    /// kana, <c>ko</c> when it contains Hangul, and null when it has neither — or both, which
    /// is no answer — leaving it to the culture.
    ///
    /// <para>Per paragraph, not per character or per segment, deliberately. Kana are the only
    /// thing that tells Japanese kanji from Chinese hanzi, and they are rarely adjacent to
    /// every kanji; and the pipeline itemizes line-break segments (often one ideograph each)
    /// to measure them, which must pick the same face the finished line is drawn in. So the
    /// whole paragraph decides once. A paragraph of kanji alone ("東京都") says nothing, and
    /// the culture still decides it.</para>
    /// </summary>
    public static string? CjkContextOf(ReadOnlySpan<char> text)
    {
        var (kana, hangul) = CjkContextChars(text);
        return kana == hangul ? null : kana ? "ja" : "ko";
    }

    /// <summary>Whether <paramref name="text"/> holds kana, and whether Hangul: all
    /// <see cref="CjkContextOf"/> reads of it.</summary>
    internal static (bool Kana, bool Hangul) CjkContextChars(ReadOnlySpan<char> text)
    {
        bool kana = false, hangul = false;
        foreach (char c in text)
        {
            if (c < '\u1100') continue;   // below every range: Latin text pays one compare a char
            if (c is >= '\u3040' and <= '\u30FF'      // Hiragana, Katakana
                  or >= '\u31F0' and <= '\u31FF'      // Katakana phonetic extensions
                  or >= '\uFF66' and <= '\uFF9F')     // halfwidth Katakana
                kana = true;
            else if (c is >= '\u1100' and <= '\u11FF' // Hangul Jamo
                  or >= '\u3130' and <= '\u318F'      // compatibility Jamo
                  or >= '\uA960' and <= '\uA97F'      // Jamo extended-A
                  or >= '\uAC00' and <= '\uD7FF'      // syllables, Jamo extended-B
                  or >= '\uFFA0' and <= '\uFFDC')     // halfwidth Hangul
                hangul = true;
        }
        return (kana, hangul);
    }

    private static unsafe string? LikelyLanguage(int script)
    {
        _getShortName ??= IcuLibrary.GetDelegate<uscript_getShortName_delegate>("uscript_getShortName");
        _addLikelySubtags ??= IcuLibrary.GetDelegate<uloc_addLikelySubtags_delegate>("uloc_addLikelySubtags");

        string? code = Marshal.PtrToStringAnsi(_getShortName(script));   // "Hebr"
        if (string.IsNullOrEmpty(code)) return null;

        byte[] input = Encoding.ASCII.GetBytes($"und_{code}\0");
        byte* output = stackalloc byte[64];
        int status = 0;
        int length;
        fixed (byte* pInput = input)
            length = _addLikelySubtags(pInput, output, 64, ref status);
        if (status > 0 || length <= 0) return null;

        // "he_Hebr_IL" → "he". A likely-subtags answer that is still "und" has no language.
        // Chinese keeps its region ("zh_Bopo_TW" → "zh-TW"): fontconfig ignores a bare "zh".
        string tag = Encoding.ASCII.GetString(output, Math.Min(length, 64));
        string[] parts = tag.Split('_', '-');
        string language = parts[0];
        if (language == "zh") return ChineseTag(script: null, region: parts[^1]);
        return language is "" or "und" ? null : language;
    }

    /// <summary>
    /// The tag for Han in <paramref name="culture"/>: its own language when that is
    /// Chinese, Japanese or Korean, and Simplified Chinese — CLDR's default for the
    /// script — for everyone else.
    ///
    /// <para>The tags are the forms fontconfig understands, which are also valid BCP 47
    /// for DirectWrite and CoreText: <c>ja</c>, <c>ko</c>, and Chinese always with a
    /// region. Measured against Fedora's fontconfig for U+8AAA: <c>zh-CN</c>, <c>zh-TW</c>,
    /// <c>zh-HK</c> reach the SC, TC and HK faces, while <c>zh</c>, <c>zh-Hans</c>,
    /// <c>zh-Hant</c>, <c>zh-Hant-TW</c> and even <c>ko-KR</c> all fell through to the
    /// Japanese one.</para>
    /// </summary>
    private static string HanLanguage(CultureInfo culture)
    {
        string[] parts = culture.Name.Split('-');
        switch (parts[0])
        {
            case "ja": return "ja";
            case "ko": return "ko";
            case "zh":
                string? script = parts.FirstOrDefault(p => p.Length == 4);
                string? region = parts.Skip(1).FirstOrDefault(p => p.Length == 2);
                return ChineseTag(script, region);
            default: return "zh-CN";
        }
    }

    /// <summary>
    /// A Chinese tag with the region fontconfig needs. An explicit script wins over the
    /// region (<c>zh-Hans-HK</c> is Simplified, read in Hong Kong); without one, the region
    /// says which (<c>zh-TW</c>); with neither, Simplified.
    /// </summary>
    private static string ChineseTag(string? script, string? region) => script switch
    {
        "Hant" => region is "HK" or "MO" ? $"zh-{region}" : "zh-TW",
        "Hans" => region is "SG" ? "zh-SG" : "zh-CN",
        _ => region is "CN" or "SG" or "TW" or "HK" or "MO" ? $"zh-{region}" : "zh-CN",
    };

    private delegate int uscript_getScript_delegate(int codepoint, ref int status);
    private delegate IntPtr uscript_getShortName_delegate(int script);
    private unsafe delegate int uloc_addLikelySubtags_delegate(
        byte* localeId, byte* maximized, int capacity, ref int status);
}
