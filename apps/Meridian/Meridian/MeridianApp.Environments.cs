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

public partial class MeridianApp
{
    private const int RevealSeconds = 5;

    // ── The Environments view (5): the compare table IS the editor ───────

    private Element BuildEnvironmentsRealView()
    {
        if (_workspace == null)
            return EmptyView("Environments", "no environments",
                "Open a collection to edit its envs/*.env files side by side.");
        if (_envFiles.Count == 0)
        {
            var empty = EmptyView("Environments", "no environments",
                $"An environment holds the variables for one place the API runs, saved as envs{System.IO.Path.DirectorySeparatorChar}<name>.env.");
            empty.AddChild(Box.New().Height(Azimuth.S2));
            empty.AddChild(Ui.Button(ButtonVariant.Primary, "New environment…", OpenNewEnvironmentDialog, "new-env",
                style: Ui.Dialog));
            return empty;
        }

        var row = Flex.Row(gap: 0).FlexGrow(1).AlignItems(AlignItems.Stretch);
        var list = BuildEnvFileList(); list.Key = "env-list";
        var table = BuildCompareTable(); table.Key = "env-table";
        row.AddChild(list);
        row.AddChild(table);
        return row;
    }

    private Element BuildEnvFileList()
    {
        var panel = Flex.Column(gap: 0).FlexShrink(0)
            .Width(Azimuth.TreeWidth)
            .BorderColor(Azimuth.Border).BorderWidth(new Thickness(0, 0, 0, 1));

        panel.AddChild(Flex.Row(gap: 0).Height(36).FlexShrink(0)
            .AlignItems(AlignItems.Center)
            .Padding(new Thickness(0, Azimuth.S3, 0, Azimuth.S3))
            .Children(Text.New("Environments").FontSize(13f).FontWeight(FontWeight.Medium).TextColor(Azimuth.TextDim)));

        foreach (var env in _envFiles)
        {
            bool active = env.Name == _envName;
            int missing = MissingCount(env.Name);
            var r = Flex.Row(gap: Azimuth.S2).Height(Azimuth.RowList).FlexShrink(0)
                .AlignItems(AlignItems.Center)
                .Padding(new Thickness(0, Azimuth.S2, 0, Azimuth.S3))
                .CornerRadius(Azimuth.Radius1)
                .BorderColor(active ? Azimuth.Accent : Color.Transparent)
                .BorderWidth(new Thickness(2, 0, 0, 0))
                .Cursor(CursorType.Pointer)
                .AccessibleRole(AccessibilityRole.Button)
                .AccessibleName($"{env.Name}{(active ? ", active" : "")}{(missing > 0 ? $", {missing} missing" : "")}")
                .AutomationId("env-file-" + env.Name);
            if (active) r.Background(Azimuth.Raised);
            r.AddChild(Ui.Mono(env.Name + ".env", 12f, active ? Azimuth.Text : Azimuth.TextDim));
            if (_envDocs.GetValueOrDefault(env.Name)?.Edited == true) r.AddChild(Dot(Azimuth.Accent));
            r.AddChild(Box.New().FlexGrow(1));
            if (active)
                r.AddChild(Text.New("active").FontSize(10f).TextColor(Azimuth.Accent300));
            if (missing > 0)
                r.AddChild(Text.New($"! {missing} missing").FontSize(10f).TextColor(Azimuth.StatusWarn));
            var name = env.Name;
            // Rename and delete: the ⋯ at the row's end, or a right-click on the row.
            r.AddChild(RowActionsButton($"actions for {name}.env", "env-actions-" + name, () => EnvironmentActions(name)));
            r.OnClick(_ => SelectEnvironment(name));
            RowContextMenu(r, () => EnvironmentActions(name));
            if (!active) r.HoverBg(Azimuth.Raised, Color.Transparent);
            panel.AddChild(r);
        }

        panel.AddChild(Box.New().Height(Azimuth.S1));
        panel.AddChild(Flex.Row(gap: 0).FlexShrink(0).Padding(new Thickness(0, Azimuth.S1, 0, Azimuth.S1))
            .Children(AddRowButton("＋ new environment", "new-env", OpenNewEnvironmentDialog)));

        panel.AddChild(Box.New().FlexGrow(1));
        panel.AddChild(Flex.Row(gap: 0).Height(28).FlexShrink(0)
            .AlignItems(AlignItems.Center)
            .Padding(new Thickness(0, Azimuth.S3, 0, Azimuth.S3))
            .BorderColor(Azimuth.Border).BorderWidth(new Thickness(0, 0, 1, 0))
            .Children(Ui.Mono($"{_workspace!.Name}{System.IO.Path.DirectorySeparatorChar}envs{System.IO.Path.DirectorySeparatorChar}", 10.5f, Azimuth.TextFaint)));
        return panel;
    }

