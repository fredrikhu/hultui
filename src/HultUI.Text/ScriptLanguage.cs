using System.Collections.Concurrent;
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
/// every script ICU knows is covered without a table here. Han resolves to <c>zh</c>,
/// the CLDR default; Japanese kana and Hangul resolve to <c>ja</c> and <c>ko</c>.</para>
/// </summary>
public static class ScriptLanguage
{
    // UScriptCode values with no language of their own: punctuation, digits and symbols
    // (Zyyy), combining marks that take their base's script (Zinh), and unassigned (Zzzz).
    private const int Common = 0, Inherited = 1, Unknown = 103;

    private static readonly ConcurrentDictionary<int, string?> ByScript = new();
    private static uscript_getScript_delegate? _getScript;
    private static uscript_getShortName_delegate? _getShortName;
    private static uloc_addLikelySubtags_delegate? _addLikelySubtags;
    private static bool _unavailable;

    /// <summary>
    /// The likely language of <paramref name="codepoint"/>'s script, or null when the
    /// character belongs to no one script or ICU cannot say. Null means "no hint", which
    /// is what fallback did before, so a failure here costs quality and nothing else.
    /// </summary>
    public static string? ForCodepoint(int codepoint)
    {
        if (_unavailable) return null;
        try
        {
            _getScript ??= IcuLibrary.GetDelegate<uscript_getScript_delegate>("uscript_getScript");
            int status = 0;
            int script = _getScript(codepoint, ref status);
            if (status > 0 || script is < 0 or Common or Inherited or Unknown) return null;
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
        string tag = Encoding.ASCII.GetString(output, Math.Min(length, 64));
        string language = tag.Split('_', '-')[0];
        return language is "" or "und" ? null : language;
    }

    private delegate int uscript_getScript_delegate(int codepoint, ref int status);
    private delegate IntPtr uscript_getShortName_delegate(int script);
    private unsafe delegate int uloc_addLikelySubtags_delegate(
        byte* localeId, byte* maximized, int capacity, ref int status);
}
