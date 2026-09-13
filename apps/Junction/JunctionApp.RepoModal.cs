using System.Linq;
using HultUI;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using Junction.Git;
using Junction.Platform;
using Junction.Theme;

namespace Junction;

public partial class JunctionApp
{
    // ── Badge helpers ────────────────────────────────────────────────────

    /// <summary>The swatch id for a repo — its stored badge, or a deterministic
    /// seed so unassigned repos still get stable, varied colours.</summary>
    private string BadgeFor(string path)
    {
        var key = NormPath(path);
        if (_settings.RepoBadges.TryGetValue(key, out var id) && !string.IsNullOrEmpty(id))
            return id;
        return Swatches.SeedId(System.IO.Path.GetFileName(path.TrimEnd('/', '\\')));
    }

    /// <summary>Badge tile colours (bg, fg) for a repo, driven by its swatch.</summary>
    private (Color Bg, Color Fg) RepoTile(string path)
    {
        var s = Swatches.ById(BadgeFor(path));
        return (s.Bg, s.Fg);
    }

    private static string NormPath(string path) =>
        (OperatingSystem.IsWindows() ? path.ToLowerInvariant() : path).TrimEnd('/', '\\');

    // ── Modal form ───────────────────────────────────────────────────────

    private enum ModalKind { AddRepo, CloneRepo, NewWorkspace, EditWorkspace, EditRepo }

    /// <summary>Live validation state for the add-repo folder, resolved off the UI
    /// thread (see <see cref="EnsureRepoPathChecked"/>) so Build() never spawns git.</summary>
    private enum RepoPathStatus { Empty, Checking, NotFound, NotRepo, Ok }

    private sealed class ModalForm
    {
        public required ModalKind Kind;
        public string Name = "";
        public string Path = "";
        public string Url = "";
        public string Dest = "~/code";
        public string Icon = "stack";
        public string Color = "blurple";
        public HashSet<string> RepoPaths = new();   // workspace modes: member repos
        public HashSet<string> WorkspaceIds = new(); // repo modes: joined workspaces
        public string? EditId;   // workspace id (workspace modes) or repo path (editRepo)

        // CloneRepo: the GitLab side of the URL / GitLab switch.
        public bool FromGitLab;
        public string CloneHost = "";
        public string CloneSearch = "";
        public Junction.GitLab.GlProject? CloneProject;

        // AddRepo folder validation: the expanded path we last checked and its result.
        public string CheckedPath = "";
        public RepoPathStatus PathStatus = RepoPathStatus.Empty;

        public bool IsWorkspace => Kind is ModalKind.NewWorkspace or ModalKind.EditWorkspace;
    }

    // ── Open ─────────────────────────────────────────────────────────────

    private void OpenModal(ModalForm form)
    {
        _modal = form;
        _popover = null;
        _pendingModalFocus = true;
        MarkDirty();
    }

    private void CloseModal()
    {
        _modal = null;
        MarkDirty();
    }

    private void OpenAddRepo() => OpenModal(new ModalForm
    {
        Kind = ModalKind.AddRepo,
        Color = "blurple",
        WorkspaceIds = ActiveWorkspace is { } w ? new() { w.Id } : new(),
    });

    internal void OpenCloneRepo() => OpenModal(new ModalForm
    {
        Kind = ModalKind.CloneRepo,
        Color = "blurple",
        WorkspaceIds = ActiveWorkspace is { } w ? new() { w.Id } : new(),
    });

    private void OpenNewWorkspace() => OpenModal(new ModalForm
    {
        Kind = ModalKind.NewWorkspace,
        Icon = "stack",
        Color = "blurple",
    });

    private void OpenEditWorkspace(string id)
    {
        var ws = _settings.Workspaces.FirstOrDefault(w => w.Id == id);
        if (ws == null) return;
        OpenModal(new ModalForm
        {
            Kind = ModalKind.EditWorkspace,
            EditId = id,
            Name = ws.Name,
            Icon = ws.Icon,
            Color = ws.Color,
            RepoPaths = new(ws.RepoPaths.Select(NormPath)),
        });
    }

