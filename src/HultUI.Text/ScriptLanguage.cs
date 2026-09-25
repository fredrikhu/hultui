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
    public static string? ForCodepoint(int codepoint) => ForCodepoint(codepoint, CultureInfo.CurrentUICulture);

    /// <inheritdoc cref="ForCodepoint(int)"/>
    /// <param name="culture">Whose reading conventions decide Han.</param>
    public static string? ForCodepoint(int codepoint, CultureInfo culture)
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
