using System.Linq;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Styling;
using GitClient.Theme;

namespace GitClient.Junction;

public partial class JunctionApp
{
    private readonly record struct ModalMeta(string Icon, string Title, string Subtitle, string PrimaryLabel, string PrimaryIcon);

    private static ModalMeta MetaFor(ModalKind k) => k switch
    {
        ModalKind.AddRepo => new(Icons.FolderPlus, "Add local repository",
            "Register a git repository already on disk.", "Add repository", Icons.FolderPlus),
        ModalKind.CloneRepo => new(Icons.DownloadSimple, "Clone repository",
            "Clone a remote into a local folder.", "Clone", Icons.DownloadSimple),
        ModalKind.NewWorkspace => new(Icons.StackPlus, "New workspace",
            "Group repositories you open together.", "Create workspace", Icons.StackPlus),
        ModalKind.EditWorkspace => new(Icons.PencilSimple, "Edit workspace",
            "Rename, recolour, and choose repositories.", "Save changes", Icons.Check),
        _ => new(Icons.Swatches, "Repository settings",
            "Badge colour and workspace membership.", "Save changes", Icons.Check),
    };

    private Element BuildModal(ModalForm f)
    {
        var meta = MetaFor(f.Kind);

        // Column backdrop: JustifyContent = vertical (top-anchored via padding),
        // AlignItems = horizontal (centered).
        var backdrop = Box.New()
            .Width(Dimension.Percent(100)).Height(Dimension.Percent(100))
            .Background(new Color(0, 0, 0, 140))
            .JustifyContent(JustifyContent.FlexStart).AlignItems(AlignItems.Center)
            .Padding(new Thickness(64, 24, 24, 24))
            .ZIndex(70);
        backdrop.Position.Type = PositionType.Absolute;
        backdrop.Position.Top = Dimension.Px(0);
        backdrop.Position.Left = Dimension.Px(0);
        backdrop.On<ClickEvent>(e => { e.StopPropagation = true; CloseModal(); });

        var card = Flex.Column(gap: 0)
            .Width(520).MaxWidth(Dimension.Percent(92)).MaxHeight(768)
            .Background(Nocturne.Surface)
            .BorderColor(Nocturne.Neutral700).BorderWidth(1f)
            .CornerRadius(Nocturne.RadiusLg);
        card.On<ClickEvent>(e => e.StopPropagation = true);

        // Header.
        var header = Flex.Row(gap: 12).AlignItems(AlignItems.FlexStart).FlexShrink(0)
            .Padding(new Thickness(16, 18, 13, 18))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(0, 0, 1, 0));
        header.AddChild(Box.New().Width(34).Height(34).FlexShrink(0).CornerRadius(Nocturne.RadiusMd)
            .Background(Nocturne.Accent800).JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
            .Children(Ui.Icon(meta.Icon, 17f, Nocturne.Accent200)));
        var htext = Flex.Column(gap: 2).FlexGrow(1);
        htext.AddChild(Text.New(meta.Title).FontSize(16f).FontWeight(FontWeight.Medium));
        htext.AddChild(Text.New(meta.Subtitle).FontSize(11.5f).TextColor(Nocturne.On(0.52)));
        header.AddChild(htext);
        header.AddChild(Ui.IconButton(Icons.X, CloseModal, "Close", 28f));
        card.AddChild(header);

        // Body (scrolling). Pad the inner column, not the ScrollView, so bordered
        // fields aren't clipped against the scroll's content-box edge.
        var body = ScrollView.New().FlexGrow(1).FlexShrink(1);
        var col = Flex.Column(gap: 15).Padding(new Thickness(16, 18, 16, 18));

        if (f.Kind == ModalKind.AddRepo)
            col.AddChild(ModalField("Repository folder",
                ModalInputWithBrowse(f, f.Path, "~/code/my-repo", v => f.Path = v, _modalNameRef, mono: true)));
        if (f.Kind == ModalKind.CloneRepo)
        {
            col.AddChild(ModalField("Remote URL",
                ModalInput(f.Url, "https://github.com/acme/service.git", v => f.Url = v, _modalNameRef, mono: true)));
            col.AddChild(ModalField("Clone to",
                ModalInputWithBrowse(f, f.Dest, "~/code", v => f.Dest = v, null, mono: true)));
        }
        if (f.IsWorkspace)
            col.AddChild(ModalField("Workspace name",
                ModalInput(f.Name, "e.g. Platform & infra", v => f.Name = v, _modalNameRef, mono: false)));

        if (f.IsWorkspace)
            col.AddChild(ModalField("Icon", BuildIconPicker(f)));

        col.AddChild(ModalField(f.IsWorkspace ? "Icon colour" : "Badge colour", BuildColorPicker(f)));
        col.AddChild(BuildPreview(f));

