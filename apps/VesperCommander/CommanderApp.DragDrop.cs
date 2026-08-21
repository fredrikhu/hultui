using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Platform;

namespace VesperCommander;

public partial class CommanderApp
{
    // ── Desktop drag & drop ──────────────────────────────────────────────
    // Both directions, and both are the *same* OLE drag as far as the OS is concerned:
    //
    //   out  — pressing and dragging an entry hands the selection to the desktop as a file
    //          drag (Window.DragFilesOut). Explorer, an editor, a mail composer, anything
    //          that takes CF_HDROP, accepts it.
    //   in   — a drag from any application (including Vesper's own, dropped on the other
    //          pane) arrives as the framework's OS-drop events, and lands in whichever
    //          folder is under the cursor.
    //
    // Shift decides what a drop means: held, it moves; otherwise it copies. Deliberately
    // not Explorer's same-volume-means-move rule — a gesture whose destructiveness depends
    // on which disk you happen to be pointing at is a poor thing to have to think about.
    // The modifier is read at the moment of the drop, not the moment the drag began.

    /// <summary>The folder a hovering drag would land in, and the pane showing it — drawn
    /// highlighted while the drag is over it. Null when no drag is in flight.</summary>
    private (char Side, string Dir)? _dropTarget;

    /// <summary>Whether the hovering drag would move rather than copy. Tracked separately so
    /// pressing or releasing Shift mid-drag re-renders the hint without a pointer move.</summary>
    private bool _dropWouldMove;

    private static bool DropIsMove(KeyModifiers mods) => mods.HasFlag(KeyModifiers.Shift);

    /// <summary>True where the pane's folder can take a drop at all. A locked tab, a fed-in
    /// result set (not a place) and an archive that can't be written to all refuse.</summary>
    private bool CanDropInto(char side) =>
        !ActiveTab(side).Locked && ResultsOf(side) == null
        && (!InArchive(side) || ArchiveAddBlock(Dir(side)) == null);

    // ── Receiving ────────────────────────────────────────────────────────

    /// <summary>Make <paramref name="el"/> a drop target for <paramref name="dir"/>. Used for
    /// the pane as a whole (its own folder) and for each folder row inside it (that folder) —
    /// the row's handler stops the event so the more specific target wins over the pane.</summary>
    private void AttachDropTarget<T>(T el, char side, string dir, bool isRow) where T : Element
    {
        el.OnDragOver(e =>
        {
            if (!e.DragData.HasFiles || !CanDropInto(side)) return;
            e.Accepted = true;
            if (isRow) e.StopPropagation = true;

            bool move = DropIsMove(e.Modifiers);
            if (_dropTarget is { } t && t.Side == side && PathEquals(t.Dir, dir) && move == _dropWouldMove) return;
            _dropTarget = (side, dir);
            _dropWouldMove = move;
            MarkDirty();
        });

        // DragLeave is direct rather than bubbling, so it fires for the exact element the
        // pointer left. Only clear the highlight when the drag left *this* target — moving
        // from a folder row up onto its own pane fires the row's leave after the pane's enter.
        el.OnDragLeave(_ =>
        {
            if (_dropTarget is not { } t || t.Side != side || !PathEquals(t.Dir, dir)) return;
            _dropTarget = null;
            MarkDirty();
        });

        el.OnDrop(e =>
        {
            if (isRow) e.StopPropagation = true;
            _dropTarget = null;
            if (!e.DragData.HasFiles) { MarkDirty(); return; }
            DropPaths(side, dir, e.DragData.Files, DropIsMove(e.Modifiers));
        });
    }

    /// <summary>True while a drag is hovering over <paramref name="dir"/> in
    /// <paramref name="side"/> — the pane and its folder rows paint a ring for it.</summary>
    private bool IsDropHighlighted(char side, string dir) =>
        _dropTarget is { } t && t.Side == side && PathEquals(t.Dir, dir);

