using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using Junction.GitLab;
using Junction.Theme;

namespace Junction;

/// <summary>
/// Options › GitLab accounts: who the app is signed in as, on which hosts, and the two ways
/// to add one — the browser for gitlab.com, a pasted token for a self-managed host. The
/// pane shows identity, expiry and scopes; the token itself is only ever in the OS store
/// (see <see cref="GitLab.GitLabAccounts"/>) or, briefly, in the add-host card that verifies it.
/// </summary>
public partial class JunctionApp
{
    private enum HostCardState { Idle, Testing, Verified, Failed }

    /// <summary>The "Add a self-managed host" card while it is open. The token lives here
    /// until Test accepts it and Add account moves it to the store, or Cancel drops it.</summary>
    private sealed class HostCard
    {
        public string Url = "https://";
        public string Token = "";
        public HostCardState State = HostCardState.Idle;
        public VerifiedToken? Verified;
        public string? Error;
        /// <summary>Bumped on every edit, so a Test that lands after the fields changed is
        /// dropped rather than shown against the wrong token.</summary>
        public int Serial;
    }

    private HostCard? _hostCard;
    private bool _oauthWaiting;
    private CancellationTokenSource? _oauthCts;
    // Hosts whose token was refused since sign-in: the row flips to "sign in again".
    private readonly HashSet<string> _gitlabAuthFailed = new(StringComparer.OrdinalIgnoreCase);

    internal IReadOnlyList<GitLabAccountData> GitLabAccountList => _gitlab.All;

    /// <summary>Any hosted call answering 401 lands here, and the account row says so.</summary>
    internal void MarkGitLabAuthFailed(string host)
    {
        if (_gitlabAuthFailed.Add(host)) MarkDirty();
    }

    // ── The pane ─────────────────────────────────────────────────────────────────────

    private Element BuildGitLabAccountsPane()
    {
        var col = Flex.Column(gap: 0);
        col.AddChild(BuildAccountsHeaderRow());
        if (_oauthWaiting) col.AddChild(BuildOAuthWaitingCard());
        if (_hostCard is { } card) col.AddChild(BuildHostCard(card));

        var accounts = _gitlab.All;
        if (accounts.Count == 0)
        {
            if (_hostCard is null && !_oauthWaiting) col.AddChild(BuildNoAccounts());
        }
        else
        {
            var list = Flex.Column(gap: 8);
            foreach (var account in accounts)
            {
                var row = BuildAccountRow(account);
                row.Key = "gitlab-account-" + account.Host;
                list.AddChild(row);
            }
            col.AddChild(list);
        }

        col.AddChild(BuildDetectedProjects());
        return col;
    }

    private Element BuildAccountsHeaderRow()
    {
        var row = Flex.Row(gap: 8).AlignItems(AlignItems.Center).Padding(new Thickness(12, 0, 12, 0));
        var add = _oauthWaiting
            ? DisabledButton("Add gitlab.com account", Icons.GitLabLogo)
            : Ui.PrimaryButton("Add gitlab.com account", Icons.GitLabLogo, StartGitLabComOAuth, 12f);
        add.Key = "add-gitlab-com-" + (_oauthWaiting ? "off" : "on");
        add.AccessibleName = "Add gitlab.com account";
        // The buttons keep their width; the note beside them is what gives way.
        row.AddChild(add.FlexShrink(0));
        var self = Ui.SecondaryButton("Add self-managed host…", Icons.HardDrives, () => OpenHostCard(null), 12f);
        self.AccessibleName = "Add self-managed host";
        row.AddChild(self.FlexShrink(0));

        var note = Flex.Row(gap: 5).AlignItems(AlignItems.Center).JustifyContent(JustifyContent.FlexEnd)
            .FlexGrow(1).FlexShrink(1).FlexBasis(Dimension.Px(0));
        note.AddChild(Ui.Icon(Icons.ShieldCheck, 12f, Nocturne.On(0.45)));
        note.AddChild(Text.New(OperatingSystem.IsWindows()
                ? "Tokens live in the Windows Credential Manager, never in the settings file."
                : "Tokens are never written to the settings file.")
            .FontSize(10.5f).TextColor(Nocturne.On(0.45)).MaxLines(1).OverflowMode(TextOverflow.Ellipsis));
        row.AddChild(note);
        return row;
    }