        if (f.IsWorkspace)
            col.AddChild(ModalField("Repositories in this workspace", BuildRepoChecklist(f)));
        else
            col.AddChild(ModalField("Add to workspaces", BuildWorkspaceChecklist(f)));

        body.AddChild(col);
        card.AddChild(body);

        // Action bar.
        var bar = Flex.Row(gap: 8).AlignItems(AlignItems.Center).FlexShrink(0)
            .Padding(new Thickness(12, 18, 12, 18))
            .BorderColor(Nocturne.Divider).BorderWidth(new Thickness(1, 0, 0, 0));
        if (f.Kind == ModalKind.EditWorkspace && f.EditId is { } wid)
        {
            var del = Flex.Row(gap: 6).AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
                .Padding(new Thickness(4, 8, 4, 8)).CornerRadius(Nocturne.RadiusMd).Cursor(CursorType.Pointer)
                .Children(
                    Ui.Icon(Icons.Trash, 13.5f, Nocturne.DangerText),
                    Text.New("Delete").FontSize(12.5f).FontWeight(FontWeight.Medium).TextColor(Nocturne.DangerText));
            del.On<ClickEvent>(e => { e.StopPropagation = true; CloseModal(); DeleteWorkspace(wid); });
            del.HoverBg(Nocturne.Alpha(Nocturne.DangerText, 0.10), Color.Transparent);
            bar.AddChild(del);
        }
        bar.AddChild(Box.New().FlexGrow(1));
        bar.AddChild(Ui.SecondaryButton("Cancel", null, CloseModal, 12.5f));
        var valid = ModalValid(f);
        // Key by validity: the enabled/disabled variants are different elements at
        // the same position, and the framework doesn't copy handlers on patch — so
        // without distinct keys the enabled button would reuse the disabled (handler-
        // less) element and never submit.
        var primary = valid
            ? Ui.PrimaryButton(meta.PrimaryLabel, meta.PrimaryIcon, SubmitModal, 12.5f)
            : DisabledButton(meta.PrimaryLabel, meta.PrimaryIcon);
        primary.Key = "modal-primary-" + (valid ? "on" : "off");
        bar.AddChild(primary);
        card.AddChild(bar);