    private void OpenEditRepo(string path)
    {
        var joined = _settings.Workspaces.Where(w => w.RepoPaths.Any(p => PathEquals(p, path)))
                                         .Select(w => w.Id);
        OpenModal(new ModalForm
        {
            Kind = ModalKind.EditRepo,
            EditId = path,
            Name = System.IO.Path.GetFileName(path.TrimEnd('/', '\\')),
            Color = BadgeFor(path),
            WorkspaceIds = new(joined),
        });
    }

    // ── Submit / mutations ───────────────────────────────────────────────

    private string ModalPreviewName(ModalForm f) => f.Kind switch
    {
        ModalKind.CloneRepo => NameFromUrl(f.Url) is { Length: > 0 } n ? n : "repository",
        ModalKind.AddRepo => f.Path.Length > 0 ? System.IO.Path.GetFileName(f.Path.TrimEnd('/', '\\')) : "repository",
        _ when f.IsWorkspace => f.Name.Length > 0 ? f.Name : "Workspace name",
        _ => f.Name.Length > 0 ? f.Name : "repository",
    };

    private static bool ModalValid(ModalForm f) => f.Kind switch
    {
        ModalKind.AddRepo => f.PathStatus == RepoPathStatus.Ok,
        ModalKind.CloneRepo => f.Url.Trim().Length > 0,
        ModalKind.NewWorkspace or ModalKind.EditWorkspace => f.Name.Trim().Length > 0,
        ModalKind.EditRepo => true,
        _ => false,
    };

    private void SubmitModal()
    {
        if (_modal is not { } f || !ModalValid(f)) return;
        switch (f.Kind)
        {
            case ModalKind.AddRepo: SubmitAddRepo(f); break;
            case ModalKind.CloneRepo: SubmitCloneRepo(f); break;
            case ModalKind.NewWorkspace: SubmitNewWorkspace(f); break;
            case ModalKind.EditWorkspace: SubmitEditWorkspace(f); break;
            case ModalKind.EditRepo: SubmitEditRepo(f); break;
        }
    }

    private void SubmitAddRepo(ModalForm f)
    {
        // The primary button is only enabled once the inline check reports Ok, so
        // by here the path is a validated repo; register it directly.
        var path = ExpandPath(f.Path.Trim());
        RegisterRepo(path, f);
        Flash($"Added {System.IO.Path.GetFileName(path.TrimEnd('/', '\\'))}");
    }

    /// <summary>Keeps <see cref="ModalForm.PathStatus"/> current for the add-repo
    /// folder. When the entered path changes, the (process-spawning) git check runs
    /// off the UI thread and marks dirty on completion, so Build() stays cheap and
    /// the dialog reflects validity inline instead of failing on submit.</summary>
    private void EnsureRepoPathChecked(ModalForm f)
    {
        var expanded = ExpandPath(f.Path.Trim());
        if (expanded.Length == 0) { f.CheckedPath = ""; f.PathStatus = RepoPathStatus.Empty; return; }
        if (f.CheckedPath == expanded) return;   // already checked / in flight for this path

        f.CheckedPath = expanded;
        f.PathStatus = RepoPathStatus.Checking;
        var target = expanded;
        _ = Task.Run(() =>
        {
            RepoPathStatus status =
                !System.IO.Directory.Exists(target) ? RepoPathStatus.NotFound
                : !IsGitRepo(target) ? RepoPathStatus.NotRepo
                : RepoPathStatus.Ok;
            Application.InvokeOnMainThread(() =>
            {
                if (_modal != f || f.CheckedPath != target) return;   // superseded
                f.PathStatus = status;
                MarkDirty();
            });
        });
    }

    /// <summary>True when <paramref name="path"/> is a git working-tree root: it has
    /// a <c>.git</c> entry (a directory for a normal clone, or a file for a worktree /
    /// submodule) that git actually recognises. Rejects plain folders and empty or
    /// broken <c>.git</c> directories that the directory check alone would accept.</summary>
    private static bool IsGitRepo(string path)
    {
        var dotGit = System.IO.Path.Combine(path, ".git");
        if (!System.IO.Directory.Exists(dotGit) && !System.IO.File.Exists(dotGit))
            return false;
        var res = GitProcess.Run(path, "rev-parse", "--is-inside-work-tree");
        return res.Success && res.Stdout.Trim() == "true";
    }

