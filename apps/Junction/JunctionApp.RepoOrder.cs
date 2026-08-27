using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using Junction.Theme;

namespace Junction;

/// <summary>
/// Dragging repositories into the order you want them in.
///
/// <para>The sidebar list is the order they were added in, which is the order nobody
/// chose. It is also the order the digit shortcuts follow, so moving a repository is
/// really about deciding what <c>Ctrl+1</c> means.</para>
/// </summary>
public partial class JunctionApp
{
    /// <summary>The key the sidebar's own drags carry. Namespaced because the same
    /// element tree also receives OS file drops, and the two must never be confused for
    /// one another.</summary>
    private const string RepoDragKey = "junction/repo";

    // Which repository is being dragged, and where releasing would put it. The index is
    // a gap between rows, so it runs 0..Count inclusive — Count means "after the last".
    private string? _repoDragPath;
    private int _repoDropIndex = -1;

    /// <summary>Makes a sidebar repository row draggable and a drop target for the other
    /// rows, and returns it wrapped with the insertion marker when one belongs above it.</summary>
    private Element DraggableRepoRow(string path, int index)
    {
        var row = RepoRow(path);
        bool dragging = _repoDragPath != null && PathEquals(path, _repoDragPath);
        // The row being dragged dims rather than disappearing: taking it out of the list
        // would make every other row jump the moment the drag started, and then the
        // insertion marker would be pointing at a list that had already moved.
        if (dragging) row.Opacity(0.4f);
        row.Key = "repo:" + path + (dragging ? "#drag" : "");

        row.Draggable();
        row.On<DragStartEvent>(e =>
        {
            e.DragData.Set(RepoDragKey, path);
            _repoDragPath = path;
            _repoDropIndex = -1;
            MarkDirty();
        });
        row.On<DragEndEvent>(_ => EndRepoDrag());

        // Every row is also a target: which gap you are aiming at is the half of the row
        // the pointer is in, which is how every list that reorders by drag behaves.
        row.On<DragOverEvent>(e =>
        {
            if (!e.DragData.Has(RepoDragKey)) return;
            e.Accepted = true;
            var box = e.CurrentTarget.Layout.AbsoluteBorderBox;
            int gap = e.Position.Y < box.Y + box.Height / 2f ? index : index + 1;
            if (gap == _repoDropIndex) return;
            _repoDropIndex = gap;
            MarkDirty();
        });
        row.On<DragLeaveEvent>(_ => { });   // the next DragOver sets the gap; nothing to undo
        row.On<DropEvent>(e =>
        {
            if (!e.DragData.TryGet<string>(RepoDragKey, out var moved) || moved == null) return;
            var box = e.CurrentTarget.Layout.AbsoluteBorderBox;
            // Re-derived from the drop position rather than trusting _repoDropIndex: the
            // pointer can move between the last DragOver and the release, and the marker
            // the user saw last is not necessarily where they let go.
            MoveRepo(moved, e.Position.Y < box.Y + box.Height / 2f ? index : index + 1);
        });

        if (_repoDropIndex != index) return row;
        return Flex.Column(gap: 0).Children(RepoDropMarker(), row);
    }

    /// <summary>The gap after the last row, so a repository can be dragged to the end.
    /// A zero-height element cannot be hit, hence the few pixels of margin the list
    /// already leaves under itself doing double duty.</summary>
    private Element RepoDropTail(int count)
    {
        var tail = Box.New().Height(10).FlexShrink(0);
        tail.On<DragOverEvent>(e =>
        {
            if (!e.DragData.Has(RepoDragKey)) return;
            e.Accepted = true;
            if (_repoDropIndex == count) return;
            _repoDropIndex = count;
            MarkDirty();
        });
        tail.On<DropEvent>(e =>
        {
            if (e.DragData.TryGet<string>(RepoDragKey, out var moved) && moved != null)
                MoveRepo(moved, count);
        });
        return _repoDropIndex == count
            ? Flex.Column(gap: 0).Children(RepoDropMarker(), tail)
            : tail;
    }

    private static Element RepoDropMarker() =>
        Box.New().Height(2).FlexShrink(0)
            .Margin(new Thickness(2, 4, 2, 4))
            .CornerRadius(1f)
            .Background(Nocturne.Accent);

    /// <summary>
    /// Moves a repository to <paramref name="gap"/> — the position <i>between</i> sidebar
    /// rows, counted before the move.
    /// </summary>
    /// <remarks>
    /// <para>The gap counts the rows on screen, and those are not the workspace's stored
    /// list: a repository's linked worktrees are nested under it and hold no place of their
    /// own. So the gap is resolved through the row it lands in front of, and <i>that</i>
    /// row's position in the stored list is the real target — "before the third repository"
    /// has to keep meaning that whatever else the list holds in between.</para>
    /// <para>Removing first would shift every later position down by one, so the target is
    /// adjusted for that here rather than at each of the two call sites.</para>
    /// </remarks>
    private void MoveRepo(string path, int gap)
    {
        EndRepoDrag();
        if (ActiveWorkspace is not { } ws) return;

        int from = ws.RepoPaths.FindIndex(p => PathEquals(p, path));
        if (from < 0) return;

        var rows = SidebarRepos;
        int to = gap >= rows.Count
            ? ws.RepoPaths.Count
            : ws.RepoPaths.FindIndex(p => PathEquals(p, rows[gap]));
        if (to < 0) return;
        if (to > from) to--;
        to = Math.Clamp(to, 0, ws.RepoPaths.Count - 1);
        if (to == from) return;

        ws.RepoPaths.RemoveAt(from);
        ws.RepoPaths.Insert(to, path);
        Persist();
        // The digit shortcuts index into the sidebar's list, so the order is worth a word —
        // and the number has to be the row's, counted the same way the gap was.
        int shown = IndexOfPath(rows, path);
        if (shown >= 0 && gap > shown) shown = gap - 1; else if (shown >= 0) shown = gap;
        var name = System.IO.Path.GetFileName(path.TrimEnd('/', System.IO.Path.DirectorySeparatorChar));
        Flash(shown >= 0 ? $"Moved {name} to #{shown + 1}" : $"Moved {name}");
        Rerender();
    }

    private void EndRepoDrag()
    {
        if (_repoDragPath == null && _repoDropIndex < 0) return;
        _repoDragPath = null;
        _repoDropIndex = -1;
        MarkDirty();
    }
}
