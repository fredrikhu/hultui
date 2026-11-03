using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Styling;
using Meridian.Core;
using Meridian.Theme;

namespace Meridian;

/// <summary>
/// Rename and delete for the collection's files — requests, folders, environments — so none
/// of them needs moving or removing outside the app. A delete goes to the OS's bin
/// (<see cref="IFileTrash"/>) after a confirmation that names what goes; where there is no bin,
/// or the bin refuses the path, the confirmation says the delete is permanent before making it.
/// </summary>
public partial class MeridianApp
{
    private readonly IFileTrash _trash;

    // ── Names ────────────────────────────────────────────────────────────
    // One set of rules for a new request or folder and a renamed one: the collection's name rules
    // (CollectionNames.Problem), and no clash in any letter case with what is already in the
    // folder (a clash on Windows and macOS, which a clone of the collection would meet). The
    // entry being renamed is no clash with itself, so a rename may change only the letter case.

    /// <summary>"get-charge.req" and " get-charge " both mean get-charge.</summary>
    private static string RequestNameFrom(string input) => CollectionNames.Stem(input, ".req");

    /// <param name="folder">Where the request is made, or is.</param>
    /// <param name="renaming">The request being renamed, or null for a new one.</param>
    private static string? RequestNameProblem(string folder, string input, string? renaming = null)
    {
        string name = RequestNameFrom(input);
        if (CollectionNames.Problem(name, "request") is { } problem) return problem;
        if (name.Equals("_folder", StringComparison.OrdinalIgnoreCase))
            return "_folder.req holds a folder's defaults; pick another name.";
        return CollectionNames.Taken(folder, name + ".req", Path.GetFileName(renaming)) is { } taken
            ? $"{taken} already exists here." : null;
    }

    /// <summary>"users/" and " users " both mean users.</summary>
    private static string FolderNameFrom(string input) => input.Trim().TrimEnd('/', '\\').TrimEnd();

    /// <param name="parent">Where the folder is made, or is.</param>
    /// <param name="renaming">The folder being renamed, or null for a new one.</param>
    private string? FolderNameProblem(string parent, string input, string? renaming = null)
    {
        string name = FolderNameFrom(input);
        if (CollectionNames.Problem(name, "folder") is { } problem) return problem;
        if (Relative(parent) == "." && name.Equals("envs", StringComparison.OrdinalIgnoreCase))
            return "envs/ holds the environments; pick another name.";
        return CollectionNames.Taken(parent, name, Path.GetFileName(renaming)) is { } taken
            ? $"{taken} already exists here." : null;
    }

    /// <summary>Moves a file or folder to a new name beside it. Null when it moved, else why not.
    /// A change of case alone goes through a name of its own first: Windows sees the target as
    /// the source and would refuse it.</summary>
    private static string? MoveTo(string from, string to)
    {
        bool dir = Directory.Exists(from);
        void Move(string a, string b) { if (dir) Directory.Move(a, b); else File.Move(a, b); }
        try
        {
            if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
            {
                string step = from + ".meridian-rename";
                Move(from, step);
                Move(step, to);
            }
            else
                Move(from, to);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return e.Message;
        }
    }

    private string Relative(string path) =>
        Path.GetRelativePath(_workspace!.Root, path).Replace('\\', '/');