        backdrop.AddChild(card);
        return backdrop;
    }

    // ── Field building blocks ────────────────────────────────────────────

    private static Element ModalField(string label, Element control)
    {
        var col = Flex.Column(gap: 6);
        col.AddChild(Text.New(label).FontSize(11f).TextColor(Nocturne.On(0.55)));
        col.AddChild(control);
        return col;
    }

    private Element ModalInput(string value, string placeholder, Action<string> onChange,
        Ref<TextInputElement>? refEl, bool mono)
    {
        var boxEl = Flex.Row(gap: 0).AlignItems(AlignItems.Center)
            .Height(38).Padding(new Thickness(0, 12, 0, 12))
            .Background(Nocturne.Bg).BorderColor(Nocturne.Divider).BorderWidth(1f).CornerRadius(Nocturne.RadiusMd);
        var input = TextInput.New(value)
            .PlaceholderText(placeholder)
            .OnTextChanged(v => { onChange(v); MarkDirty(); })
            .Height(Dimension.Px(30)).FlexGrow(1).FlexShrink(1).FlexBasis(Dimension.Px(0))
            .TextColor(Nocturne.Text);
        if (mono) input.FontFamily(Fonts.Mono).FontSize(12f);
        if (refEl != null) input.WithRef(refEl);
        input.On<KeyDownEvent>(e => { if (e.Key == KeyCode.Enter) { SubmitModal(); e.StopPropagation = true; } });
        boxEl.AddChild(input);
        return boxEl;
    }

    private Element ModalInputWithBrowse(ModalForm f, string value, string placeholder, Action<string> onChange,
        Ref<TextInputElement>? refEl, bool mono)
    {
        var row = Flex.Row(gap: 8).AlignItems(AlignItems.Center);
        var input = ModalInput(value, placeholder, onChange, refEl, mono);
        input.FlexGrow(1).FlexShrink(1);
        row.AddChild(input);
        row.AddChild(Ui.SecondaryButton("Browse", Icons.FolderOpen, () => BrowseForFolder(f, value, onChange), 12f)
            .FlexShrink(0));
        return row;
    }

    /// <summary>Opens the OS folder picker and drops the chosen path into the field.
    /// Runs off the UI thread; guards against the modal changing while it's open.</summary>
    private async void BrowseForFolder(ModalForm f, string current, Action<string> apply)
    {
        if (!_folderPicker.IsAvailable) { Flash("No folder picker available — paste the path"); return; }

        var initial = string.IsNullOrWhiteSpace(current)
            ? (HasActiveRepo ? System.IO.Path.GetDirectoryName(_activeRepoPath.TrimEnd('/', '\\')) : null)
            : ExpandPath(current);

        string? picked;
        try { picked = await _folderPicker.PickFolderAsync(initial); }
        catch (OperationCanceledException) { return; }
        catch { Flash("Couldn't open the folder picker — paste the path"); return; }

        if (string.IsNullOrEmpty(picked) || _modal != f) return;   // cancelled, or modal changed
        apply(picked);
        MarkDirty();
    }

    private Element BuildIconPicker(ModalForm f)
    {
        var wrap = Flex.Row(gap: 6).FlexWrap(FlexWrap.Wrap);
        foreach (var key in Icons.WorkspaceKeys)
        {
            var on = f.Icon == key;
            var tile = Box.New()
                .Width(34).Height(34).CornerRadius(Nocturne.RadiusMd)
                .Background(on ? Nocturne.Alpha(Nocturne.Accent, 0.14) : Nocturne.Bg)
                .BorderColor(on ? Nocturne.Accent : Nocturne.Divider).BorderWidth(1f)
                .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
                .Cursor(CursorType.Pointer).Tooltip(key)
                .Children(Ui.Icon(WorkspaceGlyph(key), 16f, on ? Nocturne.Accent200 : Nocturne.On(0.7)));
            tile.Key = "wsicon-" + key;
            var captured = key;
            tile.On<ClickEvent>(e => { e.StopPropagation = true; f.Icon = captured; MarkDirty(); });
            wrap.AddChild(tile);
        }
        return wrap;
    }

    private Element BuildColorPicker(ModalForm f)
    {
        var wrap = Flex.Row(gap: 9).FlexWrap(FlexWrap.Wrap);
        foreach (var s in Swatches.All)
        {
            var on = f.Color == s.Id;
            var dot = Box.New()
                .Width(30).Height(30).CornerRadius(15f)
                .Background(s.Solid)
                .BorderColor(on ? Nocturne.Text : Color.Transparent).BorderWidth(on ? 2f : 0f)
                .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
                .Cursor(CursorType.Pointer).Tooltip(s.Label);
            if (on) dot.AddChild(Ui.Icon(Icons.CheckBold, 13f, Nocturne.Bg));
            dot.Key = "swatch-" + s.Id;
            var captured = s.Id;
            dot.On<ClickEvent>(e => { e.StopPropagation = true; f.Color = captured; MarkDirty(); });
            wrap.AddChild(dot);
        }
        return wrap;
    }

    private Element BuildPreview(ModalForm f)
    {
        var sw = Swatches.ById(f.Color);
        var row = Flex.Row(gap: 11).AlignItems(AlignItems.Center)
            .Padding(new Thickness(11, 13, 11, 13))
            .Background(Nocturne.Bg).BorderColor(Nocturne.Divider).BorderWidth(1f).CornerRadius(Nocturne.RadiusMd);
        row.AddChild(Text.New("PREVIEW").FontSize(10f).TextColor(Nocturne.On(0.4)));
        row.AddChild(Box.New().FlexGrow(1));

        var name = ModalPreviewName(f);
        var tile = Box.New().Width(30).Height(30).FlexShrink(0).CornerRadius(Nocturne.RadiusMd)
            .Background(sw.Bg).JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center);
        if (f.IsWorkspace)
            tile.AddChild(Ui.Icon(WorkspaceGlyph(f.Icon), 16f, sw.Fg));
        else
            tile.AddChild(Text.New(string.IsNullOrEmpty(name) ? "?" : char.ToUpperInvariant(name[0]).ToString())
                .FontSize(13f).FontWeight(FontWeight.SemiBold).TextColor(sw.Fg));
        row.AddChild(tile);
        row.AddChild(Text.New(name).FontSize(14f).FontWeight(FontWeight.Medium)
            .MaxWidth(220).OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
        return row;
    }

    private Element BuildRepoChecklist(ModalForm f)
    {
        var scroll = ScrollView.New().MaxHeight(214);
        var col = Flex.Column(gap: 3);
        var paths = _settings.RepositoryPaths;
        if (paths.Count == 0)
            col.AddChild(Text.New("No repositories yet — add one first.").FontSize(12f).TextColor(Nocturne.On(0.45)));
        foreach (var path in paths)
        {
            var norm = NormPath(path);
            var on = f.RepoPaths.Contains(norm);
            var name = System.IO.Path.GetFileName(path.TrimEnd('/', '\\'));
            var (bg, fg) = RepoTile(path);
            var row = Flex.Row(gap: 9).AlignItems(AlignItems.Center)
                .Padding(new Thickness(6, 8, 6, 8)).CornerRadius(Nocturne.RadiusRow).Cursor(CursorType.Pointer);
            row.Key = "chkr-" + norm + (on ? "-on" : "-off");
            row.AddChild(Ui.Icon(on ? Icons.CheckSquare : Icons.Square, 16f, on ? Nocturne.Accent : Nocturne.On(0.4)));
            row.AddChild(Ui.InitialTile(name, bg, fg, 22f, 10f));
            row.AddChild(Text.New(name).FontSize(12.5f).FlexGrow(1)
                .OverflowMode(TextOverflow.Ellipsis).MaxLines(1));
            row.On<ClickEvent>(e =>
            {
                e.StopPropagation = true;
                if (!f.RepoPaths.Add(norm)) f.RepoPaths.Remove(norm);
                MarkDirty();
            });
            row.HoverBg(Nocturne.Neutral900, Color.Transparent);
            col.AddChild(row);
        }
        scroll.AddChild(col);
        return scroll;
    }

    private Element BuildWorkspaceChecklist(ModalForm f)
    {
        var col = Flex.Column(gap: 3);
        foreach (var ws in _settings.Workspaces)
        {
            var on = f.WorkspaceIds.Contains(ws.Id);
            var sw = Swatches.ById(ws.Color);
            var row = Flex.Row(gap: 9).AlignItems(AlignItems.Center)
                .Padding(new Thickness(6, 8, 6, 8)).CornerRadius(Nocturne.RadiusRow).Cursor(CursorType.Pointer);
            row.Key = "chkw-" + ws.Id + (on ? "-on" : "-off");
            row.AddChild(Ui.Icon(on ? Icons.CheckSquare : Icons.Square, 16f, on ? Nocturne.Accent : Nocturne.On(0.4)));
            row.AddChild(Ui.Icon(WorkspaceGlyph(ws.Icon), 15f, sw.Solid));
            row.AddChild(Text.New(ws.Name).FontSize(12.5f).FlexGrow(1));
            row.AddChild(Text.New($"{ws.RepoPaths.Count} repos").FontSize(10.5f).TextColor(Nocturne.On(0.42)));
            var id = ws.Id;
            row.On<ClickEvent>(e =>
            {
                e.StopPropagation = true;
                if (!f.WorkspaceIds.Add(id)) f.WorkspaceIds.Remove(id);
                MarkDirty();
            });
            row.HoverBg(Nocturne.Neutral900, Color.Transparent);
            col.AddChild(row);
        }
        return col;
    }

    private static Element DisabledButton(string label, string glyph)
    {
        var muted = Nocturne.On(0.32);
        return Flex.Row(gap: 6).AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
            .Padding(new Thickness(5, 10, 5, 10)).CornerRadius(Nocturne.RadiusMd)
            .BorderColor(Nocturne.On(0.18)).BorderWidth(1f).Cursor(CursorType.Default)
            .Children(
                Ui.Icon(glyph, 13.5f, muted),
                Text.New(label).FontSize(12.5f).FontWeight(FontWeight.Medium).TextColor(muted));
    }

    // ── Repositories "+" popover (a small left-click dropdown) ───────────

    private Element BuildPopover(IReadOnlyList<ContextMenuItem> items)
    {
        // Anchored under the "+" button; coords captured at click time.
        float x = _popoverX, y = _popoverY;

        var layer = Box.New()
            .Width(Dimension.Percent(100)).Height(Dimension.Percent(100))
            .ZIndex(60);
        layer.Position.Type = PositionType.Absolute;
        layer.Position.Top = Dimension.Px(0);
        layer.Position.Left = Dimension.Px(0);
        layer.On<ClickEvent>(e => { e.StopPropagation = true; _popover = null; MarkDirty(); });
        layer.On<PointerDownEvent>(e => { if (e.Button == 2) { _popover = null; MarkDirty(); } });

        var menu = Flex.Column(gap: 0)
            .Width(230).Padding(new Thickness(5))
            .Background(Nocturne.Surface).BorderColor(Nocturne.Neutral700).BorderWidth(1f)
            .CornerRadius(Nocturne.RadiusMd);
        menu.Position.Type = PositionType.Absolute;
        menu.Position.Left = Dimension.Px(x);
        menu.Position.Top = Dimension.Px(y);
        menu.On<ClickEvent>(e => e.StopPropagation = true);

        foreach (var it in items)
        {
            var row = Flex.Row(gap: 9).AlignItems(AlignItems.Center)
                .Padding(new Thickness(7, 9, 7, 9)).CornerRadius(Nocturne.RadiusRow).Cursor(CursorType.Pointer);
            row.AddChild(Ui.Icon(it.Glyph ?? "", 14f, Nocturne.On(0.7)));
            row.AddChild(Text.New(it.Label).FontSize(12.5f).FlexGrow(1));
            var run = it.Action;
            row.On<ClickEvent>(e => { e.StopPropagation = true; _popover = null; run?.Invoke(); });
            row.HoverBg(Nocturne.Neutral900, Color.Transparent);
            menu.AddChild(row);
        }
        layer.AddChild(menu);
        return layer;
    }
}
