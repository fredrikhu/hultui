namespace HultUI.WebView.Cef;

/// <summary>
/// Where the engine reports what went wrong inside a CEF callback. An exception must never
/// leave an <c>[UnmanagedCallersOnly]</c> method: it ends the process. Each callback catches,
/// says so here (standard error, beside CEF's own log), and answers CEF with its default.
/// </summary>
internal static class CefLog
{
    public static void Error(string where, Exception ex)
    {
        try { Console.Error.WriteLine($"HultUI.WebView.Cef: {where}: {ex}"); }
        catch { }   // nowhere left to say it
    }

    /// <summary><c>HULTUI_CEF_LOG=info</c> or <c>verbose</c>: the engine says what it chose, too.</summary>
    public static readonly bool Informing = Environment.GetEnvironmentVariable("HULTUI_CEF_LOG") is "info" or "verbose";

    /// <summary>What the engine chose (how frames travel, say), with <see cref="Informing"/>.</summary>
    public static void Info(string text)
    {
        if (Informing) Note(text);
    }

    /// <summary>Something an application's developer should hear of, that is not a failure.</summary>
    public static void Note(string text)
    {
        try { Console.Error.WriteLine($"HultUI.WebView.Cef: {text}"); }
        catch { }
    }
}