    private Element BuildNoAccounts()
    {
        var box = Box.New().Padding(new Thickness(28)).AlignItems(AlignItems.Center)
            .BorderColor(Nocturne.Divider).BorderWidth(1f).CornerRadius(Nocturne.RadiusMd);
        box.AddChild(Text.New("No accounts. Repositories hosted on GitLab look like any other until you add one.")
            .FontSize(12.5f).TextColor(Nocturne.On(0.5)));
        return box;
    }

    // ── Account rows ─────────────────────────────────────────────────────────────────

    private BoxElement BuildAccountRow(GitLabAccountData a)
    {
        bool dead = a.IsExpired || _gitlabAuthFailed.Contains(a.Host);
        bool warn = !dead && a.Kind == GitLabTokenKind.Pat && a.ExpiresSoon;

        var row = Flex.Row(gap: 12).AlignItems(AlignItems.Center)
            .Padding(new Thickness(11, 12, 11, 12)).CornerRadius(Nocturne.RadiusMd)
            .Background(Nocturne.Bg)
            .BorderColor(dead ? Nocturne.DangerIcon : Nocturne.Divider).BorderWidth(1f);

        var initials = Initials(a.Name.Length > 0 ? a.Name : a.Username);
        row.AddChild(Box.New().Width(34).Height(34).CornerRadius(17f).FlexShrink(0)
            .Background(dead ? Nocturne.Neutral800 : Nocturne.Accent800)
            .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
            .Children(Text.New(initials).FontSize(12f).FontWeight(FontWeight.Medium)
                .TextColor(dead ? Nocturne.Neutral200 : Nocturne.Accent100)));

        var text = Flex.Column(gap: 0).FlexGrow(1).FlexShrink(1).FlexBasis(Dimension.Px(0));

        var line1 = Flex.Row(gap: 8).AlignItems(AlignItems.Center);
        line1.AddChild(Text.New(a.Name).FontSize(13f).FontWeight(FontWeight.Medium));
        line1.AddChild(Text.New("@" + a.Username).FontSize(13f).TextColor(Nocturne.On(0.5)));
        line1.AddChild(Ui.TagOutline(a.Kind == GitLabTokenKind.OAuth ? "OAuth" : "PAT", Nocturne.Accent, 9f));
        text.AddChild(line1);

        var line2 = Flex.Row(gap: 8).AlignItems(AlignItems.Center).Padding(new Thickness(3, 0, 0, 0));
        line2.AddChild(Ui.Mono(a.Host, 11f, dead ? Nocturne.DangerText : Nocturne.On(0.55)));
        line2.AddChild(Text.New("·").FontSize(11f).TextColor(Nocturne.On(0.35)));
        if (dead || warn)
        {
            line2.AddChild(Ui.Icon(Icons.Warning, 12f, dead ? Nocturne.DangerText : Nocturne.MarkAmber));
        }
        line2.AddChild(Text.New(ExpiryText(a, dead)).FontSize(11f)
            .TextColor(dead ? Nocturne.DangerText : warn ? Nocturne.MarkAmber : Nocturne.On(0.55)));
        text.AddChild(line2);

        if (!dead && a.Scopes.Count > 0)
        {
            var scopes = Flex.Row(gap: 4).AlignItems(AlignItems.Center).Padding(new Thickness(6, 0, 0, 0));
            foreach (var s in a.Scopes)
                scopes.AddChild(Box.New().Padding(new Thickness(1, 6, 1, 6)).CornerRadius(4f)
                    .Background(Nocturne.Neutral800)
                    .Children(Ui.Mono(s, 9f, Nocturne.Neutral100)));
            text.AddChild(scopes);
        }
        row.AddChild(text);

        var actions = Flex.Row(gap: 6).AlignItems(AlignItems.Center).FlexShrink(0);
        if (dead)
        {
            actions.AddChild(Ui.PrimaryButton("Sign in again", null, () => OpenHostCard(a.WebBaseUrl), 12f));
            actions.AddChild(Ui.GhostButton("Remove", null, () => SignOut(a.Host)));
        }
        else
        {
            if (warn)
                actions.AddChild(Ui.SecondaryButton("Rotate token", Icons.ArrowsClockwise, () => RotateToken(a.Host), 12f));
            var signOut = Ui.GhostButton("Sign out", Icons.SignOut, () => PromptSignOut(a.Host));
            signOut.AccessibleName = "Sign out of " + a.Host;
            actions.AddChild(signOut);
        }
        row.AddChild(actions);
        return row;
    }

