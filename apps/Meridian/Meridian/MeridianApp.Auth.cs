using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using Meridian.Core;
using Meridian.Theme;

namespace Meridian;

public partial class MeridianApp
{
    private OAuth2 _oauth = null!;

    /// <summary>The auth a tab really uses, and the folder it came from when
    /// inherited — what the inheritance bar reports.</summary>
    internal (AuthSpec Spec, string? InheritedFrom) EffectiveAuth(RequestTab tab) =>
        Auth.Effective(tab.Path, tab.Doc, _workspace?.Root ?? Path.GetDirectoryName(tab.Path) ?? ".",
            path => TryReadFile(path) is { } text ? ReqDocument.Parse(text) : null);

    /// <summary>
    /// Applies auth to a request being sent: the header (or query pair), with
    /// OAuth2 fetching a token first. Returns an error string when auth cannot
    /// be satisfied — the send stops rather than going out unauthenticated and
    /// puzzling someone with a 401.
    /// </summary>
    private async Task<string?> ApplyAuthAsync(
        RequestTab tab, VariableResolver resolver,
        List<(string, string)> headers, CancellationToken ct)
    {
        var (spec, _) = EffectiveAuth(tab);
        if (spec.Kind == AuthKind.None) return null;

        string? token = null;
        if (spec.Kind == AuthKind.OAuth2)
        {
            var (fetched, error) = await _oauth.TokenFor(spec, resolver, DateTimeOffset.Now, ct);
            if (error != null) return error;
            token = fetched?.AccessToken;
        }

        if (Auth.HeaderFor(spec, resolver, token, out var unresolved) is { } header)
        {
            if (unresolved.Count > 0)
                return $"auth needs {string.Join(", ", unresolved.Distinct().Select(n => "{{" + n + "}}"))}";
            // An explicit header on the request wins: someone who wrote
            // "header Authorization: …" meant it.
            if (!headers.Any(h => h.Item1.Equals(header.Name, StringComparison.OrdinalIgnoreCase)))
                headers.Add(header);
        }
        return null;
    }

    /// <summary>The query pair <c>apikey query</c> adds, appended to the URL
    /// after variable resolution.</summary>
    private string ApplyAuthQuery(RequestTab tab, VariableResolver resolver, string url)
    {
        var (spec, _) = EffectiveAuth(tab);
        if (Auth.QueryFor(spec, resolver) is not { } pair) return url;
        var separator = url.Contains('?') ? '&' : '?';
        return $"{url}{separator}{Uri.EscapeDataString(pair.Name)}={Uri.EscapeDataString(pair.Value)}";
    }

    // ── The Auth sub-tab ─────────────────────────────────────────────────

    private Element BuildAuthEditor(RequestTab tab)
    {
        var (spec, inheritedFrom) = EffectiveAuth(tab);
        var scroll = ScrollView.New();
        var col = Flex.Column(gap: Azimuth.S2).Padding(new Thickness(Azimuth.S2, Azimuth.S3, Azimuth.S3, Azimuth.S3));

        if (inheritedFrom is { } folder)
            col.AddChild(BuildInheritanceBar(tab, folder, spec));

        col.AddChild(BuildAuthTypePicker(tab, spec, inheritedFrom != null));

        switch (spec.Kind)
        {
            case AuthKind.Basic:
                col.AddChild(AuthField("user", spec.A, v => SetAuth(tab, spec with { A = v }), "auth-user"));
                col.AddChild(AuthField("password", spec.B, v => SetAuth(tab, spec with { B = v }), "auth-password"));
                break;
            case AuthKind.Bearer:
                col.AddChild(AuthField("token", spec.A, v => SetAuth(tab, spec with { A = v }), "auth-token"));
                break;
            case AuthKind.ApiKey:
                col.AddChild(Flex.Row(gap: Azimuth.S2).AlignItems(AlignItems.Center).Children(
                    AuthPlacementChip(tab, spec, "header"),
                    AuthPlacementChip(tab, spec, "query")));
                col.AddChild(AuthField("name", spec.B, v => SetAuth(tab, spec with { B = v }), "auth-key-name"));
                col.AddChild(AuthField("value", spec.C, v => SetAuth(tab, spec with { C = v }), "auth-key-value"));
                break;
            case AuthKind.OAuth2:
                col.AddChild(Ui.Mono("grant type: client_credentials", 10.5f, Azimuth.TextMuted));
                col.AddChild(AuthField("token URL", spec.A, v => SetAuth(tab, spec with { A = v }), "auth-token-url"));
                col.AddChild(AuthField("client id", spec.B, v => SetAuth(tab, spec with { B = v }), "auth-client-id"));
                col.AddChild(AuthField("client secret", spec.C, v => SetAuth(tab, spec with { C = v }), "auth-client-secret"));
                col.AddChild(AuthField("scopes", spec.Scopes, v => SetAuth(tab, spec with { Scopes = v }), "auth-scopes"));
                col.AddChild(BuildTokenChip(spec));
                break;
            default:
                col.AddChild(Text.New("This request sends no credentials.")
                    .FontSize(11.5f).TextColor(Azimuth.TextMuted));
                break;
        }

        col.AddChild(Box.New().Height(Azimuth.S3));
        col.AddChild(Ui.Mono(
            inheritedFrom != null
                ? $"editing writes {Path.GetFileName(inheritedFrom)}/_folder.req"
                : "auth config serializes into this .req file · secrets stay in the environment and keyring",
            10f, Azimuth.TextFaint).AutomationId("auth-footer"));

        scroll.AddChild(col);
        return scroll;
    }