    private async void SubmitCloneRepo(ModalForm f)
    {
        var url = f.Url.Trim();
        var name = NameFromUrl(url);
        if (string.IsNullOrEmpty(name)) { Flash("Couldn't derive a name from the URL"); return; }
        var dest = ExpandPath(string.IsNullOrWhiteSpace(f.Dest) ? "~/code" : f.Dest.Trim());
        var target = System.IO.Path.Combine(dest, name);

        CloseModal();
        Flash($"Cloning {name}…");
        var res = await Task.Run(() =>
        {
            try { System.IO.Directory.CreateDirectory(dest); } catch { }
            return GitProcess.Run(dest, "clone", url, name);
        });
        if (!res.Success) { Flash(FirstLine(res.Stderr, "Clone failed")); return; }
        RegisterRepo(target, f);
        Flash($"Cloned {name}");
    }

    /// <summary>Adds a repo path to the flat set + checked workspaces, assigns its
    /// badge, switches to the home workspace, selects it, and lands on Overview.</summary>
    private void RegisterRepo(string path, ModalForm f)
    {
        if (!_settings.RepositoryPaths.Any(p => PathEquals(p, path)))
            _settings.RepositoryPaths.Add(path);
        _settings.RepoBadges[NormPath(path)] = f.Color;

        foreach (var ws in _settings.Workspaces)
            if (f.WorkspaceIds.Contains(ws.Id) && !ws.RepoPaths.Any(p => PathEquals(p, path)))
                ws.RepoPaths.Add(path);

        var home = f.WorkspaceIds.FirstOrDefault(id => _settings.Workspaces.Any(w => w.Id == id));
        if (home != null) _activeWorkspaceId = home;
        _activeRepoPath = path;
        SetCommitSelection(null);
        Persist();
        _modal = null;
        SetView(JunctionView.Overview);
        RefreshAll();
    }

    private void SubmitNewWorkspace(ModalForm f)
    {
        var name = f.Name.Trim();
        var id = UniqueId(Slug(name), _settings.Workspaces.Select(w => w.Id).ToHashSet());
        _settings.Workspaces.Add(new WorkspaceData
        {
            Id = id, Name = name, Icon = f.Icon, Color = f.Color,
            RepoPaths = ResolveMemberPaths(f.RepoPaths),
        });
        _modal = null;
        Persist();
        SelectWorkspace(id);
        Flash($"Created workspace {name}");
    }

    private void SubmitEditWorkspace(ModalForm f)
    {
        var ws = _settings.Workspaces.FirstOrDefault(w => w.Id == f.EditId);
        if (ws == null) { CloseModal(); return; }
        ws.Name = f.Name.Trim();
        ws.Icon = f.Icon;
        ws.Color = f.Color;
        ws.RepoPaths = ResolveMemberPaths(f.RepoPaths);
        _modal = null;
        // If the active repo fell out of the active workspace, reselect.
        if (ws.Id == _activeWorkspaceId && !ws.RepoPaths.Any(p => PathEquals(p, _activeRepoPath)))
            _activeRepoPath = ws.RepoPaths.FirstOrDefault() ?? "";
        Persist();
        Flash($"Updated {ws.Name}");
        RefreshAll();
    }

    private void SubmitEditRepo(ModalForm f)
    {
        if (f.EditId is not { } path) { CloseModal(); return; }
        _settings.RepoBadges[NormPath(path)] = f.Color;
        foreach (var ws in _settings.Workspaces)
        {
            var has = ws.RepoPaths.Any(p => PathEquals(p, path));
            var want = f.WorkspaceIds.Contains(ws.Id);
            if (has && !want) ws.RepoPaths.RemoveAll(p => PathEquals(p, path));
            else if (!has && want) ws.RepoPaths.Add(path);
        }
        _modal = null;
        var active = ActiveWorkspace;
        if (active != null && !active.RepoPaths.Any(p => PathEquals(p, _activeRepoPath)))
            _activeRepoPath = active.RepoPaths.FirstOrDefault() ?? "";
        Persist();
        Flash($"Saved {System.IO.Path.GetFileName(path.TrimEnd('/', '\\'))}");
        RefreshAll();
    }