    private static string ExpiryText(GitLabAccountData a, bool dead)
    {
        if (dead)
            return a.ExpiresAt is { } e && e <= DateTimeOffset.UtcNow
                ? $"token expired {e:yyyy-MM-dd} — sign in again"
                : "token was refused — sign in again";
        if (a.Kind == GitLabTokenKind.OAuth) return "access token refreshes automatically";
        return a.DaysLeft switch
        {
            null => "never expires",
            0 => "expires today",
            1 => "expires tomorrow",
            var d => $"expires in {d} days",
        };
    }

    // ── OAuth (gitlab.com) ───────────────────────────────────────────────────────────

    private Element BuildOAuthWaitingCard()
    {
        var card = Flex.Row(gap: 12).AlignItems(AlignItems.Center)
            .Padding(new Thickness(13, 14, 13, 14)).CornerRadius(Nocturne.RadiusMd)
            .Background(Nocturne.Accent900).BorderColor(Nocturne.Accent700).BorderWidth(1f);
        card.AddChild(Spinner(20f, Nocturne.Accent300));
        var text = Flex.Column(gap: 2).FlexGrow(1).FlexShrink(1).FlexBasis(Dimension.Px(0));
        text.AddChild(Text.New("Waiting for the browser…").FontSize(13f).FontWeight(FontWeight.Medium));
        text.AddChild(Text.New($"Finish signing in to gitlab.com in the window that just opened. Junction is listening on localhost:{GitLabOAuth.DefaultPort} for the redirect.")
            .FontSize(11.5f).TextColor(Nocturne.On(0.62)));
        card.AddChild(text);
        card.AddChild(Ui.GhostButton("Paste a token instead", null, () => { CancelOAuth(); OpenHostCard("https://gitlab.com"); }));
        card.AddChild(Ui.SecondaryButton("Cancel", null, () => { CancelOAuth(); MarkDirty(); }, 12f));
        return Flex.Column(gap: 0).Padding(new Thickness(0, 0, 10, 0)).Children(card);
    }

    private async void StartGitLabComOAuth()
    {
        if (_oauthWaiting) return;
        if (GitLabOAuth.GitLabComClientId.Length == 0)
        {
            // Until the application is registered on gitlab.com the browser flow has nothing
            // to ask for; the token path is the same sign-in with one more paste.
            OpenHostCard("https://gitlab.com");
            Flash("No OAuth application is registered for gitlab.com yet — paste a token instead");
            return;
        }

        _oauthWaiting = true;
        _oauthCts = new CancellationTokenSource();
        var ct = _oauthCts.Token;
        MarkDirty();

        GitLabResult<GitLabAccountData> res;
        try
        {
            res = await Task.Run(() => _gitlab.SignInWithOAuthAsync("https://gitlab.com", OpenInBrowser, ct: ct), ct);
        }
        catch (OperationCanceledException)
        {
            return;   // Cancel or Paste-a-token already put the pane back
        }
        if (!_oauthWaiting) return;
        _oauthWaiting = false;
        _oauthCts = null;

        if (res.Ok)
        {
            _gitlabAuthFailed.Remove(res.Value.Host);
            ForgetFirstRunDismissal(res.Value.Host);
            Flash($"Signed in to gitlab.com as @{res.Value.Username}");
        }
        else
        {
            Flash(res.Failure!.Message);
        }
        Rerender();
    }

    private static bool OpenInBrowser(string url) => Platform.ShellLauncher.Open(url, out _);