    /// <summary>The anatomy lesson: where auth comes from, and one action to
    /// take it over for this request.</summary>
    private Element BuildInheritanceBar(RequestTab tab, string folder, AuthSpec spec)
    {
        var bar = Flex.Row(gap: Azimuth.S2).Height(30).FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(0, Azimuth.S2, 0, Azimuth.S2))
            .CornerRadius(Azimuth.Radius1)
            .Background(Azimuth.Tint(Azimuth.Accent));
        // A role, or the container never reaches the accessibility tree and
        // FindByAutomationId cannot see it.
        bar.AccessibleRole(AccessibilityRole.Label)
            .AccessibleName($"auth inherited from {Path.GetFileName(folder)}");
        bar.AutomationId("auth-inheritance");
        bar.AddChild(Ui.Mono("⤴", 11f, Azimuth.Accent300));
        bar.AddChild(Text.New($"inherited from {Path.GetFileName(folder)}/ — {KindLabel(spec.Kind)}")
            .FontSize(11.5f).TextColor(Azimuth.TextDim));
        bar.AddChild(Box.New().FlexGrow(1));
        bar.AddChild(BarAction("override for this request", "auth-override", () =>
        {
            // Copy the inherited config down, so overriding starts from what
            // was actually in effect rather than from nothing.
            tab.Doc.SetAuth(Auth.Format(spec));
            MarkDirty();
        }));
        return bar;
    }

    private Element BuildAuthTypePicker(RequestTab tab, AuthSpec spec, bool inherited)
    {
        var row = Flex.Row(gap: Azimuth.S1).AlignItems(AlignItems.Center);
        foreach (var kind in new[] { AuthKind.None, AuthKind.Basic, AuthKind.Bearer, AuthKind.ApiKey, AuthKind.OAuth2 })
        {
            bool active = spec.Kind == kind;
            var b = Box.New()
                .Padding(new Thickness(4, Azimuth.S3, 4, Azimuth.S3))
                .CornerRadius(Azimuth.Radius1)
                .BorderColor(active ? Azimuth.Accent : Azimuth.Border).BorderWidth(1f)
                .Cursor(CursorType.Pointer)
                .AccessibleRole(AccessibilityRole.RadioButton)
                .AccessibleName($"{KindLabel(kind)}{(active ? ", selected" : "")}")
                .AutomationId("auth-kind-" + kind.ToString().ToLowerInvariant())
                .Children(Ui.ChipLabel(KindLabel(kind), 11f, active ? Azimuth.Accent300 : Azimuth.TextMuted));
            if (active) b.Background(Azimuth.Tint(Azimuth.Accent));
            var captured = kind;
            b.OnClick(_ => SetAuth(tab, Default(captured)));
            row.AddChild(b);
        }
        return row;
    }

    internal static string KindLabel(AuthKind kind) => kind switch
    {
        AuthKind.None => "None",
        AuthKind.Basic => "Basic",
        AuthKind.Bearer => "Bearer",
        AuthKind.ApiKey => "API key",
        _ => "OAuth2",
    };

    /// <summary>A fresh spec of a kind. Values default to variable chips, which
    /// nudges secrets toward the environment instead of pasted literals.</summary>
    internal static AuthSpec Default(AuthKind kind) => kind switch
    {
        AuthKind.Basic => new AuthSpec(AuthKind.Basic, "{{user}}", "{{password}}"),
        AuthKind.Bearer => new AuthSpec(AuthKind.Bearer, "{{token}}"),
        AuthKind.ApiKey => new AuthSpec(AuthKind.ApiKey, "header", "X-Api-Key", "{{api_key}}"),
        AuthKind.OAuth2 => new AuthSpec(AuthKind.OAuth2,
            "{{base_url}}/oauth/token", "{{client_id}}", "{{client_secret}}"),
        _ => AuthSpec.None,
    };

    /// <summary>Writes the spec where it belongs: the request's own file, or
    /// the folder file it was inherited from.</summary>
    private void SetAuth(RequestTab tab, AuthSpec spec)
    {
        var (_, inheritedFrom) = EffectiveAuth(tab);
        if (inheritedFrom is { } folder && tab.Doc.Auth == null)
        {
            var file = Path.Combine(folder, "_folder.req");
            if (TryReadFile(file) is { } text)
            {
                var doc = ReqDocument.Parse(text);
                doc.SetAuth(Auth.Format(spec));
                try
                {
                    File.WriteAllText(file, doc.Serialize());
                    RescanWorkspace();
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    Flash($"Could not write _folder.req: {e.Message}");
                }
            }
        }
        else
        {
            tab.Doc.SetAuth(Auth.Format(spec));
        }
        // A changed client or endpoint invalidates any token we hold.
        _oauth.Forget(spec);
        MarkDirty();
    }

    private Element AuthField(string label, string value, Action<string> onChanged, string automationId)
    {
        var row = Flex.Row(gap: Azimuth.S2).Height(Azimuth.RowForm).FlexShrink(0).AlignItems(AlignItems.Center);
        row.AddChild(Text.New(label).FontSize(11.5f).TextColor(Azimuth.TextMuted).Width(110).FlexShrink(0));
        row.AddChild(FormInput(value, onChanged, 0, automationId));
        return row;
    }

    private Element AuthPlacementChip(RequestTab tab, AuthSpec spec, string placement)
    {
        bool active = spec.A == placement;
        var chip = Box.New()
            .Padding(new Thickness(3, Azimuth.S2, 3, Azimuth.S2))
            .CornerRadius(Azimuth.Radius1)
            .BorderColor(active ? Azimuth.Accent : Azimuth.Border).BorderWidth(1f)
            .Cursor(CursorType.Pointer)
            .AccessibleRole(AccessibilityRole.RadioButton)
            .AccessibleName($"in {placement}{(active ? ", selected" : "")}")
            .AutomationId("auth-in-" + placement)
            .Children(Ui.ChipLabel(placement, 10f, active ? Azimuth.Accent300 : Azimuth.TextMuted));
        if (active) chip.Background(Azimuth.Tint(Azimuth.Accent));
        chip.OnClick(_ => SetAuth(tab, spec with { A = placement }));
        return chip;
    }

    /// <summary>The token chip trio — valid / expiring / expired — so a 401 is
    /// never the first sign that a token aged out.</summary>
    private Element BuildTokenChip(AuthSpec spec)
    {
        var now = DateTimeOffset.Now;
        var token = _oauth.Cached(spec);
        var (text, color) = token == null
            ? ("no token yet · fetched on send", Azimuth.TextMuted)
            : token.IsValid(now)
                ? ($"● token valid · expires in {Humanize(token.Remaining(now))}", Azimuth.StatusPass)
                : token.Remaining(now) > TimeSpan.Zero
                    ? ($"● expires in {Humanize(token.Remaining(now))} · auto-refresh on send", Azimuth.StatusWarn)
                    : ($"● expired {Humanize(-token.Remaining(now))} ago", Azimuth.StatusFail);

        var row = Flex.Row(gap: Azimuth.S2).AlignItems(AlignItems.Center);
        row.AddChild(Ui.Mono(text, 10.5f, color).AutomationId("auth-token-chip"));
        if (token != null)
            row.AddChild(BarAction("clear token", "auth-clear-token", () =>
            {
                _oauth.Forget(spec);
                MarkDirty();
            }));
        return row;
    }

    internal static string Humanize(TimeSpan span) => span.TotalMinutes switch
    {
        < 1 => $"{Math.Max(span.TotalSeconds, 0):0} s",
        < 60 => $"{span.TotalMinutes:0} m",
        _ => $"{span.TotalHours:0.#} h",
    };
}