    /// <summary>Maps the form's normalised member-path set back to the real repo
    /// paths from the flat registry.</summary>
    private List<string> ResolveMemberPaths(HashSet<string> normalised) =>
        _settings.RepositoryPaths.Where(p => normalised.Contains(NormPath(p))).ToList();

    // ── Sidebar-menu direct actions ──────────────────────────────────────

    private void DuplicateWorkspace(string id)
    {
        var w = _settings.Workspaces.FirstOrDefault(x => x.Id == id);
        if (w == null) return;
        var nid = UniqueId(Slug(w.Name + "-copy"), _settings.Workspaces.Select(x => x.Id).ToHashSet());
        _settings.Workspaces.Add(new WorkspaceData
        {
            Id = nid, Name = w.Name + " copy", Icon = w.Icon, Color = w.Color,
            RepoPaths = [.. w.RepoPaths],
        });
        Persist();
        Flash($"Duplicated {w.Name}");
        MarkDirty();
    }

    private void DeleteWorkspace(string id)
    {
        if (_settings.Workspaces.Count <= 1) { Flash("Keep at least one workspace"); return; }
        _settings.Workspaces.RemoveAll(w => w.Id == id);
        if (_activeWorkspaceId == id)
        {
            var first = _settings.Workspaces[0];
            _activeWorkspaceId = first.Id;
            if (!first.RepoPaths.Any(p => PathEquals(p, _activeRepoPath)))
                _activeRepoPath = first.RepoPaths.FirstOrDefault() ?? "";
        }
        Persist();
        Flash("Deleted workspace");
        RefreshAll();
    }

    private void ToggleRepoInWorkspace(string path, string wsId)
    {
        var ws = _settings.Workspaces.FirstOrDefault(w => w.Id == wsId);
        if (ws == null) return;
        if (ws.RepoPaths.Any(p => PathEquals(p, path)))
        {
            ws.RepoPaths.RemoveAll(p => PathEquals(p, path));
            if (ws.Id == _activeWorkspaceId && PathEquals(path, _activeRepoPath))
                _activeRepoPath = ws.RepoPaths.FirstOrDefault() ?? "";
            Flash($"Removed from {ws.Name}");
        }
        else
        {
            ws.RepoPaths.Add(path);
            Flash($"Added to {ws.Name}");
        }
        Persist();
        RefreshAll();
    }

    private void RemoveRepo(string path)
    {
        ForgetRepo(path);
        Persist();
        Flash("Removed repository");
        RefreshAll();
    }

    /// <summary>Drops a path from everything that remembers repositories — the flat
    /// registry, every workspace, its badge — and reselects a fallback if it was the
    /// active repo. Deliberately does not persist or refresh: the caller does, so a
    /// batch (pruning several worktrees at once) writes settings once.
    ///
    /// <para>Shared with the worktree panel, which registers a linked worktree as a
    /// repository when you open it and so has to undo exactly this much when the
    /// worktree goes away.</para></summary>
    private void ForgetRepo(string path)
    {
        _settings.RepositoryPaths.RemoveAll(p => PathEquals(p, path));
        foreach (var ws in _settings.Workspaces)
            ws.RepoPaths.RemoveAll(p => PathEquals(p, path));
        _settings.RepoBadges.Remove(NormPath(path));
        if (PathEquals(path, _activeRepoPath))
        {
            var ws = ActiveWorkspace;
            _activeRepoPath = ws?.RepoPaths.FirstOrDefault()
                ?? _settings.RepositoryPaths.FirstOrDefault() ?? "";
            SetCommitSelection(null);
        }
    }

    // ── Sidebar context-menu item lists ──────────────────────────────────

    private IReadOnlyList<ContextMenuItem> RepoAddMenuItems() =>
    [
        Item(Icons.FolderPlus, "Add existing repository…", OpenAddRepo),
        Item(Icons.DownloadSimple, "Clone repository…", OpenCloneRepo),
    ];

