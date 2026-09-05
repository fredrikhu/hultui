using HultUI;

namespace VesperCommander;

/// <summary>
/// "Who has this file open?" — Ctrl+Shift+L, the palette, and the moment it is actually
/// wanted: a delete or a rename that just failed for being in use. The OS says "in use by
/// another process" and stops there; <see cref="FileHolders"/> asks which one, and the answer
/// goes in a dialog by program name and PID, so the user knows what to close.
/// </summary>
public partial class CommanderApp
{
    /// <summary>The command: ask about the marked set, or the cursor row.</summary>
    private void ShowHolders(char side)
    {
        if (!FileHolders.IsSupported) { Notice("This system can't say who has a file open"); return; }
        var items = OperationTargets(side).Where(e => !e.IsParent).ToList();
        if (items.Count == 0) return;
        if (items.Any(e => ArchivePath.InArchive(e.FullPath)))
        {
            // A member is bytes inside a file, not a file anything could hold; the archive
            // itself is what would be open.
            Notice("Archive members aren't open by anything — ask about the archive file instead");
            return;
        }
        var paths = items.Select(e => e.FullPath).ToList();
        string label = items.Count == 1 ? items[0].Name : $"{items.Count} items";
        LookUpHolders(paths, label, whenNone: $"Nothing has {label} open");
    }

    /// <summary>After a delete, rename or move failed with "in use": say by what.</summary>
    private void ReportInUse(List<string> paths, string verb)
    {
        string label = paths.Count == 1 ? System.IO.Path.GetFileName(paths[0]) : $"{paths.Count} items";
        if (!FileHolders.IsSupported)
        { Notice($"Couldn't {verb} {label} — another program has it open"); return; }
        LookUpHolders(paths, label,
            whenNone: $"Couldn't {verb} {label} — it was in use, though nothing seems to hold it now");
    }

    /// <summary>The lookup, off the UI thread — the Restart Manager walks every process, and
    /// a folder is asked about through everything in it. A notice while it runs, then either a
    /// notice (nobody) or the dialog (somebody).</summary>
    private void LookUpHolders(List<string> paths, string label, string whenNone)
    {
        Notice($"Asking who has {label} open…");
        Task.Run(() =>
        {
            var holders = FileHolders.Find(paths);
            Application.InvokeOnMainThread(() =>
            {
                if (holders is null) Notice($"Couldn't find out who has {label} open");
                else if (holders.Count == 0) Notice(whenNone);
                else ShowHoldersDialog(label, holders);
            });
        });
    }

    private const int MaxHoldersShown = 10;

    private void ShowHoldersDialog(string label, List<FileHolder> holders)
    {
        var lines = new List<string>();
        foreach (var h in holders.Take(MaxHoldersShown))
        {
            lines.Add($"{h.Name}  ·  PID {h.Pid}");
            // The confirm dialog sets a line with a separator in it in mono, one line,
            // ellipsized — which is exactly how a path wants to be shown.
            if (h.Path != null) lines.Add(h.Path);
        }
        if (holders.Count > MaxHoldersShown) lines.Add($"…and {holders.Count - MaxHoldersShown} more");

        string count = holders.Count == 1 ? "one program" : $"{holders.Count} programs";
        _notice = null;   // "Asking…" is answered
        _confirm = new ConfirmSpec($"{label} is open in {count}", string.Join('\n', lines),
            "Close", Danger: false,
            OnConfirm: () => { _confirm = null; MarkDirty(); },
            CancelLabel: null);
        MarkDirty();
    }

    /// <summary>One line for an inline error: the rename prompt stays open with the answer
    /// in it. Synchronous, because the prompt is — one file through the Restart Manager is
    /// quick enough to wait for.</summary>
    private static string InUseMessage(string path)
    {
        var holders = FileHolders.IsSupported ? FileHolders.Find([path]) : null;
        if (holders is not { Count: > 0 }) return "Another program has the file open";
        var named = string.Join(", ", holders.Take(3).Select(h => $"{h.Name} (PID {h.Pid})"));
        return holders.Count > 3
            ? $"Open in {named} and {holders.Count - 3} more"
            : $"Open in {named}";
    }
}
