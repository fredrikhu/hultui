using HultUI;

namespace VesperCommander;

public partial class CommanderApp
{
    // ── Elevated retry ───────────────────────────────────────────────────
    // Vesper runs unprivileged. When an operation fails purely because the user
    // lacks rights (FileOps tags those errors — see FileOps.IsDenied), we offer to
    // redo just the failed items through an elevated copy of this executable; see
    // Elevation for why that means a second process. Nothing is ever elevated
    // silently: every batch goes through this confirm, so the OS prompt the user
    // sees always follows an action they asked for.

    /// <summary>True while an elevated batch is running (the worker is up, or the OS
    /// consent prompt is showing) — gates a second concurrent request.</summary>
    private bool _elevating;

    /// <summary>Offer an elevated retry for <paramref name="job"/>. <paramref name="what"/>
    /// describes the batch ("Copy 3 items"); <paramref name="onDone"/> runs on the UI thread
    /// after the worker finishes, and <paramref name="onCancel"/> when the user declines the
    /// offer. No-op (returns false) when elevation can't help — already elevated, or
    /// unsupported on this OS — so a caller holding resources for the retry can release them.</summary>
    private bool OfferElevation(ElevatedJob job, string what, Action? onDone = null, Action? onCancel = null)
    {
        if (job.Items.Count == 0 || _elevating || !Elevation.IsSupported) return false;

        int n = job.Items.Count;
        string subject = n == 1 ? $"\"{System.IO.Path.GetFileName(job.Items[0].Source)}\"" : $"{n} items";
        _confirm = new ConfirmSpec(
            "Access denied",
            $"{what} failed for {subject} — you don't have permission.\n"
                + $"Retry as {Elevation.AdminNoun}? Your system will ask you to confirm.",
            $"Retry as {Elevation.AdminNoun}", Danger: false,
            () => { _confirm = null; RunElevated(job, onDone); },
            OnCancel: onCancel);
        MarkDirty();
        return true;
    }

    /// <summary>Hand the job to the elevated worker on a background thread (the wait spans
    /// the OS consent prompt, which can take as long as the user does), then report.</summary>
    private void RunElevated(ElevatedJob job, Action? onDone)
    {
        _elevating = true;
        _notice = $"Waiting for {Elevation.AdminNoun} approval…";
        MarkDirty();

        System.Threading.Tasks.Task.Run(() =>
        {
            var result = Elevation.Run(job, out var launchError);
            Application.InvokeOnMainThread(() =>
            {
                _elevating = false;
                _notice = result != null
                    ? ElevatedSummary(job, result)
                    : launchError == null
                        ? "Elevation cancelled"                        // the user declined the prompt
                        : $"Couldn't elevate: {launchError}";
                _genL++; _genR++;          // the worker changed the filesystem behind our back
                onDone?.Invoke();
                MarkDirty();
            });
        });
    }

    private static string ElevatedSummary(ElevatedJob job, ElevatedResult result)
    {
        string verb = job.Verb switch
        {
            "copy" => "Copied", "move" => "Moved", "delete" => "Deleted",
            "rename" => "Renamed", "mkdir" => "Created", _ => "Completed",
        };
        if (result.Failed == 0) return $"{verb} {result.Succeeded} item(s) as {Elevation.AdminNoun}";
        var first = result.Errors.FirstOrDefault(e => e != null);
        return $"{verb} {result.Succeeded}, {result.Failed} still failed"
            + (first != null ? $": {FileOps.Clean(first)}" : "");
    }

    /// <summary>Build a job from (source, target) pairs.</summary>
    private static ElevatedJob Job(string verb, IEnumerable<(string Source, string Target, bool IsDir)> items,
        bool overwrite = false, bool recycle = false) =>
        new()
        {
            Verb = verb,
            Overwrite = overwrite,
            Recycle = recycle,
            Items = items.Select(i => new ElevatedItem { Source = i.Source, Target = i.Target, IsDir = i.IsDir }).ToList(),
        };
}