    private IReadOnlyList<ContextMenuItem> WorkspaceMenuItems(WorkspaceData w) =>
    [
        Item(Icons.ArrowRight, "Open workspace", () => SelectWorkspace(w.Id)),
        Item(Icons.PencilSimple, "Edit workspace…", () => OpenEditWorkspace(w.Id)),
        Item(Icons.Copy, "Duplicate", () => DuplicateWorkspace(w.Id)),
        ContextMenuItem.Separator(),
        Danger(Icons.Trash, "Delete workspace", () => DeleteWorkspace(w.Id)),
    ];

    private IReadOnlyList<ContextMenuItem> RepoMenuItems(string path)
    {
        var cur = ActiveWorkspace;
        var inCur = cur != null && cur.RepoPaths.Any(p => PathEquals(p, path));
        var items = new List<ContextMenuItem>
        {
            Item(Icons.ArrowRight, "Open", () => OpenRepo(path)),
            Item(Icons.Swatches, "Repository settings…", () => OpenEditRepo(path)),
            ContextMenuItem.Separator(),
        };
        if (cur != null)
            items.Add(inCur
                ? Item(Icons.MinusCircle, $"Remove from {cur.Name}", () => ToggleRepoInWorkspace(path, cur.Id))
                : Item(Icons.PlusCircle, $"Add to {cur.Name}", () => ToggleRepoInWorkspace(path, cur.Id)));
        items.Add(ContextMenuItem.Separator());
        // The two places you go when the answer is not in a git client: the folder, and
        // a shell in it.
        items.Add(Item(Icons.TreeStructure, "Worktrees…", () => OpenWorktreesFor(path)));
        items.Add(ContextMenuItem.Separator());
        items.Add(Item(Icons.FolderOpen, ShellLauncher.FileManagerLabel, () => OpenRepoFolder(path)));
        items.Add(Item(Icons.Terminal, "Open in terminal", () => OpenRepoTerminal(path)));
        items.Add(Item(Icons.Copy, "Copy path", () => CopyToClipboard(path)));
        items.Add(ContextMenuItem.Separator());
        items.Add(Danger(Icons.Trash, "Remove repository", () => RemoveRepo(path)));
        return items;
    }

    /// <summary>Opens the repository's folder in the desktop's file manager. Off the UI
    /// thread for the same reason the Files tab's launcher is: a Windows association
    /// lookup goes through COM and can stall for most of a second.</summary>
    private async void OpenRepoFolder(string path)
    {
        var (ok, error) = await Task.Run(() =>
        {
            var started = ShellLauncher.Open(path, out var err);
            return (started, err);
        });
        if (!ok)
            Flash(string.IsNullOrEmpty(error) ? "Could not open the file manager" : error);
    }

    /// <summary>Opens a terminal in the repository's folder — for the handful of git
    /// commands no client has a button for.</summary>
    private async void OpenRepoTerminal(string path)
    {
        var (ok, error) = await Task.Run(() =>
        {
            var started = ShellLauncher.OpenTerminal(path, _settings.ExternalTools.Terminal, out var err);
            return (started, err);
        });
        if (!ok)
            Flash(string.IsNullOrEmpty(error) ? "Could not open a terminal" : error);
    }

    // ── id / url helpers (mirror the design) ─────────────────────────────

    private static string Slug(string s)
    {
        var chars = (s ?? "").Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
        var slug = new string(chars);
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        slug = slug.Trim('-');
        return slug.Length == 0 ? "repo" : slug;
    }

    private static string UniqueId(string baseId, HashSet<string> taken)
    {
        if (!taken.Contains(baseId)) return baseId;
        for (var n = 2; ; n++)
        {
            var id = $"{baseId}-{n}";
            if (!taken.Contains(id)) return id;
        }
    }

    private static string NameFromUrl(string url)
    {
        var u = (url ?? "").Trim();
        if (u.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) u = u[..^4];
        var parts = u.Split(['/', ':', '\\'], StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[^1] : "";
    }

    private static string ExpandPath(string path)
    {
        if (path.StartsWith('~'))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            path = home + path[1..];
        }
        return path;
    }
}
