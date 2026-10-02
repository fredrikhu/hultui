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

    /// <summary>Something an application's developer should hear of, that is not a failure.</summary>
    public static void Note(string text)
    {
        try { Console.Error.WriteLine($"HultUI.WebView.Cef: {text}"); }
        catch { }
    }
}
