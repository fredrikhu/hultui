using HultUI.Core.Animation;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using Junction.Git;
using Junction.Theme;

namespace Junction;

/// <summary>
/// Talking to a remote: which command is in flight, the motion that says so, and the
/// quiet background fetch that keeps the ahead/behind counts honest.
/// </summary>
public partial class JunctionApp
{
    // ── What is running ──────────────────────────────────────────────────

    /// <summary>Which remote command is in flight. One at a time across the whole app —
    /// two concurrent fetches of the same repository fight over the same index lock — so
    /// this is both the animation's subject and the guard every entry point checks.</summary>
    private enum RemoteOp { None, Fetch, Pull, Push, Other }

    private RemoteOp _remoteOp;

    /// <summary>0→1 and round again while a remote command runs; every frame of the
    /// toolbar's motion is a function of it. One number rather than a tween per button
    /// because the buttons are rebuilt, and an animation writing into an element the last
    /// rebuild replaced animates nothing anyone can see.</summary>
    private float _remoteOpPhase;
    private TweenHandle _remoteOpTween;

    /// <summary>Whether any remote command is in flight — the guard every entry
    /// point checks, so two of them can never race for git's index lock.</summary>
    private bool RemoteOpRunning => _remoteOp != RemoteOp.None;

    /// <summary>
    /// Marks a remote command as started and puts its button in motion.
    ///
    /// <para>The loop drives a plain field and rebuilds, which is how the rest of this app
    /// animates state it owns (the pane splitters and the diff's horizontal pan do the
    /// same). It costs a build per frame for the seconds a fetch takes, and in exchange
    /// nothing has to survive a rebuild.</para>
    /// </summary>
    private void BeginRemoteOp(RemoteOp op)
    {
        _remoteOp = op;
        _remoteOpPhase = 0f;
        _remoteOpTween.Cancel();
        // Fetch turns; pull and push travel. The travel is the shorter of the two so the
        // arrows read as a stream rather than as one slow slide.
        _remoteOpTween = Animate.Value(0f, 1f, v => { _remoteOpPhase = v; MarkDirty(); })
            .Duration(op == RemoteOp.Fetch ? 1.1f : 0.75f)
            .Ease(Easing.Linear)
            .Loop()
            .Start();
        MarkDirty();
    }

    private void EndRemoteOp()
    {
        _remoteOpTween.Cancel();
        _remoteOpTween = default;
        _remoteOp = RemoteOp.None;
        _remoteOpPhase = 0f;
        MarkDirty();
    }

    // ── The motion ───────────────────────────────────────────────────────

    // How far the travelling arrows move, in dip, and where in the cycle they fade.
    private const float RemoteOpTravel = 7f;
    private const float RemoteOpFadeIn = 0.18f;
    private const float RemoteOpFadeOut = 0.72f;

    /// <summary>
    /// A toolbar icon, in motion when its own command is the one running.
    ///
    /// <para>Fetch spins, because it is a round trip with no direction to it. Pull and
    /// push travel the way their arrows point — down and up — fading in at the start of
    /// each pass and out at the end, so the loop reads as a stream of arrows rather than
    /// as one that keeps snapping back.</para>
    /// </summary>
    private Element RemoteOpIcon(string glyph, float size, Color color, RemoteOp op)
    {
        var icon = Ui.Icon(glyph, size, color);
        if (_remoteOp != op) return icon;

        float p = _remoteOpPhase;
        if (op == RemoteOp.Fetch)
        {
            icon.Transform = Transform2D.Rotate(p * 360f);
            return icon;
        }

        // Down for pull, up for push.
        float direction = op == RemoteOp.Push ? -1f : 1f;
        icon.Transform = (icon.Transform ?? Transform2D.Identity) with
        {
            TranslateY = direction * (p - 0.5f) * RemoteOpTravel,
        };
        icon.Opacity = p < RemoteOpFadeIn ? p / RemoteOpFadeIn
            : p > RemoteOpFadeOut ? (1f - p) / (1f - RemoteOpFadeOut)
            : 1f;
        return icon;
    }

    /// <summary>What the button says while it is working. Present tense, because it is
    /// the only thing on screen still describing an action that has not finished.</summary>
    private static string RemoteOpLabel(RemoteOp op) => op switch
    {
        RemoteOp.Fetch => "Fetching…",
        RemoteOp.Pull => "Pulling…",
        RemoteOp.Push => "Pushing…",
        _ => "Working…",
    };

    // ── Background fetch ─────────────────────────────────────────────────

    private CancellationTokenSource? _autoFetchCts;

    /// <summary>Settle time before the first background fetch. Opening the app already
    /// costs a burst of git calls to fill seven views; adding network round trips to that
    /// makes the slowest moment of the session slower still.</summary>
    private static readonly TimeSpan AutoFetchSettle = TimeSpan.FromSeconds(20);

