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
            return EmptyView("Environments", "no environments",
                $"Create envs{System.IO.Path.DirectorySeparatorChar}local.env in the collection and it appears here.");

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
            r.OnClick(_ => SelectEnvironment(name));
            if (!active) r.HoverBg(Azimuth.Raised, Color.Transparent);
            panel.AddChild(r);
        }

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
                Text.New("All keys × all environments").FontSize(13f).FontWeight(FontWeight.Medium).TextColor(Azimuth.TextDim),
                Ui.Mono("plain text on disk · secret values never leave the OS keyring", 10.5f, Azimuth.TextFaint)));

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
        row.AddChild(Box.New().Width(52).FlexShrink(0)
            .Children(secret
                ? Ui.Mono("●", 10f, Azimuth.Accent300)
                : Text.New("")));

        foreach (var env in _envFiles)
            row.AddChild(BuildCell(env.Name, key, secret));
        return row;
    }

    private Element BuildCell(string envName, string key, bool secret)
    {
        var cell = Flex.Row(gap: Azimuth.S1).FlexGrow(1).FlexBasis(Dimension.Px(0)).AlignItems(AlignItems.Center);
        var doc = _envDocs.GetValueOrDefault(envName);
        if (doc == null) return cell;

        if (secret)
        {
            cell.AddChild(BuildSecretCell(envName, key));
            return cell;
        }

        if (!doc.Keys.Contains(key))
        {
            // Missing here: the warn cell with the one-click fix.
            var add = Ui.Button(ButtonVariant.Ghost, $"! missing — add to {envName}",
                    () => { doc.SetValue(key, ""); MarkDirty(); }, $"add-{key}-to-{envName}", style: Ui.WarnFill)
                .AccessibleName($"add {key} to {envName}");
            cell.AddChild(add);
            return cell;
        }

        var input = TextInput.New(doc.Values.GetValueOrDefault(key, "")).Style(Ui.Field);
        input.OnTextChanged(v => { doc.SetValue(key, v); MarkDirty(); })
            .Height(Dimension.Px(26)).FlexGrow(1)
            .FontFamily(Fonts.Mono).FontSize(11.5f)
            .TextColor(Azimuth.Text);
        input.AutomationId($"cell-{key}-{envName}");
        cell.AddChild(input);
        return cell;
    }

    /// <summary>A secret's cell is per-machine, not per-file: masked when the
    /// keyring has it (with the timed reveal), the fresh-clone fix when it
    /// doesn't. The same cell for every column — the keyring is not
    /// environment-scoped.</summary>
    private Element BuildSecretCell(string envName, string key)
    {
        var ws = _workspace!;
        string? value = _keyring.Get(ws.Root, key);
        var cell = Flex.Row(gap: Azimuth.S1).FlexGrow(1).AlignItems(AlignItems.Center);

        if (value == null)
        {
            var set = Ui.Button(ButtonVariant.Ghost, "! not set on this machine — set value", null,
                    $"set-secret-{key}", style: Ui.WarnFill)
                .AccessibleName($"set {key} for this machine");
            set.OnClick(_ => OpenDialog($"Secret {key}", "value — stored in the OS keyring, never in the file", v =>
            {
                if (!_keyring.Set(ws.Root, key, v))
                    Flash("No keyring on this platform — set it as an environment variable instead");
                MarkDirty();
            }));
            cell.AddChild(set);
            return cell;
        }

        bool revealed = _reveal is { } r && r.Key == key
            && System.Diagnostics.Stopwatch.GetTimestamp() < r.UntilTs;
        cell.AddChild(Ui.Mono(revealed ? value : "••••••••", 11.5f, revealed ? Azimuth.Text : Azimuth.TextMuted)
            .AutomationId($"secret-value-{key}"));
        cell.AddChild(Ui.Mono("keyring", 9f, Azimuth.TextFaint));
        cell.AddChild(Box.New().FlexGrow(1));
        if (!revealed)
            cell.AddChild(BarAction($"reveal ({RevealSeconds} s)", $"reveal-{key}", () => RevealSecret(key)));
        return cell;
    }

    private async void RevealSecret(string key)
    {
        _reveal = (key, System.Diagnostics.Stopwatch.GetTimestamp()
            + System.Diagnostics.Stopwatch.Frequency * RevealSeconds);
        MarkDirty();
        await Task.Delay(TimeSpan.FromSeconds(RevealSeconds));
        if (_reveal is { } r && r.Key == key)
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
        MarkDirty();
    }

    // ── Minimal modal input dialog (Backdrop + card) ─────────────────────

    private (string Title, string Placeholder, Action<string> OnSubmit)? _dialog;
    private string _dialogValue = "";
    private readonly Ref<TextInputElement> _dialogRef = new();
    private bool _pendingDialogFocus;

    private void OpenDialog(string title, string placeholder, Action<string> onSubmit)
    {
        _dialog = (title, placeholder, onSubmit);
        _dialogValue = "";
        _pendingDialogFocus = true;
        MarkDirty();
    }

    private void SubmitDialog()
    {
        if (_dialog is not { } dialog) return;
        _dialog = null;
        dialog.OnSubmit(_dialogValue);
        MarkDirty();
    }

    private void CancelDialog()
    {
        _dialog = null;
        MarkDirty();
    }

    private Element BuildDialog((string Title, string Placeholder, Action<string> OnSubmit) dialog)
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

        var card = Flex.Column(gap: Azimuth.S3)
            .Width(420).Padding(new Thickness(Azimuth.S4))
            .Background(Azimuth.Surface)
            .BorderColor(Azimuth.BorderStrong).BorderWidth(1f)
            .CornerRadius(Azimuth.Radius2);
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
        card.AddChild(input);

        var buttons = Flex.Row(gap: Azimuth.S2).JustifyContent(JustifyContent.FlexEnd);
        buttons.AddChild(BarAction("cancel", "dialog-cancel", CancelDialog));
        buttons.AddChild(Ui.Button(ButtonVariant.Primary, "OK", SubmitDialog, "dialog-ok", style: Ui.Dialog));
        card.AddChild(buttons);

        backdrop.AddChild(card);
        return backdrop;
    }
}