    /// <summary>Keys the active env is missing relative to the union — what the
    /// file list's warn badge counts.</summary>
    private int MissingCount(string envName)
    {
        var doc = _envDocs.GetValueOrDefault(envName);
        if (doc == null) return 0;
        return AllKeys().Count(k => !doc.Keys.Contains(k));
    }

    /// <summary>The union of keys across every environment, in first-appearance
    /// order walking the files in name order.</summary>
    private List<string> AllKeys()
    {
        var keys = new List<string>();
        foreach (var env in _envFiles)
            if (_envDocs.GetValueOrDefault(env.Name) is { } doc)
                foreach (var key in doc.Keys)
                    if (!keys.Contains(key))
                        keys.Add(key);
        return keys;
    }

    private bool IsSecretAnywhere(string key) =>
        _envFiles.Any(e => _envDocs.GetValueOrDefault(e.Name)?.IsSecret(key) == true);

    private Element BuildCompareTable()
    {
        var col = Flex.Column(gap: 0).FlexGrow(1);

        col.AddChild(Flex.Row(gap: Azimuth.S3).Height(40).FlexShrink(0)
            .AlignItems(AlignItems.Center)
            .Padding(new Thickness(0, Azimuth.S3, 0, Azimuth.S3))
            .Children(
                // One line each in a 40px bar: the title keeps its width, the note truncates.
                Text.New("All keys × all environments").FontSize(13f).FontWeight(FontWeight.Medium).TextColor(Azimuth.TextDim)
                    .MaxLines(1).FlexShrink(0),
                Ui.Mono("plain text on disk · secret values never leave the OS keyring", 10.5f, Azimuth.TextFaint)
                    .MaxLines(1).TextOverflow(TextOverflow.Ellipsis)));

        // Header row.
        var header = Flex.Row(gap: Azimuth.S2).Height(28).FlexShrink(0)
            .AlignItems(AlignItems.Center)
            .Padding(new Thickness(0, Azimuth.S3, 0, Azimuth.S3))
            .BorderColor(Azimuth.Border).BorderWidth(new Thickness(0, 0, 1, 0));
        header.AddChild(Ui.Mono("KEY", 10f, Azimuth.TextMuted).Width(170).FlexShrink(0));
        header.AddChild(Ui.Mono("SECRET", 10f, Azimuth.TextMuted).Width(52).FlexShrink(0));
        foreach (var env in _envFiles)
            header.AddChild(Flex.Row(gap: 4).FlexGrow(1).FlexBasis(Dimension.Px(0)).AlignItems(AlignItems.Center)
                .Children(
                    Ui.Mono(env.Name, 10f, env.Name == _envName ? Azimuth.Accent300 : Azimuth.TextMuted),
                    env.Name == _envName
                        ? Text.New("● active").FontSize(9f).TextColor(Azimuth.Accent300)
                        : Box.New()));
        header.AddChild(Box.New().Width(22).FlexShrink(0));   // over the rows' ✕
        col.AddChild(header);

        var scroll = ScrollView.New();
        var rows = Flex.Column(gap: 2).Padding(new Thickness(Azimuth.S1, Azimuth.S3, Azimuth.S3, Azimuth.S3));
        foreach (var key in AllKeys())
            rows.AddChild(BuildKeyRow(key));

        rows.AddChild(AddRowButton("＋ add key", "add-env-key", () =>
            OpenDialog("New key", "key_name", name =>
            {
                if (_envName is { } active && name.Trim() is { Length: > 0 } trimmed)
                {
                    _envDocs[active].SetValue(trimmed, "");
                    MarkDirty();
                }
            })));
        scroll.AddChild(rows);
        var scrollBox = Box.New().FlexGrow(1).FlexBasis(Dimension.Px(0));
        scrollBox.AddChild(scroll);
        col.AddChild(scrollBox);

        // Footer: the save line for every dirty column.
        int dirty = _envFiles.Count(e => _envDocs.GetValueOrDefault(e.Name)?.Edited == true);
        var footer = Flex.Row(gap: Azimuth.S2).Height(26).FlexShrink(0)
            .AlignItems(AlignItems.Center)
            .Padding(new Thickness(0, Azimuth.S3, 0, Azimuth.S3))
            .Background(Azimuth.Surface)
            .BorderColor(Azimuth.Border).BorderWidth(new Thickness(0, 1, 0, 0));
        footer.AddChild(Ui.Mono("envs/*.env", 10.5f, Azimuth.TextMuted));
        footer.AddChild(Box.New().FlexGrow(1));
        footer.AddChild(dirty > 0
            ? Flex.Row(gap: 4).AlignItems(AlignItems.Center).Children(
                Dot(Azimuth.Accent),
                Text.New($"{dirty} unsaved").FontSize(11f).TextColor(Azimuth.TextDim).AutomationId("env-save-state"))
            : Text.New("saved").FontSize(11f).TextColor(Azimuth.TextFaint).AutomationId("env-save-state"));
        footer.AddChild(Ui.Kbd(Shortcuts.Keys("save")));
        col.AddChild(footer);
        return col;
    }

