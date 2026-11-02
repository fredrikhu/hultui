namespace HultUI;

/// <summary>
/// <c>HULTUI_FRAME_REQUESTS=&lt;directory&gt;</c>: a tool outside the process asks for a window's
/// frame (<see cref="Window.SaveFrame"/>) without any input to the window.
///
/// <para>The tool puts <c>&lt;name&gt;.request</c> in the directory, best written as another name
/// and renamed, so it is never read half written. Empty, it asks for the primary window; otherwise
/// its text is the title of the window to save. The frame lands beside it as
/// <c>&lt;name&gt;.png</c>, moved into place whole, and the request is deleted; when it cannot be
/// written, <c>&lt;name&gt;.error</c> says why. A request someone still has open (its writer, a
/// virus scanner looking at the new file) is tried again every 100 ms, for up to 5 s. Read when the application starts; unset, nothing
/// watches and nothing is paid.</para>
///
/// <para>With headless sessions side by side, each application watches: a title is served by the
/// session that has the window, and an empty request by the oldest session alive.</para>
/// </summary>
internal static class FrameRequests
{
    public const string Variable = "HULTUI_FRAME_REQUESTS";

    /// <summary>Watches <paramref name="directory"/> for requests to <paramref name="application"/>'s
    /// windows until disposed, or returns null when there is no directory to watch.</summary>
    public static IDisposable? Start(Application application, string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return null;
        try
        {
            Directory.CreateDirectory(directory);
            var watcher = new FileSystemWatcher(directory, "*.request")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
            };
            // A request is seen by its name arriving: written in place, or renamed to it.
            watcher.Created += (_, e) => Requested(application, e.FullPath);
            watcher.Renamed += (_, e) => Requested(application, e.FullPath);
            watcher.EnableRaisingEvents = true;
            return watcher;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // A diagnostic hook does not stop the application.
            Console.Error.WriteLine($"[hultui] {Variable}: cannot watch \"{directory}\": {ex.Message}");
            return null;
        }
    }

    /// <summary>How often, and how far apart, a request someone still holds open is tried again:
    /// its writer still writing, or a virus scanner looking at the file that just arrived.</summary>
    internal static int HeldTries = 50;   // a test shortens it
    private static readonly TimeSpan HeldRetry = TimeSpan.FromMilliseconds(100);

    /// <summary>On the watcher's thread: the window is picked on the UI thread, which owns the list.</summary>
    private static void Requested(Application application, string request, int tried = 0)
    {
        if (!request.EndsWith(".request", StringComparison.OrdinalIgnoreCase)) return;
        application.Post(() =>
        {
            if (!File.Exists(request)) return;   // a duplicate event for one already served
            string png = Path.ChangeExtension(request, ".png");
            string error = Path.ChangeExtension(request, ".error");
            Task saved;
            try
            {
                // Read only once no one else has it open, so a request still being written is
                // not read half written, and the delete below is not refused.
                string title;
                using (var reader = new StreamReader(new FileStream(request, FileMode.Open, FileAccess.Read, FileShare.None)))
                    title = reader.ReadToEnd().Trim();
                Window? window;
                if (title.Length == 0)
                {
                    // Headless sessions side by side each watch: the oldest answers.
                    if (!application.IsOldestLive) return;
                    window = application.PrimaryWindow;
                }
                else
                {
                    window = application.Windows.FirstOrDefault(w => w.IsOpen && w.Title == title);
                    // Another session's window: left for its own watcher.
                    if (window is null && Application.AnyOpenWindowTitled(title)) return;
                    if (window is null) throw new InvalidOperationException($"No open window is titled \"{title}\".");
                }
                File.Delete(request);
                saved = window.SaveFrame(png);
            }
            catch (IOException) when (tried + 1 < HeldTries && File.Exists(request))
            {
                // Held by someone else (a sharing violation): tried again in a moment.
                _ = Task.Delay(HeldRetry).ContinueWith(_ => Requested(application, request, tried + 1), TaskScheduler.Default);
                return;
            }
            catch (Exception ex)
            {
                saved = Task.FromException(ex);
            }
            saved.ContinueWith(t =>
            {
                if (t.Exception is null) return;
                // The answer first: a request still held cannot be deleted, and that refusal
                // stopped the .error being written, so the tool waited for good.
                try
                {
                    File.WriteAllText(error + ".partial", t.Exception.GetBaseException().Message);
                    File.Move(error + ".partial", error, overwrite: true);
                }
                catch (IOException) { }
                try { File.Delete(request); }
                catch (IOException) { }
            }, TaskScheduler.Default);
        });
    }
}