    private static bool SamePath(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static bool IsUnder(string path, string dir) =>
        path.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    // ── Deleting: the bin, or permanently with a second word ─────────────

    /// <summary>What happens to <paramref name="what"/>, for the confirmation: "… moves to the
    /// Recycle Bin, where it can be restored", or that it is deleted for good.</summary>
    private string WhereItGoes(string what, bool plural = false) => _trash.Name is { } bin
        ? $"{what} {(plural ? "move" : "moves")} to the {bin}, where {(plural ? "they" : "it")} can be restored."
        : $"{what} {(plural ? "are" : "is")} deleted permanently. This can't be undone.";

    private string DeleteLabel => _trash.Name is null ? "Delete permanently" : "Delete";

    /// <summary>Sends <paramref name="path"/> to the bin and runs <paramref name="after"/> once it
    /// is gone. When the bin refuses it (another file system, a share without a bin) the path stays,
    /// and a second confirmation offers to delete it for good.</summary>
    private void Discard(string path, string what, Action after)
    {
        if (_trash.Delete(path) is not { } error)
        {
            after();
            if (_trash.Name is { } bin) Flash($"{what} moved to the {bin}");
            return;
        }
        if (_trash.Name is not { } name)
        {
            Flash($"Could not delete {what}: {error}");
            return;
        }
        OpenConfirm($"Delete {what} permanently?",
            $"{what} could not go to the {name}: {error} Delete it permanently instead? This can't be undone.",
            "Delete permanently", () =>
            {
                if (FileTrash.DeletePermanently(path) is { } failed) Flash($"Could not delete {what}: {failed}");
                else after();
            });
    }

    // ── Environments ─────────────────────────────────────────────────────

    private IReadOnlyList<(string, Action)> EnvironmentActions(string name) =>
    [
        ("Rename…", () => OpenRenameEnvironmentDialog(name)),
        ("Delete…", () => ConfirmDeleteEnvironment(name)),
    ];

    private void OpenRenameEnvironmentDialog(string name) =>
        OpenDialog($"Rename {name}.env", "new name — local, staging, ci", input => RenameEnvironment(name, input),
            input => EnvNameProblem(input, except: name), initial: name);

    /// <summary>Moves envs/&lt;old&gt;.env to the new name. Unsaved edits come along (the table
    /// keeps them under the new name), and the environment stays active if it was.</summary>
    internal void RenameEnvironment(string oldName, string input)
    {
        if (_workspace is null || _envFiles.FirstOrDefault(e => e.Name == oldName) is not { } env) return;
        if (EnvNameProblem(input, except: oldName) is { } problem) { Flash(problem); return; }   // the prompt refuses these first
        string name = EnvNameFrom(input);
        if (name == oldName) return;
        if (MoveTo(env.Path, Path.Combine(Path.GetDirectoryName(env.Path)!, name + ".env")) is { } error)
        {
            Flash($"Could not rename {oldName}.env: {error}");
            return;
        }
        // Before the rescan, which drops the documents of files it no longer finds, and picks
        // another environment when the active one has gone.
        if (_envDocs.Remove(oldName, out var doc)) _envDocs[name] = doc;
        if (_envName == oldName) _envName = name;
        RescanWorkspace();
        MarkDirty();
    }

    private void ConfirmDeleteEnvironment(string name)
    {
        if (_workspace is null || _envFiles.All(e => e.Name != name)) return;
        string message = WhereItGoes($"envs/{name}.env");
        if (_envDocs.GetValueOrDefault(name)?.Edited == true) message += " Its unsaved changes go with it.";
        if (name == _envName)
        {
            var next = _envFiles.Select(e => e.Name).Where(n => n != name).ToList();
            message += next.Count == 0 ? " No environment is left active."
                : $" {(next.Contains("local") ? "local" : next[0])} becomes the active environment.";
        }
        OpenConfirm($"Delete {name}.env?", message, DeleteLabel, () => DeleteEnvironment(name));
    }

    /// <summary>Deletes envs/&lt;name&gt;.env; when it was the active environment, the rescan picks
    /// the next as opening a collection does — local, else the first. The keyring keeps its secrets:
    /// they belong to the collection, not one environment, and the file can come back from the bin.</summary>
    internal void DeleteEnvironment(string name)
    {
        if (_envFiles.FirstOrDefault(e => e.Name == name) is not { } env) return;
        Discard(env.Path, $"{name}.env", () =>
        {
            _envDocs.Remove(name);
            RescanWorkspace();
            MarkDirty();
        });
    }

    // ── Requests ─────────────────────────────────────────────────────────

    private IReadOnlyList<(string, Action)> RequestActions(string path) =>
    [
        ("Rename…", () => OpenRenameRequestDialog(path)),
        ("Delete…", () => ConfirmDeleteRequest(path)),
    ];

    private void OpenRenameRequestDialog(string path)
    {
        string stem = Path.GetFileNameWithoutExtension(path);
        OpenDialog($"Rename {stem}.req", "file name — .req is added", input => RenameRequest(path, input),
            input => RequestNameProblem(Path.GetDirectoryName(path)!, input, renaming: path), initial: stem);
    }

    /// <summary>Renames a request's file. Its open tab follows, unsaved edits and all, and saves
    /// to the new name.</summary>
    internal void RenameRequest(string path, string input)
    {
        if (_workspace is null) return;
        if (RequestNameProblem(Path.GetDirectoryName(path)!, input, renaming: path) is { } problem) { Flash(problem); return; }
        string target = Path.Combine(Path.GetDirectoryName(path)!, RequestNameFrom(input) + ".req");
        if (target == path) return;
        if (MoveTo(path, target) is { } error)
        {
            Flash($"Could not rename {Path.GetFileName(path)}: {error}");
            return;
        }
        Retarget(p => SamePath(p, path) ? target : null);
        RescanWorkspace();
        MarkDirty();
    }

    private void ConfirmDeleteRequest(string path)
    {
        if (_workspace is null) return;
        string message = WhereItGoes(Relative(path));
        if (_tabs.FirstOrDefault(t => SamePath(t.Path, path)) is { } tab)
            message += tab.Dirty ? " It is open with unsaved changes, which go with it." : " Its tab closes.";
        OpenConfirm($"Delete {Path.GetFileName(path)}?", message, DeleteLabel, () => DeleteRequest(path));
    }

    internal void DeleteRequest(string path)
    {
        if (_workspace is null) return;
        Discard(path, Path.GetFileName(path), () =>
        {
            CloseTabsWhere(t => SamePath(t.Path, path));
            RescanWorkspace();
            MarkDirty();
        });
    }

    // ── Folders ──────────────────────────────────────────────────────────

    private IReadOnlyList<(string, Action)> FolderActions(CollectionFolder folder) =>
    [
        ("Rename…", () => OpenRenameFolderDialog(folder.Path)),
        ("Delete…", () => ConfirmDeleteFolder(folder)),
    ];

    private void OpenRenameFolderDialog(string path)
    {
        string name = Path.GetFileName(path);
        OpenDialog($"Rename {name}/", "folder name", input => RenameFolder(path, input),
            input => FolderNameProblem(Path.GetDirectoryName(path)!, input, renaming: path), initial: name);
    }

    /// <summary>Renames a folder, and everything in it moves along: its requests, its
    /// <c>_folder.req</c> defaults, any other file. Open tabs under it follow, and so does the
    /// Runner's scope.</summary>
    internal void RenameFolder(string path, string input)
    {
        if (_workspace is null) return;
        if (FolderNameProblem(Path.GetDirectoryName(path)!, input, renaming: path) is { } problem) { Flash(problem); return; }
        string target = Path.Combine(Path.GetDirectoryName(path)!, FolderNameFrom(input));
        if (target == path) return;
        if (MoveTo(path, target) is { } error)
        {
            Flash($"Could not rename {Path.GetFileName(path)}/: {error}");
            return;
        }
        Retarget(p => SamePath(p, path) ? target : IsUnder(p, path) ? target + p[path.Length..] : null);
        RescanWorkspace();
        MarkDirty();
    }

    private void ConfirmDeleteFolder(CollectionFolder folder)
    {
        if (_workspace is null) return;
        int requests = CountRequests(folder);
        int others;
        try
        {
            others = Directory.EnumerateFiles(folder.Path, "*", SearchOption.AllDirectories).Count() - requests;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            others = 0;
        }
        // _folder.req and anything not a request count as other files: all of it goes.
        string what = $"{Relative(folder.Path)}/ and the {requests} {(requests == 1 ? "request" : "requests")} in it";
        if (others > 0) what += $" (and {others} other {(others == 1 ? "file" : "files")})";
        string message = WhereItGoes(what, plural: true);
        var open = _tabs.Where(t => IsUnder(t.Path, folder.Path)).ToList();
        int dirty = open.Count(t => t.Dirty);
        if (dirty > 0)
            message += $" {dirty} of them {(dirty == 1 ? "is" : "are")} open with unsaved changes, which go with {(dirty == 1 ? "it" : "them")}.";
        else if (open.Count > 0)
            message += $" {(open.Count == 1 ? "Its open tab closes" : "Their open tabs close")}.";
        OpenConfirm($"Delete {folder.Name}/?", message, DeleteLabel, () => DeleteFolder(folder.Path));
    }

    private static int CountRequests(CollectionFolder folder) =>
        folder.Requests.Count + folder.Folders.Sum(CountRequests);

    internal void DeleteFolder(string path)
    {
        if (_workspace is null) return;
        Discard(path, Path.GetFileName(path) + "/", () =>
        {
            CloseTabsWhere(t => IsUnder(t.Path, path));
            if (_runScopePath is { } scope && (SamePath(scope, path) || IsUnder(scope, path))) _runScopePath = null;
            RescanWorkspace();
            MarkDirty();
        });
    }

    // ── What follows a moved path ────────────────────────────────────────

    /// <summary>Points everything that names a request or folder by path at where it moved:
    /// open tabs, the chain's last-matched runs, the Runner's scope. <paramref name="moved"/>
    /// maps an old path to its new one, or to null when it did not move.</summary>
    private void Retarget(Func<string, string?> moved)
    {
        foreach (var tab in _tabs)
            if (moved(tab.Path) is { } to) tab.Path = to;
        foreach (var (key, run) in _extractLastMatched.ToList())
            if (moved(key.Path) is { } to)
            {
                _extractLastMatched.Remove(key);
                _extractLastMatched[(to, key.Variable)] = run;
            }
        if (_runScopePath is { } scope && moved(scope) is { } newScope) _runScopePath = newScope;
    }

    /// <summary>Closes the tabs whose files went, stopping their sends and body work. The tab
    /// that was open stays open when it survived; otherwise its neighbour opens.</summary>
    private void CloseTabsWhere(Func<RequestTab, bool> gone)
    {
        var active = ActiveTab;
        int activeIndex = _activeTab;
        for (int i = _tabs.Count - 1; i >= 0; i--)
        {
            if (!gone(_tabs[i])) continue;
            _tabs[i].Sending?.Cancel();
            StopResponseWork(_tabs[i]);
            _tabs.RemoveAt(i);
        }
        _activeTab = active != null && _tabs.IndexOf(active) is >= 0 and var kept ? kept : Math.Min(activeIndex, _tabs.Count - 1);
    }

    // ── The row menu: ⋯ and right-click ──────────────────────────────────

    /// <summary>The ⋯ at a row's end, opening its rename / delete menu under it. A button of its
    /// own, so the keyboard and a reader reach what right-click does.</summary>
    private Element RowActionsButton(string accessibleName, string automationId, Func<IReadOnlyList<(string, Action)>> items)
    {
        var b = Ui.Button(ButtonVariant.Ghost, "⋯", null, automationId, style: Ui.Remove);
        b.AccessibleName = accessibleName;
        b.Tooltip("rename · delete");
        b.On<ClickEvent>(e =>
        {
            e.StopPropagation = true;   // the row behind opens or selects
            var box = e.CurrentTarget.WindowBorderBox;
            OpenRowMenu(box.X + box.Width - RowMenuWidth, box.Y + box.Height + 4f, items());
        });
        return b;
    }

    /// <summary>Right-click on a row opens its menu at the pointer.</summary>
    private void RowContextMenu(Element row, Func<IReadOnlyList<(string, Action)>> items) =>
        row.On<PointerDownEvent>(e =>
        {
            if (e.Button != PointerEvent.RightButton) return;
            e.StopPropagation = true;
            OpenRowMenu(e.Position.X, e.Position.Y, items());
        });

    private const float RowMenuWidth = 140f;   // BuildPopover's menu

    private void OpenRowMenu(float x, float y, IReadOnlyList<(string, Action)> items)
    {
        _popoverX = Math.Clamp(x, 8f, Math.Max(8f, _window.LayoutWidth - RowMenuWidth - 8f));
        _popoverY = Math.Clamp(y, 8f, Math.Max(8f, _window.LayoutHeight - 90f));
        _popover = items;
        MarkViewDirty();   // the popover is the window's own, no part shows it
    }

    // ── F2 and Delete: the open request, or the active environment ───────

    /// <summary>F2 renames, Delete deletes — the request open in the editor on the Requests
    /// view, the active environment on the Environments view. True when one acted.</summary>
    private bool FileShortcut(KeyDownEvent e)
    {
        bool rename = Shortcuts.Pressed("rename", e), delete = Shortcuts.Pressed("delete", e);
        if (!rename && !delete || _workspace is null) return false;
        if (View == MeridianView.Requests && ActiveTab is { } tab && File.Exists(tab.Path))
        {
            if (rename) OpenRenameRequestDialog(tab.Path); else ConfirmDeleteRequest(tab.Path);
            return true;
        }
        if (View == MeridianView.Environments && _envName is { } env)
        {
            if (rename) OpenRenameEnvironmentDialog(env); else ConfirmDeleteEnvironment(env);
            return true;
        }
        return false;
    }

    // ── The confirmation (Backdrop + card, as the prompt) ────────────────

    private (string Title, string Message, string Confirm, Action OnConfirm)? _confirm;
    private readonly Ref<ButtonElement> _confirmRef = new();
    private bool _pendingConfirmFocus;

    /// <summary>Asks before <paramref name="onConfirm"/> runs. Enter or the confirming button
    /// runs it; Escape, cancel or a click beside the card closes the question.</summary>
    private void OpenConfirm(string title, string message, string confirm, Action onConfirm)
    {
        _confirm = (title, message, confirm, onConfirm);
        _pendingConfirmFocus = true;
        MarkDirty();
    }

    /// <summary>As <see cref="SubmitDialog"/>: the action finds the question already gone, and the
    /// window builds again only when the action built nothing. Enter on the focused button arrives
    /// here as its click and then at the window as a key; the second finds nothing to confirm.</summary>
    private void Confirm()
    {
        if (_confirm is not { } confirm) return;
        _confirm = null;
        int builds = Builds;
        confirm.OnConfirm();
        if (Builds == builds) MarkDirty();
    }

    private void CancelConfirm()
    {
        _confirm = null;
        MarkDirty();
    }

    private Element BuildConfirm((string Title, string Message, string Confirm, Action OnConfirm) confirm)
    {
        var backdrop = Box.New()
            .Width(Dimension.Percent(100)).Height(Dimension.Percent(100))
            .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
            .ZIndex(70);
        backdrop.Position.Type = PositionType.Absolute;
        backdrop.Position.Top = Dimension.Px(0);
        backdrop.Position.Left = Dimension.Px(0);
        backdrop.Background(Azimuth.Alpha(Azimuth.Bg, 0.62));
        backdrop.On<ClickEvent>(e => { e.StopPropagation = true; CancelConfirm(); });

        // An alert dialog named by its title and described by its message, so a reader hears what
        // goes and where before the button that does it.
        var card = Flex.Column(gap: Azimuth.S3)
            .Width(420).Padding(new Thickness(Azimuth.S4))
            .Background(Azimuth.Surface)
            .BorderColor(Azimuth.BorderStrong).BorderWidth(1f)
            .CornerRadius(Azimuth.Radius2)
            .AccessibleRole(AccessibilityRole.Dialog).AccessibleName(confirm.Title)
            .AccessibleDescription(confirm.Message)
            .AutomationId("confirm");
        card.On<ClickEvent>(e => e.StopPropagation = true);

        card.AddChild(Text.New(confirm.Title).FontSize(13.5f).FontWeight(FontWeight.Medium).TextColor(Azimuth.Text));
        card.AddChild(Text.New(confirm.Message).FontSize(12f).TextColor(Azimuth.TextDim).AutomationId("confirm-message"));

        var buttons = Flex.Row(gap: Azimuth.S2).JustifyContent(JustifyContent.FlexEnd);
        buttons.AddChild(BarAction("cancel", "confirm-cancel", CancelConfirm));
        var ok = Ui.Button(ButtonVariant.Danger, confirm.Confirm, Confirm, "confirm-ok", style: Ui.Dialog);
        ok.WithRef(_confirmRef);
        buttons.AddChild(ok);
        card.AddChild(buttons);

        backdrop.AddChild(card);
        return backdrop;
    }
}