    /// <summary>
    /// (Re)starts the background fetch loop from the current settings, cancelling any
    /// loop already running. Safe to call whenever the setting changes.
    /// </summary>
    private void RestartAutoFetch()
    {
        _autoFetchCts?.Cancel();
        _autoFetchCts?.Dispose();
        _autoFetchCts = null;
        if (!_settings.General.AutoFetchEnabled) return;

        var cts = new CancellationTokenSource();
        _autoFetchCts = cts;
        _ = AutoFetchLoop(cts.Token);
    }

    /// <summary>
    /// Fetches in the background on an interval so ahead/behind counts, remote branches
    /// and the Overview are about the remote as it is now rather than as it was when the
    /// window opened.
    ///
    /// <para>Deliberately silent: no toast, no spinner, and no view reload unless
    /// something actually arrived. A background task that redraws the screen every few
    /// minutes is worse than a stale number — it moves what someone is reading.</para>
    /// </summary>
    private async Task AutoFetchLoop(CancellationToken ct)
    {
        try
        {
            await Task.Delay(AutoFetchSettle, ct);
            while (!ct.IsCancellationRequested)
            {
                await AutoFetchSweep(ct);
                await Task.Delay(TimeSpan.FromMinutes(
                    Math.Clamp(_settings.General.AutoFetchIntervalMinutes, 1, 120)), ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Settings changed, or the window is closing.
        }
    }

    private async Task AutoFetchSweep(CancellationToken ct)
    {
        foreach (var path in AutoFetchTargets())
        {
            if (ct.IsCancellationRequested) return;
            // Never race a fetch the user asked for: git takes a lock per repository,
            // and the loser reports a failure the user did nothing to cause.
            if (RemoteOpRunning) return;

            var repo = RepoFor(path);
            string before, after;
            GitResult result;
            try
            {
                before = await Task.Run(repo.RemoteRefsFingerprint, ct);
                result = await repo.FetchAsync(NoAskPass, _settings.Git.PruneOnFetch);
                if (ct.IsCancellationRequested) return;
                if (!result.Success) continue;   // offline, or credentials we must not prompt for
                after = await Task.Run(repo.RemoteRefsFingerprint, ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // A repository that has been moved or unmounted since it was opened throws
                // rather than failing. One bad repository must not end the loop for the
                // rest — and nothing here is worth telling the user about, because they
                // did not ask for this fetch.
                continue;
            }
            // Only invalidate when a ref actually moved. Bumping unconditionally would
            // throw away every cached view of a repository nobody had pushed to, several
            // times an hour, for nothing. (Both awaits above resume on the UI thread, so
            // touching app state here is safe.)
            if (string.Equals(before, after, StringComparison.Ordinal)) continue;
            BumpDataGen(path);
            // Rerender, not MarkDirty: the bump re-keys every loader of this repo, and only
            // a tracked rebuild subscribes to the new ones (see OnExternalChange).
            Rerender();
        }
    }

    // ── GitLab refresh timer ─────────────────────────────────────────────────────────

    private CancellationTokenSource? _gitlabRefreshCts;

    /// <summary>(Re)starts the hosted-data timer from the settings — Options › General.
    /// Off (0 minutes) leaves the reloads to Fetch, the panel and repository switches.</summary>
    private void RestartGitLabRefresh()
    {
        _gitlabRefreshCts?.Cancel();
        _gitlabRefreshCts?.Dispose();
        _gitlabRefreshCts = null;
        if (_settings.General.GitLabRefreshMinutes <= 0) return;
        var cts = new CancellationTokenSource();
        _gitlabRefreshCts = cts;
        _ = GitLabRefreshLoop(cts.Token);
    }

    private async Task GitLabRefreshLoop(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMinutes(Math.Clamp(_settings.General.GitLabRefreshMinutes, 1, 120)), ct);
                // Only the repository on screen: the others reload when they are switched to.
                if (HasActiveRepo && GitLabActive(_activeRepoPath) && !_mrLoaders.For(_activeRepoPath).IsLoading)
                    RefreshHosted(_activeRepoPath);
            }
        }
        catch (OperationCanceledException)
        {
            // Settings changed, or the window is closing.
        }
    }

    /// <summary>Turns the background fetch on or off — Options › General. The toast
    /// confirms which way it went, since the effect is by design invisible.</summary>
    private void ToggleAutoFetch()
    {
        _settings.General.AutoFetchEnabled = !_settings.General.AutoFetchEnabled;
        Persist();
        RestartAutoFetch();
        Flash(_settings.General.AutoFetchEnabled
            ? $"Background fetch on — every {_settings.General.AutoFetchIntervalMinutes} min"
            : "Background fetch off");
    }

    /// <summary>Which repositories the background fetch covers. The sidebar shows an
    /// ahead/behind count for every repository in the workspace, so "all open" means all
    /// of those — a count is only worth showing if it is current.</summary>
    private IReadOnlyList<string> AutoFetchTargets() =>
        _settings.General.AutoFetchMode == AutoFetchMode.AllOpenRepositories
            ? [.. ActiveWorkspaceRepos]
            : HasActiveRepo ? [_activeRepoPath] : [];
}
