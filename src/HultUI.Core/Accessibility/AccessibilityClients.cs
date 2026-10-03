namespace HultUI.Core.Accessibility;

/// <summary>
/// Whether assistive technology is listening: a screen reader, or a tool such as accerciser.
/// Reported by the platform bridge (on Linux, from <c>org.a11y.Status</c>); read by content
/// that is costly to describe, such as a web page, which keeps its tree off while nobody
/// would hear it.
/// </summary>
public static class AccessibilityClients
{
    private static bool _reported;
    private static bool? _forced = Environment.GetEnvironmentVariable("HULTUI_ACCESSIBILITY") switch
    {
        "1" => true,
        "0" => false,
        _ => null,
    };

    /// <summary>True while assistive technology listens, or while <see cref="Force"/> says so.</summary>
    public static bool Active => _forced ?? Volatile.Read(ref _reported);

    /// <summary>Raised when <see cref="Active"/> changes, on whichever thread changed it.</summary>
    public static event Action<bool>? ActiveChanged;

    /// <summary>
    /// Overrides what the platform reports: true or false, or null to follow it again. For
    /// tests and diagnosis; <c>HULTUI_ACCESSIBILITY=1</c> (or <c>0</c>) sets it at start.
    /// </summary>
    public static bool? Force
    {
        get => _forced;
        set
        {
            bool before = Active;
            _forced = value;
            if (Active != before) ActiveChanged?.Invoke(Active);
        }
    }

    /// <summary>For platform bridges: whether assistive technology listens now.</summary>
    public static void Report(bool listening)
    {
        bool before = Active;
        Volatile.Write(ref _reported, listening);
        if (Active != before) ActiveChanged?.Invoke(Active);
    }
}
