using static CefOsr.Native;

namespace CefOsr;

/// <summary>Loads one URL (CEFOSR_URL) and prints what its console says for 3 s.</summary>
internal static unsafe class Url
{
    public static int Run(Options o)
    {
        if (Host.Initialize([], o) != 1) return 1;
        Host.Pump(() => Host.ContextReady, 15000);
        Host.ConsoleMessage += (b, m, src, line) => Log("console: " + m);
        Host.CreateBrowser(Environment.GetEnvironmentVariable("CEFOSR_URL")!, sync: false);
        Host.Wait(3000);
        Q1.Shutdown();
        return 0;
    }
}