    private void CancelOAuth()
    {
        if (!_oauthWaiting) return;
        _oauthWaiting = false;
        _oauthCts?.Cancel();
        _oauthCts = null;
    }

    // ── Add a self-managed host ──────────────────────────────────────────────────────

    private void OpenHostCard(string? prefillUrl)
    {
        _hostCard = new HostCard { Url = prefillUrl ?? "https://" };
        MarkDirty();
    }

    private void CancelHostCard() => _hostCard = null;

    private Element BuildHostCard(HostCard card)
    {
        var box = Flex.Column(gap: 12)
            .Padding(new Thickness(14)).CornerRadius(Nocturne.RadiusMd)
            .Background(Nocturne.Bg).BorderColor(Nocturne.Divider).BorderWidth(1f);
        box.AutomationId = "gitlab-host-card";
        box.AddChild(Text.New("Add a self-managed host").FontSize(13f).FontWeight(FontWeight.Medium));

        box.AddChild(HostField("Host URL",
            HostInput(card.Url, "https://gitlab.example.com", v => EditHostCard(card, () => card.Url = v), masked: false, "gitlab-host-url")));

        var tokenRow = Flex.Row(gap: 8).AlignItems(AlignItems.Center);
        tokenRow.AddChild(HostInput(card.Token, "glpat-… (paste)", v => EditHostCard(card, () => card.Token = v), masked: true, "gitlab-host-token")
            .FlexGrow(1).FlexShrink(1));
        bool canTest = card.Token.Trim().Length > 0 && card.State != HostCardState.Testing;
        var test = canTest
            ? Ui.SecondaryButton("Test", Icons.PlugsConnected, TestHostCard, 12f)
            : card.State == HostCardState.Testing ? BusyButton("Testing…") : DisabledButton("Test", Icons.PlugsConnected);
        test.Key = "gitlab-host-test-" + (canTest ? "on" : "off");
        test.AccessibleName = "Test";
        tokenRow.AddChild(test.FlexShrink(0));
        var tokenField = HostField("Personal access token", tokenRow);
        tokenField.AddChild(Text.New("Needs the api scope (read_api for read-only). Create one under /-/user_settings/personal_access_tokens. Self-managed hosts have no OAuth app registered for Junction, so a token is the zero-setup path.")
            .FontSize(11f).TextColor(Nocturne.On(0.5)));
        box.AddChild(tokenField);

        if (card.State == HostCardState.Verified && card.Verified is { } v)
        {
            var ok = HostResultRow(Icons.CheckCircle, Nocturne.DiffAddSign, Nocturne.DiffAddBg);
            ok.AddChild(Text.New("Verified —").FontSize(12f));
            ok.AddChild(Text.New(v.User.Name).FontSize(12f).FontWeight(FontWeight.SemiBold));
            var expiry = v.ExpiresAt is { } e
                ? $"expires in {Math.Max(0, (int)Math.Floor((e - DateTimeOffset.UtcNow).TotalDays))} days"
                : "never expires";
            ok.AddChild(Text.New($"@{v.User.Username} · {expiry} · scopes {string.Join(", ", v.Scopes)}")
                .FontSize(12f).FlexShrink(1).OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
            ok.AutomationId = "gitlab-host-verified";
            box.AddChild(ok);
        }
        else if (card.State == HostCardState.Failed && card.Error is { } error)
        {
            var bad = HostResultRow(Icons.XCircle, Nocturne.DiffDelSign, Nocturne.DiffDelBg).AlignItems(AlignItems.FlexStart);
            bad.AddChild(Text.New(error).FontSize(12f).FlexShrink(1));
            bad.AutomationId = "gitlab-host-failed";
            box.AddChild(bad);
        }

        var footer = Flex.Row(gap: 8).AlignItems(AlignItems.Center).JustifyContent(JustifyContent.FlexEnd);
        footer.AddChild(Ui.SecondaryButton("Cancel", null, () => { CancelHostCard(); MarkDirty(); }, 12f));
        bool canAdd = card.State == HostCardState.Verified && card.Verified != null;
        var add = canAdd
            ? Ui.PrimaryButton("Add account", Icons.Plus, AddHostAccount, 12f)
            : DisabledButton("Add account", Icons.Plus);
        add.Key = "gitlab-host-add-" + (canAdd ? "on" : "off");
        add.AccessibleName = "Add account";
        footer.AddChild(add);
        box.AddChild(footer);

        return Flex.Column(gap: 0).Padding(new Thickness(0, 0, 10, 0)).Children(box);
    }

    private static BoxElement HostField(string label, Element control)
    {
        var col = Flex.Column(gap: 5);
        col.AddChild(Text.New(label).FontSize(12f).TextColor(Nocturne.On(0.7)));
        col.AddChild(control);
        return col;
    }

    private static BoxElement HostResultRow(string glyph, Color fg, Color bg)
    {
        var row = Flex.Row(gap: 8).AlignItems(AlignItems.Center)
            .Padding(new Thickness(8, 10, 8, 10)).CornerRadius(Nocturne.RadiusSm)
            .Background(bg).BorderColor(Nocturne.Alpha(fg, 0.35)).BorderWidth(1f);
        row.AddChild(Ui.Icon(glyph, 15f, fg).FlexShrink(0));
        return row;
    }

    private Element HostInput(string value, string placeholder, Action<string> onChange, bool masked, string automationId)
    {
        var input = TextInput.New(value).Style(Ui.Field(background: Nocturne.Surface))
            .PlaceholderText(placeholder)
            .OnTextChanged(onChange)
            .Height(Dimension.Px(36)).FlexShrink(0)
            .FontFamily(Fonts.Mono).FontSize(12f).TextColor(Nocturne.Text);
        input.Masked = masked;
        input.AutomationId = automationId;
        input.On<KeyDownEvent>(e =>
        {
            if (e.Key != KeyCode.Enter) return;
            e.StopPropagation = true;
            if (_hostCard is { State: HostCardState.Verified }) AddHostAccount();
            else TestHostCard();
        });
        return input;
    }

    /// <summary>Any edit puts the card back to idle: a verdict belongs to the text it was
    /// given, and this bumps the serial so a Test still in flight is dropped.</summary>
    private void EditHostCard(HostCard card, Action apply)
    {
        apply();
        card.Serial++;
        if (card.State != HostCardState.Idle)
        {
            card.State = HostCardState.Idle;
            card.Verified = null;
            card.Error = null;
        }
        MarkDirty();
    }

    private async void TestHostCard()
    {
        if (_hostCard is not { } card || card.State == HostCardState.Testing || card.Token.Trim().Length == 0) return;
        card.State = HostCardState.Testing;
        card.Error = null;
        card.Verified = null;
        int serial = ++card.Serial;
        MarkDirty();

        var url = card.Url;
        var token = card.Token;
        var res = await Task.Run(() => _gitlab.VerifyTokenAsync(url, token));
        if (_hostCard != card || card.Serial != serial) return;   // edited or closed meanwhile

        if (res.Ok)
        {
            card.State = HostCardState.Verified;
            card.Verified = res.Value;
        }
        else
        {
            card.State = HostCardState.Failed;
            card.Error = HostCardFailureCopy(res.Failure!, url);
        }
        MarkDirty();
    }

    /// <summary>The design's three failure sentences, chosen by what the check found.</summary>
    private static string HostCardFailureCopy(GitLabFailure f, string url)
    {
        string web, host;
        try { web = GitLabClient.NormalizeApiBase(url)[..^"/api/v4".Length]; host = new Uri(web).Host; }
        catch (UriFormatException) { web = url; host = url; }
        return f.Kind switch
        {
            GitLabFailureKind.Unauthorized =>
                $"401 — {host} rejected the token. Paste a personal access token from {web}/-/user_settings/personal_access_tokens.",
            GitLabFailureKind.Forbidden => f.Message,
            GitLabFailureKind.Invalid or GitLabFailureKind.NotFound =>
                $"Not a GitLab host — {url} did not answer /api/v4/user. Check the URL (include https://).",
            GitLabFailureKind.Network or GitLabFailureKind.Timeout =>
                $"{f.Message}. Check the URL (include https://).",
            _ => f.Message,
        };
    }

    private void AddHostAccount()
    {
        if (_hostCard is not { State: HostCardState.Verified, Verified: { } verified }) return;
        var account = _gitlab.AddVerified(verified);
        _hostCard = null;
        _gitlabAuthFailed.Remove(account.Host);
        ForgetFirstRunDismissal(account.Host);
        Flash($"Added {account.Host} as @{account.Username}");
        Rerender();
    }

    // ── Sign out / rotate ────────────────────────────────────────────────────────────

    private void PromptSignOut(string host) => OpenConfirm(
        $"Sign out of {host}?",
        $"Merge requests, pipelines and review threads for repositories on {host} stop showing until you sign in again. " +
        "Nothing local changes; the token is removed from the credential store.",
        "Sign out",
        () => SignOut(host));

    private async void SignOut(string host)
    {
        // The revoke goes over the network off the UI thread; the forgetting touches the
        // settings list the build reads, so it waits for the continuation to land back here.
        await Task.Run(() => _gitlab.RevokeAsync(host));
        _gitlab.Forget(host);
        _gitlabAuthFailed.Remove(host);
        ForgetFirstRunDismissal(host);
        Flash($"Signed out of {host}");
        Rerender();
    }

    private async void RotateToken(string host)
    {
        var res = await Task.Run(() => _gitlab.RotateTokenAsync(host));
        if (res.Ok)
            Flash(res.Value.ExpiresAt is { } e ? $"Token rotated — now expires {e:yyyy-MM-dd}" : "Token rotated");
        else
            Flash($"Could not rotate the token: {res.Failure!.Message}");
        MarkDirty();
    }

    // ── Detected projects ────────────────────────────────────────────────────────────

    private Element BuildDetectedProjects()
    {
        var col = Flex.Column(gap: 0).Padding(new Thickness(16, 0, 0, 0));
        var inner = Flex.Column(gap: 8).Padding(new Thickness(14, 0, 0, 0))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(1, 0, 0, 0));
        inner.AddChild(Text.New("DETECTED PROJECTS").FontSize(10f).TextColor(Nocturne.On(0.42)));

        var rows = Flex.Column(gap: 2);
        int shown = 0;
        foreach (var path in _settings.RepositoryPaths)
        {
            if (GitLabRemoteFor(path) is not { } remote) continue;
            shown++;
            bool connected = _gitlab.HasAccount(remote.Host);
            var name = System.IO.Path.GetFileName(path.TrimEnd('/', '\\'));
            var (bg, fg) = RepoTile(path);
            var row = Flex.Row(gap: 10).AlignItems(AlignItems.Center).Padding(new Thickness(5, 4, 5, 4));
            row.AddChild(Ui.InitialTile(name, bg, fg, 20f, 9f));
            row.AddChild(Text.New(name).FontSize(12f).Width(130).OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
            row.AddChild(Ui.Icon(Icons.GitLabLogo, 12f, connected ? Nocturne.Accent300 : Nocturne.On(0.30)));
            row.AddChild(Ui.Mono(remote.ProjectPath, 12f, Nocturne.On(0.65)).FlexShrink(1)
                .OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
            row.AddChild(Text.New($"on {remote.Host}").FontSize(12f).TextColor(Nocturne.On(0.40)));
            row.AddChild(Box.New().FlexGrow(1));
            row.AddChild(Text.New(connected ? "connected" : "no account for this host").FontSize(11f)
                .TextColor(connected ? Nocturne.DiffAddSign : Nocturne.On(0.45)));
            rows.AddChild(row);
        }
        if (shown == 0)
            rows.AddChild(Text.New("None of the repositories in the sidebar has a GitLab remote.")
                .FontSize(12f).TextColor(Nocturne.On(0.45)).Padding(new Thickness(5, 4, 5, 4)));
        inner.AddChild(rows);
        inner.AddChild(Text.New("A repository is GitLab-backed when its origin points at a host above. Override the remote in Repository settings.")
            .FontSize(11f).TextColor(Nocturne.On(0.45)));
        col.AddChild(inner);
        return col;
    }
}