    private Element BuildKeyRow(string key)
    {
        bool secret = IsSecretAnywhere(key);
        var row = Flex.Row(gap: Azimuth.S2).Height(Azimuth.RowForm).FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(0, Azimuth.S3, 0, Azimuth.S3));
        if (secret) row.Background(Azimuth.Alpha(Azimuth.Accent, 0.04));

        row.AddChild(Ui.Mono(key, 11.5f, Azimuth.TextDim).Width(170).FlexShrink(0));

        // The flag is a toggle: on writes `secret <key>` into every file that has the key, off
        // takes it out again, first asking what becomes of a value this machine has stored
        // (AskUnmarkSecret). Either waits for Ctrl+S with the table's other edits.
        var flag = Ui.Button(ButtonVariant.Ghost, secret ? "●" : "○",
                () => { if (secret) AskUnmarkSecret(key); else SetSecret(key, true); },
                $"secret-toggle-{key}", style: secret ? Ui.FlagOn : Ui.FlagOff)
            .Tooltip(secret ? "secret — the value lives in the OS keyring. Click to store it in the file instead"
                : "mark secret — keep the value in the OS keyring, not the file");
        flag.AccessibleName = $"secret {key}";
        flag.AccessibleToggled = secret;
        row.AddChild(Box.New().Width(52).FlexShrink(0).Children(flag));

        foreach (var env in _envFiles)
            row.AddChild(BuildCell(env.Name, key, secret));