    /// <summary>Copy (or move) dropped paths into <paramref name="dir"/>. Sources already in
    /// that folder are dropped from the batch — a drag back onto where it started is a no-op,
    /// not a folder full of "file (2)".</summary>
    private void DropPaths(char side, string dir, IReadOnlyList<string> paths, bool move)
    {
        _active = side;
        if (!CanDropInto(side))
        {
            _notice = ActiveTab(side).Locked ? "Tab is locked (read-only) — can't drop here"
                : ResultsOf(side) != null ? "Results are not a folder — nothing can be dropped in"
                : "This archive can't be written to";
            MarkDirty();
            return;
        }

        bool intoArchive = ArchivePath.InArchive(dir);
        var items = new List<FileEntry>();
        foreach (var p in paths)
        {
            if (!FileOps.Exists(p)) continue;
            // An archive's members are never the dropped paths' own folder, so the
            // same-folder check has nothing to say about a drop going into one.
            if (!intoArchive && PathEquals(System.IO.Path.GetDirectoryName(p) ?? "", dir)) continue;
            // Dropping a folder into itself (or into its own subtree) would recurse forever.
            if (Directory.Exists(p) && IsUnderOrEqual(dir, p)) continue;
            items.Add(FileOps.EntryFromPath(p));
        }
        if (items.Count == 0)
        {
            _notice = "Nothing to drop there — they are already in that folder";
            MarkDirty();
            return;
        }

        if (intoArchive)
        {
            StartArchiveAdd(side, dir, items, move);
            return;
        }

        var op = new TransferOp(items) { IsMove = move, Source = side, DestDir = dir };
        _op = op;
        _progressOpen = true;
        MarkDirty();
        System.Threading.Tasks.Task.Run(() => RunTransfer(op));
    }

    /// <summary>True when <paramref name="candidate"/> is <paramref name="root"/> itself or
    /// sits somewhere below it.</summary>
    private static bool IsUnderOrEqual(string candidate, string root)
    {
        if (PathEquals(candidate, root)) return true;
        var rooted = root.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar)
            + System.IO.Path.DirectorySeparatorChar;
        return candidate.StartsWith(rooted, OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    // ── Sending ──────────────────────────────────────────────────────────

    /// <summary>True where the platform can start a drag other applications will see. Where
    /// it is false, entries simply are not draggable rather than starting one that goes
    /// nowhere.
    ///
    /// <para>Cached: every row asks on every build, and the answer is a property of the
    /// process (its OS, and whether its entry point is STA) that cannot change while it
    /// runs — so a listing of ten thousand files must not pay for the lookup ten thousand
    /// times a frame.</para></summary>
    private bool? _canDragOut;
    private bool CanDragOut => _canDragOut ??= _window.CanDragFilesOut;

    /// <summary>
    /// Begin an OS-wide drag of the pane's selection (or of <paramref name="e"/> alone when
    /// nothing is marked). Called from the entry's DragStart handler, which is where the
    /// framework has decided a drag is under way and the button is still down.
    /// </summary>
    /// <remarks>
    /// <para>The drag offers Copy and Link, never Move. Move would oblige us to delete the
    /// originals on the strength of a DWORD the target returned, with no way to tell an
    /// honest Move from a target that reported one after failing — and a drag that
    /// occasionally eats files is much worse than one that only ever copies them. F6 moves.</para>
    /// <para>Archive members have no path to hand over, so a drag from inside an archive says
    /// so rather than dragging a VFS string no other application could open. F5 extracts.</para>
    /// </remarks>
    private void StartFileDragOut(char side, FileEntry e)
    {
        if (!CanDragOut) return;
        _active = side;

        var targets = OperationTargets(side);
        // Dragging an entry outside the marked set drags that entry, not the marks — the same
        // rule the right-click menu follows, and the one every file manager uses.
        if (targets.Count == 0 || !targets.Any(t => PathEquals(t.FullPath, e.FullPath)))
            targets = [e];

        if (targets.Any(t => ArchivePath.InArchive(t.FullPath)))
        {
            _notice = "Archive members can't be dragged out — use F5 to extract";
            // Deferred: this runs inside the DragStart dispatch, and MarkDirty reconciles
            // synchronously — rebuilding here would detach the element the drag just started
            // from, leaving the framework to ghost a node that is no longer in the tree.
            HultUI.Application.InvokeOnMainThread(MarkDirty);
            return;
        }

        var paths = targets.Where(t => !t.IsParent).Select(t => t.FullPath).ToList();
        if (paths.Count == 0) return;

        // Blocks until the drop resolves: the OS owns the pointer for the whole gesture and
        // no frame renders meanwhile, which is what every native outbound drag does.
        var performed = _window.DragFilesOut(paths, DragEffect.Copy | DragEffect.Link);

        // The drop may well have been into our own other pane, which runs its own transfer —
        // so refresh both sides and say nothing about a copy this app did not perform.
        if (performed != DragEffect.None) { _genL++; _genR++; }
        HultUI.Application.InvokeOnMainThread(MarkDirty);
    }
}
