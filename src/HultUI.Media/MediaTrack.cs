using System.Globalization;

namespace HultUI.Media;

/// <summary>An audio or subtitle track a backend found in the open media.</summary>
/// <param name="Id">The backend's identifier, passed back to select the track.</param>
/// <param name="Title">What to show — see <see cref="Describe"/>, which is how a backend
/// builds it from what the file says.</param>
/// <param name="Language">An ISO 639 code when the media says, otherwise null.</param>
public sealed record MediaTrack(int Id, string Title, string? Language = null)
{
    /// <summary>
    /// What to call a track, given what the file says about it. The language leads,
    /// because that is what someone opening the menu is choosing between; the embedded
    /// title follows as the detail that separates two tracks of the same language.
    ///
    /// <para>A title alone is not enough: a file whose tracks are both titled "Stereo"
    /// — one English, one Japanese, which is how most dubbed releases are muxed — gives a
    /// menu of two identical entries, and the one piece of information that would tell
    /// them apart is the one the file did record.</para>
    /// </summary>
    /// <param name="title">The track's embedded title, or null.</param>
    /// <param name="language">Its ISO 639 language code, or null.</param>
    /// <param name="id">The backend's track id, used when the file says nothing at all.</param>
    public static string Describe(string? title, string? language, int id)
    {
        title = string.IsNullOrWhiteSpace(title) ? null : title.Trim();
        var name = LanguageName(language);

        if (name is null) return title ?? $"Track {id}";
        if (title is null) return name;
        // A track titled "English", or "eng", says nothing the language has not already
        // said — repeating it reads as a stutter.
        if (title.Equals(name, StringComparison.OrdinalIgnoreCase) ||
            title.Equals(language, StringComparison.OrdinalIgnoreCase))
            return name;
        return $"{name} · {title}";
    }

    /// <summary>
    /// The English name of an ISO 639 language code — "jpn" or "ja" to "Japanese" — or
    /// null when the code is missing, means "undetermined", or is not one the runtime
    /// knows. A track whose code means nothing here keeps its own title: a private
    /// language tag shown raw would name the track after something nobody reads.
    /// </summary>
    public static string? LanguageName(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        code = code.Trim();
        // What ffmpeg writes when a muxer recorded no language; it is not a language.
        if (code.Equals("und", StringComparison.OrdinalIgnoreCase) ||
            code.Equals("unknown", StringComparison.OrdinalIgnoreCase)) return null;

        return Names.TryGetValue(code, out var name) ? name : null;
    }

    /// <summary>ISO 639-2/B to /T, the twenty codes where the two lists differ.</summary>
    private static readonly (string B, string T)[] BibliographicCodes =
    [
        ("alb", "sqi"), ("arm", "hye"), ("baq", "eus"), ("bur", "mya"), ("chi", "zho"),
        ("cze", "ces"), ("dut", "nld"), ("fre", "fra"), ("geo", "kat"), ("ger", "deu"),
        ("gre", "ell"), ("ice", "isl"), ("mac", "mkd"), ("mao", "mri"), ("may", "msa"),
        ("per", "fas"), ("rum", "ron"), ("slo", "slk"), ("tib", "bod"), ("wel", "cym"),
    ];

    /// <summary>
    /// Code to English name, built once from the runtime's neutral cultures. Also carries
    /// the ISO 639-2/B codes, which are what a Matroska file usually holds and which the
    /// runtime does not know: <c>ThreeLetterISOLanguageName</c> is the /T form, so "ger"
    /// and "fre" would otherwise find nothing.
    /// </summary>
    private static readonly Dictionary<string, string> Names = BuildNames();

    private static Dictionary<string, string> BuildNames()
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // Under invariant globalization this comes back empty; a track then keeps its own
        // title rather than being named after a code nobody reads.
        foreach (var culture in CultureInfo.GetCultures(CultureTypes.NeutralCultures))
        {
            if (culture.Equals(CultureInfo.InvariantCulture)) continue;
            var english = culture.EnglishName;
            if (string.IsNullOrEmpty(english) || english.StartsWith("Unknown", StringComparison.Ordinal)) continue;
            // "Chinese (Simplified)" and its kind: the script is not what a track menu is
            // choosing between, and the plain name is already in the list.
            int paren = english.IndexOf(" (", StringComparison.Ordinal);
            if (paren > 0) english = english[..paren];

            names.TryAdd(culture.TwoLetterISOLanguageName, english);
            names.TryAdd(culture.ThreeLetterISOLanguageName, english);
        }

        foreach (var (b, t) in BibliographicCodes)
            if (names.TryGetValue(t, out var english)) names.TryAdd(b, english);

        return names;
    }
}