        var remove = RemoveButton($"remove {key} from every environment", () => RemoveKey(key))
            .Tooltip(secret ? "remove from every environment — and, on save, its value from this machine's keyring"
                : "remove from every environment");
        remove.AutomationId($"remove-key-{key}");
        row.AddChild(remove);
        return row;
    }

    /// <summary>Flags or un-flags a key in every environment that has it. Un-flagged, the key
    /// resolves from the file again, and this machine's keyring value goes on the save that writes
    /// the files without the flag (<see cref="ForgetRemovedSecrets"/>), so no value is left behind
    /// in the keyring with nothing to show it. <see cref="AskUnmarkSecret"/> asks first.</summary>
    internal void SetSecret(string key, bool secret)
    {
        if (!secret && IsSecretAnywhere(key)) _removedSecrets.Add(key);
        foreach (var env in _envFiles)
            if (_envDocs.GetValueOrDefault(env.Name) is { } doc && doc.Keys.Contains(key))
            {
                if (secret) doc.MarkSecret(key);
                else doc.UnmarkSecret(key);
            }
        MarkDirty();
    }

    /// <summary>Takes a key out of every environment. A secret's keyring value goes too, on the
    /// save that writes the files without it (<see cref="ForgetRemovedSecrets"/>): nothing will
    /// ask for it again, and an entry left behind would come back if the key were added anew.</summary>
    private void RemoveKey(string key)
    {
        if (IsSecretAnywhere(key)) _removedSecrets.Add(key);
        foreach (var env in _envFiles)
            _envDocs.GetValueOrDefault(env.Name)?.RemoveKey(key);
        MarkDirty();
    }

    /// <summary>Secret keys removed from every environment, or un-flagged, since the last save.</summary>
    private readonly HashSet<string> _removedSecrets = new(StringComparer.Ordinal);

    /// <summary>Forgets the keyring values of the removed and un-flagged secrets — the shared one and
    /// every environment's own — once every file is saved without their flag. A key flagged again
    /// before the save keeps its values; a file that failed to save keeps them waiting for the next
    /// save.</summary>
    private void ForgetRemovedSecrets()
    {
        if (_workspace is not { } ws || _removedSecrets.Count == 0) return;
        if (_envFiles.Any(e => _envDocs.GetValueOrDefault(e.Name)?.Edited == true)) return;
        foreach (var key in _removedSecrets)
            if (!IsSecretAnywhere(key))
            {
                _keyring.Delete(ws.Root, null, key);
                foreach (var env in _envFiles) _keyring.Delete(ws.Root, env.Name, key);
            }
        _removedSecrets.Clear();
    }

    /// <summary>The keys an environment may hold keyring values for: every key in the table, and
    /// a removed secret whose values wait for the save.</summary>
    private IEnumerable<string> KeyringKeys() => AllKeys().Union(_removedSecrets);

    /// <summary>The keys <paramref name="envName"/> has a keyring value of its own for.</summary>
    private List<string> OwnSecrets(string envName) =>
        KeyringKeys().Where(k => _keyring.Get(_workspace!.Root, envName, k) != null).ToList();

    /// <summary>A renamed environment's own keyring values go with it, so its secrets resolve under
    /// the new name as they did under the old. A change of letter case alone forgets the entry
    /// first: a store that ignores case (Windows) would take the new name for the old.</summary>
    private void MoveEnvironmentSecrets(string from, string to)
    {
        var root = _workspace!.Root;
        bool caseOnly = string.Equals(from, to, StringComparison.OrdinalIgnoreCase);
        foreach (var key in KeyringKeys())
        {
            if (_keyring.Get(root, from, key) is not { } value)
            {
                // Left by an environment of the new name made and deleted outside the app.
                if (!caseOnly) _keyring.Delete(root, to, key);
                continue;
            }
            if (caseOnly)
            {
                _keyring.Delete(root, from, key);
                if (!_keyring.Set(root, to, key, value)) _keyring.Set(root, from, key, value);
            }
            else if (_keyring.Set(root, to, key, value))
                _keyring.Delete(root, from, key);
        }
    }

    /// <summary>A deleted environment's own keyring values are forgotten: an entry left behind would
    /// come back if an environment of that name were made again. The shared values stay; the
    /// others use them.</summary>
    private void ForgetEnvironmentSecrets(string envName)
    {
        foreach (var key in OwnSecrets(envName))
            _keyring.Delete(_workspace!.Root, envName, key);
    }

    private Element BuildCell(string envName, string key, bool secret)
    {
        var cell = Flex.Row(gap: Azimuth.S1).FlexGrow(1).FlexBasis(Dimension.Px(0)).AlignItems(AlignItems.Center);
        var doc = _envDocs.GetValueOrDefault(envName);
        if (doc == null) return cell;

        if (!doc.Keys.Contains(key))
        {
            // Missing here: the warn cell with the one-click fix. A secret arrives as its flag
            // alone, so this environment resolves it from the keyring like the others.
            var add = Ui.Button(ButtonVariant.Ghost, $"! missing — add to {envName}",
                    () => { if (secret) doc.MarkSecret(key); else doc.SetValue(key, ""); MarkDirty(); },
                    $"add-{key}-to-{envName}", style: Ui.WarnFill)
                .AccessibleName($"add {key} to {envName}");
            cell.AddChild(add);
            return cell;
        }

        if (secret)
        {
            cell.AddChild(doc.Values.GetValueOrDefault(key) is { Length: > 0 } plain
                ? BuildPlainSecretFix(doc, envName, key, plain)
                : BuildSecretCell(envName, key));
            return cell;
        }

        var input = TextInput.New(doc.Values.GetValueOrDefault(key, "")).Style(Ui.Field);
        // A value shows in this table alone; the title bar names the environment, not its values.
        // The box holds what was typed, and nothing else in the view reads a value, so a character
        // builds the view only when it makes the environment unsaved: the dot in the list and the
        // footer's count. A build of the table was 312 KB a character at 3 × 12 keys and 1.2 MB at
        // 5 × 40, each secret cell a keyring read (plan 17 round 9).
        input.OnTextChanged(v =>
            {
                bool wasEdited = doc.Edited;
                doc.SetValue(key, v);
                if (doc.Edited != wasEdited) MarkViewDirty();
            })
            .Height(Dimension.Px(26)).FlexGrow(1)
            .FontFamily(Fonts.Mono).FontSize(11.5f)
            .TextColor(Azimuth.Text);
        input.AutomationId($"cell-{key}-{envName}");
        cell.AddChild(input);
        return cell;
    }

    /// <summary>A secret's cell is per machine and per environment: masked when this environment
    /// resolves it (<see cref="Secrets.Resolve"/>), with the timed reveal and where the value comes
    /// from, the fresh-clone fix when it doesn't. Its menu changes or forgets the stored value, and
    /// takes the step between an environment's own value and the shared one
    /// (<see cref="SecretActions"/>).</summary>
    private Element BuildSecretCell(string envName, string key)
    {
        var ws = _workspace!;
        var cell = Flex.Row(gap: Azimuth.S1).FlexGrow(1).AlignItems(AlignItems.Center);

        if (Secrets.Resolve(_keyring, ws.Root, envName, key) is not { } secret)
        {
            var set = Ui.Button(ButtonVariant.Ghost, "! not set on this machine — set value",
                    () => OpenSetSecretDialog(envName, key), $"set-secret-{key}-{envName}", style: Ui.WarnFill)
                .AccessibleName($"set {key} for {envName} on this machine");
            cell.AddChild(set);
            return cell;
        }

        bool revealed = _reveal is { } r && r.Env == envName && r.Key == key
            && System.Diagnostics.Stopwatch.GetTimestamp() < r.UntilTs;
        var value = Flex.Column(gap: 0).FlexShrink(1).JustifyContent(JustifyContent.Center);
        value.AddChild(Ui.Mono(revealed ? secret.Value : "••••••••", 11.5f, revealed ? Azimuth.Text : Azimuth.TextMuted)
            .MaxLines(1).TextOverflow(TextOverflow.Ellipsis)
            .AutomationId($"secret-value-{key}-{envName}"));
        var (source, about) = secret.Source switch
        {
            SecretSource.Keyring => ("keyring", $"{envName}'s own value, in this machine's keyring"),
            SecretSource.SharedKeyring => ("shared", "the value every environment without its own uses, in this machine's keyring"),
            SecretSource.Variable => ("$" + Secrets.VariableName(envName, key), $"from the environment variable {Secrets.VariableName(envName, key)}"),
            _ => ("$" + key, $"from the environment variable {key}, for every environment"),
        };
        // Under the value rather than beside it, where a narrow column squeezed it out first.
        value.AddChild(Ui.Mono(source, 9f, Azimuth.TextFaint).MaxLines(1).TextOverflow(TextOverflow.Ellipsis)
            .Tooltip(about).AutomationId($"secret-source-{key}-{envName}"));
        cell.AddChild(value);
        cell.AddChild(Box.New().FlexGrow(1));
        if (!revealed)
            cell.AddChild(BarAction($"reveal ({RevealSeconds} s)", $"reveal-{key}-{envName}", () => RevealSecret(envName, key)).FlexShrink(0));
        // The rest in a menu, as on the file rows: a column holds one masked value, its source and
        // reveal, and a row of buttons or icons ran into the next column at a laptop's width.
        if (SecretActions(envName, key, secret) is { Count: > 0 } actions)
        {
            cell.AddChild(RowActionsButton($"{key} in {envName}", $"secret-actions-{key}-{envName}", () => actions,
                tooltip: secret.Source switch
                {
                    SecretSource.Keyring => "change · forget · use everywhere",
                    SecretSource.SharedKeyring => "set own value · change · forget the shared value",
                    _ => "set own value",
                }));
            RowContextMenu(cell, () => actions);
        }
        return cell;
    }

    /// <summary>What a stored value's menu offers. An own value is changed or forgotten for this
    /// environment, and — many secrets being one value everywhere — becomes the shared one in a
    /// click. A shared value is overridden with a value for this environment alone, which then
    /// wins, or changed or forgotten for every environment that uses it. A value from an
    /// environment variable is not Meridian's to change; it can only be overridden.</summary>
    private List<(string, Action)> SecretActions(string envName, string key, SecretValue secret)
    {
        var actions = new List<(string, Action)>();
        switch (secret.Source)
        {
            case SecretSource.Keyring:
                actions.Add(("Change…", () => OpenChangeSecretDialog(envName, key)));
                if (_envFiles.Any(e => e.Name != envName && _envDocs.GetValueOrDefault(e.Name)?.Keys.Contains(key) == true))
                    actions.Add(("Use everywhere", () => ShareSecret(envName, key)));
                actions.Add(("Forget", () => ConfirmForgetSecret(envName, key)));
                break;
            case SecretSource.SharedKeyring:
                actions.Add(("Set own value…", () => OpenSetSecretDialog(envName, key)));
                actions.Add(("Change shared…", () => OpenChangeSecretDialog(null, key)));
                actions.Add(("Forget shared", () => ConfirmForgetSecret(null, key)));
                break;
            default:
                actions.Add(("Set own value…", () => OpenSetSecretDialog(envName, key)));
                break;
        }
        return actions;
    }

    /// <summary>Asks for <paramref name="envName"/>'s own value of a secret.</summary>
    private void OpenSetSecretDialog(string envName, string key) =>
        OpenDialog($"Secret {key} for {envName}", "value — stored in the OS keyring, never in the file", v =>
        {
            if (!_keyring.Set(_workspace!.Root, envName, key, v)) FlashNoKeyring(envName, key);
            MarkDirty();
        });

    /// <summary>The keyring refused a value. Says why when the machine has no keyring at all (a Linux
    /// machine without libsecret or a running Secret Service), and the variable that works instead.</summary>
    /// <param name="envName">The environment the value was for, or null for every environment.</param>
    private void FlashNoKeyring(string? envName, string key)
    {
        string problem = _keyring is DefaultKeyring { Unavailable: { } why }
            ? $"No keyring on this machine: {why}"
            : $"The keyring didn't store {key}";
        Flash(envName == null
            ? $"{problem} — set the environment variable {key} instead"
            : $"{problem} — set the environment variable {Secrets.VariableName(envName, key)} (or {key} for every environment) instead");
    }

    /// <summary>Makes <paramref name="envName"/>'s own value the shared one, and every environment
    /// then uses it: the own values of the others on this machine go too, after a confirmation that
    /// names them.</summary>
    private void ShareSecret(string envName, string key)
    {
        var root = _workspace!.Root;
        if (_keyring.Get(root, envName, key) is not { } value) return;
        var others = _envFiles.Select(e => e.Name)
            .Where(n => n != envName && _keyring.Get(root, n, key) != null).ToList();

        void Share()
        {
            if (!_keyring.Set(root, null, key, value)) { FlashNoKeyring(envName, key); return; }
            foreach (var env in _envFiles) _keyring.Delete(root, env.Name, key);
            MarkDirty();
        }

        if (others.Count == 0) Share();
        else
            OpenConfirm($"Use {envName}'s {key} in every environment?",
                $"{string.Join(", ", others)} {(others.Count == 1 ? "has a value" : "have values")} of {(others.Count == 1 ? "its" : "their")} own "
                + "on this machine, which this forgets.",
                "Use everywhere", Share);
    }

    /// <summary>A key flagged secret whose file still holds a value in plain text — typed into
    /// the table before the flag went on. Marking leaves the line alone, so this cell offers the
    /// fix: move the value to the keyring as this environment's own when it has none yet, else
    /// clear it.</summary>
    private Element BuildPlainSecretFix(EnvDocument doc, string envName, string key, string plain)
    {
        var ws = _workspace!;
        bool keyringHasIt = _keyring.Get(ws.Root, envName, key) != null;
        var fix = Ui.Button(ButtonVariant.Ghost,
                keyringHasIt ? $"! still in {envName}.env — clear" : $"! still in {envName}.env — move to keyring",
                () =>
                {
                    if (!keyringHasIt && !_keyring.Set(ws.Root, envName, key, plain))
                    {
                        FlashNoKeyring(envName, key);
                        return;
                    }
                    doc.SetValue(key, "");
                    MarkDirty();
                },
                $"plain-secret-{key}-{envName}", style: Ui.WarnFill)
            .AccessibleName(keyringHasIt
                ? $"clear {key}'s plain-text value from {envName}"
                : $"move {key}'s plain-text value from {envName} to the OS keyring");
        return Flex.Row(gap: Azimuth.S1).FlexGrow(1).AlignItems(AlignItems.Center).Children(fix);
    }

    private async void RevealSecret(string envName, string key)
    {
        _reveal = (envName, key, System.Diagnostics.Stopwatch.GetTimestamp()
            + System.Diagnostics.Stopwatch.Frequency * RevealSeconds);
        MarkDirty();
        await Task.Delay(TimeSpan.FromSeconds(RevealSeconds));
        if (_reveal is { } r && r.Env == envName && r.Key == key)
        {
            _reveal = null;
            MarkDirty();
        }
    }

    internal void SaveEnvironments()
    {
        foreach (var env in _envFiles)
        {
            if (_envDocs.GetValueOrDefault(env.Name) is not { Edited: true } doc) continue;
            try
            {
                File.WriteAllText(env.Path, doc.Serialize());
                doc.MarkSaved();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Flash($"Could not save {env.Name}.env: {e.Message}");
            }
        }
        ForgetRemovedSecrets();
        MarkDirty();
    }

    // ── Environment switcher (Ctrl+E / the titlebar chip) ────────────────

    internal void OpenEnvSwitcher()
    {
        var items = new List<(string, Action)>();
        foreach (var env in _envFiles)
        {
            var name = env.Name;
            items.Add(($"{(name == _envName ? "✓ " : "")}{name}", () => SelectEnvironment(name)));
        }
        items.Add((_envName == null ? "✓ no environment" : "no environment", () => SelectEnvironment(null)));
        // Anchored under the titlebar chip's neighbourhood.
        _popoverX = Math.Max(8f, _window.LayoutWidth - 300f);
        _popoverY = 42f;
        _popover = items;
        MarkViewDirty();
    }

    // ── New environment ──────────────────────────────────────────────────

    private void OpenNewEnvironmentDialog() =>
        OpenDialog("New environment", "name — local, staging, ci", CreateEnvironment, input => EnvNameProblem(input));

    /// <summary>"local.env" and " local " both mean local: the suffix and the spaces go.</summary>
    private static string EnvNameFrom(string input) => CollectionNames.Stem(input, ".env");

    /// <summary>Why a typed name can't be a new environment, or null when it can. The name is a
    /// file name in envs/ and a <c>--env</c> argument, on every OS the collection is cloned to
    /// (<see cref="CollectionNames.Problem"/>), and no clash with an existing environment in any
    /// letter case.</summary>
    /// <param name="except">The environment being renamed, which is no clash: a rename may change
    /// only the letter case.</param>
    private string? EnvNameProblem(string input, string? except = null)
    {
        string name = EnvNameFrom(input);
        if (CollectionNames.Problem(name, "environment") is { } problem) return problem;
        if (_envFiles.FirstOrDefault(e => e.Name != except
                && string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)) is { } existing)
            return $"{existing.Name}.env already exists.";
        return null;
    }

    /// <summary>Writes an empty envs/&lt;name&gt;.env (making envs/ when the collection has none),
    /// rescans, and makes it the active environment — the one "＋ add key" adds to.</summary>
    internal void CreateEnvironment(string input)
    {
        if (_workspace is not { } ws) return;
        if (EnvNameProblem(input) is { } problem) { Flash(problem); return; }   // the prompt refuses these first
        string name = EnvNameFrom(input);
        var dir = System.IO.Path.Combine(ws.Root, "envs");
        try
        {
            Directory.CreateDirectory(dir);
            // CreateNew: a file the last scan missed is never overwritten.
            using (new FileStream(System.IO.Path.Combine(dir, name + ".env"), FileMode.CreateNew)) { }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Flash($"Could not create {name}.env: {e.Message}");
            return;
        }
        RescanWorkspace();
        SelectEnvironment(name);
    }

    // ── Minimal modal input dialog (Backdrop + card) ─────────────────────

    private (string Title, string Placeholder, Action<string> OnSubmit, Func<string, string?>? Validate)? _dialog;
    private string _dialogValue = "";
    private string? _dialogError;
    private readonly Ref<TextInputElement> _dialogRef = new();
    private bool _pendingDialogFocus;

    /// <param name="validate">Null, or the reason a value is refused: the prompt then stays up with
    /// the reason under the field and the text as typed.</param>
    /// <param name="initial">The text the field starts with, selected: a rename's current name.</param>
    private void OpenDialog(string title, string placeholder, Action<string> onSubmit,
        Func<string, string?>? validate = null, string initial = "")
    {
        NoteModalOpener();
        _dialog = (title, placeholder, onSubmit, validate);
        _dialogValue = initial;
        _dialogError = null;
        _pendingDialogFocus = true;
        MarkDirty();
    }

    /// <summary>Runs the prompt's action, which finds the prompt already taken off (<see cref="_dialog"/>
    /// null), and builds the window only when the action built nothing, as a popover's pick does
    /// (<see cref="Pick"/>). An action builds for what it changed — a new key, a secret set, a discovery
    /// started — and the prompt goes with that build; the window was built again after it.</summary>
    private void SubmitDialog()
    {
        if (_dialog is not { } dialog) return;
        if (dialog.Validate?.Invoke(_dialogValue) is { } error)
        {
            _dialogError = error;
            MarkDirty();
            return;
        }
        _dialog = null;
        ReturnFocusToModalOpener();
        int builds = Builds;
        dialog.OnSubmit(_dialogValue);
        if (Builds == builds) MarkDirty();
    }

    private void CancelDialog()
    {
        _dialog = null;
        ReturnFocusToModalOpener();
        MarkDirty();
    }

    // ── The focus back to what opened a prompt or a question ─────────────
    // The prompt's field and the question's button take the focus when they open, and went with
    // the card when it closed: the focus was left nowhere, and a reader was told so (Escape out of
    // "New request" left no focused element). It goes back to what held it when the card opened —
    // the "New request" button, a row's rename — as the card closes, while the card's field still
    // has it: the opener is in the window's own tree, which the close does not take away, so it
    // moves in one step, as a reader hears it. A submit gives it back before its action runs, so
    // an action that focuses something of its own (after its build) wins, and a card the action
    // opens takes the opener as its own.

    /// <summary>What held the keyboard focus when the open prompt or question opened.</summary>
    private Element? _modalOpener;

    private void NoteModalOpener()
    {
        if (_dialog is null && _confirm is null) _modalOpener = _window.FocusedElement;
    }

    /// <summary>The prompt or the question is closing: the focus back to its opener, if it is still
    /// in the window.</summary>
    private void ReturnFocusToModalOpener()
    {
        var opener = _modalOpener;
        _modalOpener = null;
        if (opener is not null && _window.IsInTree(opener)) opener.Focus();
    }

    private Element BuildDialog((string Title, string Placeholder, Action<string> OnSubmit, Func<string, string?>? Validate) dialog)
    {
        var backdrop = Box.New()
            .Width(Dimension.Percent(100)).Height(Dimension.Percent(100))
            .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
            .ZIndex(70);
        backdrop.Position.Type = PositionType.Absolute;
        backdrop.Position.Top = Dimension.Px(0);
        backdrop.Position.Left = Dimension.Px(0);
        backdrop.Background(Azimuth.Alpha(Azimuth.Bg, 0.62));
        backdrop.On<ClickEvent>(e => { e.StopPropagation = true; CancelDialog(); });

        // A dialog named by its title, directly inside the backdrop, so the backdrop is no button
        // (AccessibilityTreeBuilder.InferRole): a reader heard the prompt inside a button named
        // after all of its text, twice (the card was a second one), and a text change in it built
        // the window's whole accessibility tree. The click beside the card and Escape still close it.
        var card = Flex.Column(gap: Azimuth.S3)
            .Width(420).Padding(new Thickness(Azimuth.S4))
            .Background(Azimuth.Surface)
            .BorderColor(Azimuth.BorderStrong).BorderWidth(1f)
            .CornerRadius(Azimuth.Radius2)
            .AccessibleRole(AccessibilityRole.Dialog).AccessibleName(dialog.Title);
        card.On<ClickEvent>(e => e.StopPropagation = true);

        card.AddChild(Text.New(dialog.Title).FontSize(13.5f).FontWeight(FontWeight.Medium).TextColor(Azimuth.Text));

        var input = TextInput.New(_dialogValue).Style(Ui.GroundField);
        input.PlaceholderText(dialog.Placeholder)
            .OnTextChanged(v => _dialogValue = v)
            .Height(Dimension.Px(30)).FlexShrink(0)
            .FontFamily(Fonts.Mono).FontSize(12f)
            .TextColor(Azimuth.Text)
            .WithRef(_dialogRef);
        input.AutomationId("dialog-input");
        if (_dialogError != null) input.AccessibleDescription(_dialogError);
        card.AddChild(input);
        if (_dialogError != null)
            card.AddChild(Text.New("! " + _dialogError).FontSize(11.5f).TextColor(Azimuth.StatusWarn)
                .AutomationId("dialog-error"));

        var buttons = Flex.Row(gap: Azimuth.S2).JustifyContent(JustifyContent.FlexEnd);
        buttons.AddChild(BarAction("cancel", "dialog-cancel", CancelDialog));
        buttons.AddChild(Ui.Button(ButtonVariant.Primary, "OK", SubmitDialog, "dialog-ok", style: Ui.Dialog));
        card.AddChild(buttons);

        backdrop.AddChild(card);
        return backdrop;
    }
}
