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
    private static readonly string[] Methods = ["GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS"];

    private readonly HultUI.Core.Reactivity.Ref<TextInputElement> _urlRef = new();
    private bool _pendingUrlFocus;

    // ── The collection shell: tree · editor ──────────────────────────────

    /// <summary>
    /// The Requests view once a collection is open. A ScrollView rather than a
    /// VirtualList for the tree on purpose: a collection is tens of requests,
    /// not thousands, and the rows are heterogeneous (folder headers between
    /// request rows).
    /// </summary>
    private Element BuildCollectionShell()
    {
        var row = Flex.Row(gap: 0).FlexGrow(1).AlignItems(AlignItems.Stretch);
        var tree = BuildTree(); tree.Key = "tree";
        row.AddChild(tree);

        var editor = BuildEditorColumn(); editor.Key = "editor";
        if (ActiveTab is not { } tab)
        {
            row.AddChild(editor);
            return row;
        }

        // The response pane: right of the editor by default, under it when
        // flipped or forced by the breakpoint. Keyed by orientation so the two
        // shapes never patch into each other.
        var response = BuildResponsePanel(tab, ResponseAtBottom);
        response.Key = "response-" + (ResponseAtBottom ? "bottom" : "right");
        if (ResponseAtBottom)
        {
            var main = Flex.Column(gap: 0).FlexGrow(1).Children(editor, response);
            main.Key = "main-bottom";
            row.AddChild(main);
        }
        else
        {
            var main = Flex.Row(gap: 0).FlexGrow(1).AlignItems(AlignItems.Stretch).Children(editor, response);
            main.Key = "main-right";
            row.AddChild(main);
        }
        return row;
    }

    // ── Tree ─────────────────────────────────────────────────────────────

    private Element BuildTree()
    {
        var ws = _workspace!;
        var panel = Flex.Column(gap: 0).FlexShrink(0)
            .Width(Azimuth.TreeWidth)
            .BorderColor(Azimuth.Border).BorderWidth(new Thickness(0, 0, 0, 1));

        panel.AddChild(Flex.Row(gap: 0).Height(36).FlexShrink(0)
            .AlignItems(AlignItems.Center)
            .Padding(new Thickness(0, Azimuth.S3, 0, Azimuth.S3))
            .Children(Text.New("Collections").FontSize(13f).FontWeight(FontWeight.Medium).TextColor(Azimuth.TextDim)));

        var scroll = ScrollView.New();
        var list = Flex.Column(gap: 0);
        if (_tree is { } tree)
        {
            if (tree.Root.Folders.Count == 0 && tree.Root.Requests.Count == 0)
                list.AddChild(Flex.Column(gap: Azimuth.S1)
                    .Padding(new Thickness(Azimuth.S3))
                    .Children(
                        Text.New("no requests yet").FontSize(12f).TextColor(Azimuth.TextMuted),
                        Text.New("Create a .req file and it appears here.").FontSize(11f).TextColor(Azimuth.TextFaint)));
            AddFolderRows(list, tree.Root, depth: 0, isRoot: true);
        }
        scroll.AddChild(list);
        var scrollBox = Box.New().FlexGrow(1);
        scrollBox.AddChild(scroll);
        panel.AddChild(scrollBox);

        // The workspace path pinned at the bottom — the file-backed honesty line.
        panel.AddChild(Flex.Row(gap: 0).Height(28).FlexShrink(0)
            .AlignItems(AlignItems.Center)
            .Padding(new Thickness(0, Azimuth.S3, 0, Azimuth.S3))
            .BorderColor(Azimuth.Border).BorderWidth(new Thickness(0, 0, 1, 0))
            .Children(Ui.Mono(ws.Root, 10.5f, Azimuth.TextFaint).TextOverflow(TextOverflow.Ellipsis).MaxLines(1)));

        return panel;
    }

    private void AddFolderRows(BoxElement list, CollectionFolder folder, int depth, bool isRoot)
    {
        if (!isRoot)
        {
            // Folders in mono — they are real directories.
            list.AddChild(Flex.Row(gap: Azimuth.S1).Height(Azimuth.RowList).FlexShrink(0)
                .AlignItems(AlignItems.Center)
                .Padding(new Thickness(0, 0, 0, Azimuth.S3 + depth * Azimuth.S3))
                .Children(Ui.Mono(folder.Name + "/", 12f, Azimuth.TextMuted)));
        }
        int childDepth = isRoot ? 0 : depth + 1;
        foreach (var sub in folder.Folders)
            AddFolderRows(list, sub, childDepth, isRoot: false);
        foreach (var request in folder.Requests)
            list.AddChild(RequestRow(request, childDepth));
    }

    private Element RequestRow(RequestRef request, int depth)
    {
        bool active = ActiveTab?.Path.Equals(request.Path, StringComparison.OrdinalIgnoreCase) == true;
        var openTab = _tabs.FirstOrDefault(t => t.Path.Equals(request.Path, StringComparison.OrdinalIgnoreCase));

        var row = Flex.Row(gap: Azimuth.S2).Height(Azimuth.RowList).FlexShrink(0)
            .AlignItems(AlignItems.Center)
            .Padding(new Thickness(0, Azimuth.S2, 0, Azimuth.S3 + depth * Azimuth.S3))
            .CornerRadius(Azimuth.Radius1)
            .BorderColor(active ? Azimuth.Accent : Color.Transparent)
            .BorderWidth(new Thickness(2, 0, 0, 0))
            .Cursor(CursorType.Pointer)
            .AccessibleRole(AccessibilityRole.Button)
            .AccessibleName($"{request.Method ?? "?"} {request.DisplayName}")
            .AutomationId("req-" + System.IO.Path.GetFileNameWithoutExtension(request.Path));
        if (active) row.Background(Azimuth.Raised);

        row.AddChild(MethodChip(request.Method, treeScale: true));
        row.AddChild(Text.New(request.DisplayName).FontSize(12f)
            .TextColor(active ? Azimuth.Text : Azimuth.TextDim)
            .TextOverflow(TextOverflow.Ellipsis).MaxLines(1).FlexGrow(1));
        // The cheap chain badges: what this request feeds forward, and how
        // many variables it consumes. The Chain tab has the full story.
        if (request.Produces.Count > 0)
            row.AddChild(Ui.Mono("→" + request.Produces[0], 8f, Azimuth.Accent300).FlexShrink(0));
        if (request.UsesCount > 0)
            row.AddChild(Ui.Mono("⛓" + request.UsesCount, 8f, Azimuth.TextFaint).FlexShrink(0));
        if (openTab is { Dirty: true })
            row.AddChild(Dot(Azimuth.Accent));

        row.OnClick(_ => OpenRequest(request.Path));
        if (!active) row.HoverBg(Azimuth.Raised, Color.Transparent);
        return row;
    }

    /// <summary>The method chip: mono caps on a 16% tint of the method colour,
    /// text always present. 8px at tree scale, 10 elsewhere; DELETE abbreviates
    /// to DEL at tree scale (the sheet's rule).</summary>
    internal static Element MethodChip(string? method, bool treeScale = false)
    {
        string label = method ?? "?";
        if (treeScale && label == "DELETE") label = "DEL";
        var color = method != null ? Azimuth.MethodColor(method) : Azimuth.TextMuted;
        return Box.New()
            .Padding(new Thickness(2, 5, 2, 5))
            .CornerRadius(Azimuth.Radius1)
            .Background(Azimuth.Tint(color))
            .FlexShrink(0)
            .Children(Ui.ChipLabel(label, treeScale ? 8f : 10f, color).FontWeight(FontWeight.SemiBold));
    }

    private static Element Dot(Color color)
    {
        var dot = Box.New().Width(5).Height(5).CornerRadius(2.5f).FlexShrink(0);
        dot.Background(color);
        return dot;
    }

    // ── Editor column: tabs · url bar · sub-editors · footer ─────────────

    private Element BuildEditorColumn()
    {
        var col = Flex.Column(gap: 0).FlexGrow(1);
        if (_tabs.Count == 0)
        {
            col.AddChild(Flex.Column(gap: Azimuth.S2).FlexGrow(1)
                .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
                .Children(
                    Text.New("no request open").FontSize(13f).TextColor(Azimuth.TextMuted),
                    Text.New("Pick one from the tree.").FontSize(11.5f).TextColor(Azimuth.TextFaint)));
            return col;
        }

        var tabsBar = BuildTabsBar(); tabsBar.Key = "tabs";
        col.AddChild(tabsBar);

        var tab = ActiveTab!;
        if (tab.ExternalChanged)
        {
            var bar = BuildExternalChangeBar(tab); bar.Key = "external-change";
            col.AddChild(bar);
        }

        var urlBar = BuildUrlBar(tab); urlBar.Key = "urlbar";
        col.AddChild(urlBar);

        var subTabs = BuildSubTabs(tab); subTabs.Key = "subtabs";
        col.AddChild(subTabs);

        Element body = tab.SubTab switch
        {
            EditorTab.Params => BuildParamsEditor(tab),
            EditorTab.Body => BuildBodyEditor(tab),
            EditorTab.Assertions => BuildAssertionsEditor(tab),
            EditorTab.Chain => BuildChainEditor(tab),
            _ => BuildHeadersEditor(tab),
        };
        body.Key = "subeditor-" + tab.SubTab;
        var bodyBox = Box.New().FlexGrow(1).FlexBasis(Dimension.Px(0));
        bodyBox.AddChild(body);
        col.AddChild(bodyBox);

        var footer = BuildEditorFooter(tab); footer.Key = "footer";
        col.AddChild(footer);
        return col;
    }

    private Element BuildTabsBar()
    {
        var bar = Flex.Row(gap: 2).Height(34).FlexShrink(0)
            .AlignItems(AlignItems.Stretch)
            .Padding(new Thickness(0, Azimuth.S2, 0, Azimuth.S2))
            .BorderColor(Azimuth.Border).BorderWidth(new Thickness(0, 0, 1, 0));

        for (int i = 0; i < _tabs.Count; i++)
        {
            var tab = _tabs[i];
            bool active = i == _activeTab;
            int index = i;
            var name = TabTitle(tab);

            var t = Flex.Row(gap: 6).AlignItems(AlignItems.Center)
                .Padding(new Thickness(0, Azimuth.S3, 0, Azimuth.S3))
                .CornerRadius(Azimuth.Radius1)
                .BorderColor(active ? Azimuth.Accent : Color.Transparent)
                .BorderWidth(new Thickness(0, 0, 0, 2))
                .Cursor(CursorType.Pointer)
                .AccessibleRole(AccessibilityRole.Button)
                .AccessibleName($"{name} tab{(tab.Dirty ? ", unsaved" : "")}");
            if (active) t.Background(Azimuth.Raised);
            t.AddChild(MethodChip(tab.Doc.Method, treeScale: true));
            t.AddChild(Text.New(name).FontSize(12f).TextColor(active ? Azimuth.Text : Azimuth.TextMuted));
            if (tab.Dirty) t.AddChild(Dot(Azimuth.Accent));
            if (active)
            {
                var close = Box.New().Padding(new Thickness(2)).Cursor(CursorType.Pointer)
                    .AccessibleRole(AccessibilityRole.Button).AccessibleName("Close tab")
                    .Children(Text.New("✕").FontSize(10f).TextColor(Azimuth.TextFaint));
                close.On<ClickEvent>(e => { e.StopPropagation = true; CloseActiveTab(); });
                t.AddChild(close);
            }
            t.OnClick(_ => { _activeTab = index; MarkDirty(); });
            if (!active) t.HoverBg(Azimuth.Raised, Color.Transparent);
            bar.AddChild(t);
        }
        return bar;
    }

    private string TabTitle(RequestTab tab) =>
        tab.Doc.Name ?? CollectionScanner.Prettify(System.IO.Path.GetFileNameWithoutExtension(tab.Path));

    // ── URL bar ──────────────────────────────────────────────────────────

    private Element BuildUrlBar(RequestTab tab)
    {
        var bar = Flex.Row(gap: Azimuth.S2).Height(48).FlexShrink(0)
            .AlignItems(AlignItems.Center)
            .Padding(new Thickness(0, Azimuth.S3, 0, Azimuth.S3));

        // Method dropdown chip.
        var method = Flex.Row(gap: 4).AlignItems(AlignItems.Center)
            .Padding(new Thickness(5, Azimuth.S2, 5, Azimuth.S2))
            .CornerRadius(Azimuth.Radius1)
            .Background(Azimuth.Tint(tab.Doc.Method != null ? Azimuth.MethodColor(tab.Doc.Method) : Azimuth.TextMuted))
            .Cursor(CursorType.Pointer)
            .AccessibleRole(AccessibilityRole.Button).AccessibleName($"Method: {tab.Doc.Method ?? "none"}")
            .AutomationId("method-chip");
        var mColor = tab.Doc.Method != null ? Azimuth.MethodColor(tab.Doc.Method) : Azimuth.TextMuted;
        method.AddChild(Ui.Mono(tab.Doc.Method ?? "?", 11f, mColor).FontWeight(FontWeight.SemiBold));
        method.AddChild(Text.New("▾").FontSize(9f).TextColor(mColor));
        method.On<ClickEvent>(e =>
        {
            e.StopPropagation = true;
            var box = e.CurrentTarget.Layout.AbsoluteBorderBox;
            _popoverX = box.X;
            _popoverY = box.Y + box.Height + 4f;
            _popover = Methods.Select(m => (m, (Action)(() => SetMethod(m)))).ToList();
            MarkDirty();
        });
        bar.AddChild(method);

        // URL: a chip-rendered display normally; a plain text input while editing.
        var urlBox = Flex.Row(gap: 0).FlexGrow(1).Height(32)
            .AlignItems(AlignItems.Center)
            .Padding(new Thickness(0, Azimuth.S2, 0, Azimuth.S2))
            .Background(Azimuth.Surface)
            .BorderColor(Azimuth.Border).BorderWidth(1f)
            .CornerRadius(Azimuth.Radius1);
        if (tab.UrlEditing)
        {
            var input = TextInput.New(tab.Doc.Url ?? "");
            input.OnTextChanged(url => { tab.Doc.SetRequestLine(tab.Doc.Method ?? "GET", url); MarkDirty(); })
                .Height(Dimension.Px(28))
                .FlexGrow(1).FlexShrink(1).FlexBasis(Dimension.Px(0))
                .FontFamily(Fonts.Mono).FontSize(13f)
                .TextColor(Azimuth.Text)
                .WithRef(_urlRef);
            input.AutomationId("url-input");
            input.On<BlurEvent>(_ => { tab.UrlEditing = false; MarkDirty(); });
            urlBox.AddChild(input);
        }
        else
        {
            var display = Flex.Row(gap: 2).FlexGrow(1).AlignItems(AlignItems.Center)
                .Cursor(CursorType.Text)
                .AccessibleRole(AccessibilityRole.Button)
                .AccessibleName($"URL: {tab.Doc.Url ?? "empty"}")
                .AutomationId("url-display");
            AddUrlChips(display, tab.Doc.Url ?? "");
            display.OnClick(_ => { tab.UrlEditing = true; _pendingUrlFocus = true; MarkDirty(); });
            urlBox.AddChild(display);
        }
        bar.AddChild(urlBox);

        // Send / Cancel.
        if (tab.Sending == null)
        {
            var send = Flex.Row(gap: 6).AlignItems(AlignItems.Center)
                .Padding(new Thickness(6, Azimuth.S4, 6, Azimuth.S4))
                .CornerRadius(Azimuth.Radius1)
                .BorderColor(Azimuth.Accent).BorderWidth(1f)
                .Cursor(CursorType.Pointer)
                .AccessibleRole(AccessibilityRole.Button).AccessibleName("Send")
                .AutomationId("send");
            send.AddChild(Text.New("Send").FontSize(12.5f).FontWeight(FontWeight.Medium).TextColor(Azimuth.Accent));
            send.AddChild(Ui.Kbd("Ctrl ↵"));
            send.OnClick(_ => SendActive());
            send.HoverBg(Azimuth.Tint(Azimuth.Accent), Color.Transparent);
            bar.AddChild(send);
        }
        else
        {
            var cancel = Flex.Row(gap: 6).AlignItems(AlignItems.Center)
                .Padding(new Thickness(6, Azimuth.S4, 6, Azimuth.S4))
                .CornerRadius(Azimuth.Radius1)
                .BorderColor(Azimuth.StatusWarn).BorderWidth(1f)
                .Cursor(CursorType.Pointer)
                .AccessibleRole(AccessibilityRole.Button).AccessibleName("Cancel")
                .AutomationId("cancel");
            cancel.AddChild(Text.New("Cancel").FontSize(12.5f).FontWeight(FontWeight.Medium).TextColor(Azimuth.StatusWarn));
            cancel.AddChild(Ui.Kbd("Esc"));
            cancel.OnClick(_ => CancelActive());
            bar.AddChild(cancel);
        }
        return bar;
    }

    private void SetMethod(string method)
    {
        if (ActiveTab is not { } tab) return;
        tab.Doc.SetRequestLine(method, tab.Doc.Url ?? "");
        MarkDirty();
    }

    /// <summary>The URL rendered as literals + variable chips: resolved chips in
    /// accent on accent tint, unresolved in warn, secrets masked with a keyring
    /// word. Clicking anywhere switches to the plain-text input.</summary>
    private void AddUrlChips(BoxElement into, string url)
    {
        if (url.Length == 0)
        {
            into.AddChild(Text.New("https://…").FontSize(13f).TextColor(Azimuth.TextFaint));
            return;
        }
        var resolver = Resolver();
        foreach (var seg in VariableResolver.Tokenize(url))
        {
            if (!seg.IsVariable)
            {
                into.AddChild(Ui.Mono(seg.Text, 13f, Azimuth.Text).FlexShrink(1)
                    .TextOverflow(TextOverflow.Ellipsis).MaxLines(1));
                continue;
            }
            bool secret = _env?.IsSecret(seg.Text) == true;
            bool resolved = resolver.Lookup(seg.Text) != null || secret;
            var fg = resolved ? Azimuth.Accent300 : Azimuth.StatusWarn;
            var chip = Flex.Row(gap: 3).AlignItems(AlignItems.Center)
                .Padding(new Thickness(1, 4, 1, 4))
                .CornerRadius(Azimuth.Radius1)
                .Background(Azimuth.Alpha(fg, 0.12))
                .FlexShrink(0)
                .AccessibleName($"variable {seg.Text}{(resolved ? "" : ", unresolved")}");
            chip.AddChild(Ui.ChipLabel("{{" + seg.Text + "}}", 12f, fg));
            if (secret)
                chip.AddChild(Ui.Mono("••• keyring", 9f, Azimuth.TextMuted));
            into.AddChild(chip);
        }
    }

    // ── Sub-editors ──────────────────────────────────────────────────────

    private Element BuildSubTabs(RequestTab tab)
    {
        var bar = Flex.Row(gap: Azimuth.S1).Height(32).FlexShrink(0)
            .AlignItems(AlignItems.Center)
            .Padding(new Thickness(0, Azimuth.S3, 0, Azimuth.S3))
            .BorderColor(Azimuth.Border).BorderWidth(new Thickness(0, 0, 1, 0));

        bar.AddChild(SubTabButton(tab, EditorTab.Params, $"Params {CountLabel(QueryParams(tab).Count)}"));
        bar.AddChild(SubTabButton(tab, EditorTab.Headers, $"Headers {CountLabel(tab.Doc.Headers.Count)}"));
        bar.AddChild(SubTabButton(tab, EditorTab.Body, tab.Doc.Body is { Type.Length: > 0 } b ? $"Body {b.Type}" : "Body"));
        bar.AddChild(SubTabButton(tab, EditorTab.Assertions, AssertionsTabLabel(tab)));
        var chainNode = Chain.Analyze(tab.Path, tab.Doc);
        bar.AddChild(SubTabButton(tab, EditorTab.Chain,
            $"Chain {CountLabel(chainNode.Uses.Count + chainNode.Produces.Count)}"));
        return bar;
    }

    private static string CountLabel(int n) => n > 0 ? n.ToString() : "";

    /// <summary>"Assertions", or with the live verdict: "Assertions 2✓" when
    /// all pass against the last response, "Assertions 1/3✗" when any fail.</summary>
    private string AssertionsTabLabel(RequestTab tab)
    {
        int count = tab.Doc.Asserts.Count;
        if (count == 0) return "Assertions";
        if (tab.Result?.Response is not { } response) return $"Assertions {count}";
        int failed = tab.Doc.Asserts.Count(a =>
            Directives.ParseAssert(a.Args, out _) is { } spec && !Directives.Evaluate(spec, response).Passed);
        return failed == 0 ? $"Assertions {count}✓" : $"Assertions {failed}/{count}✗";
    }

    private Element SubTabButton(RequestTab tab, EditorTab which, string label)
    {
        bool active = tab.SubTab == which;
        var b = Box.New()
            .Padding(new Thickness(5, Azimuth.S3, 5, Azimuth.S3))
            .CornerRadius(Azimuth.Radius1)
            .Cursor(CursorType.Pointer)
            .AccessibleRole(AccessibilityRole.Button).AccessibleName(label.TrimEnd())
            .AutomationId("subtab-" + which.ToString().ToLowerInvariant())
            .Children(Text.New(label.TrimEnd()).FontSize(12f)
                .FontWeight(active ? FontWeight.Medium : FontWeight.Regular)
                .TextColor(active ? Azimuth.Text : Azimuth.TextMuted));
        if (active) b.Background(Azimuth.Raised);
        b.OnClick(_ => { tab.SubTab = which; MarkDirty(); });
        if (!active) b.HoverBg(Azimuth.Raised, Color.Transparent);
        return b;
    }

    private Element BuildHeadersEditor(RequestTab tab)
    {
        var scroll = ScrollView.New();
        var col = Flex.Column(gap: 2).Padding(new Thickness(Azimuth.S2, Azimuth.S3, Azimuth.S3, Azimuth.S3));
        for (int i = 0; i < tab.Doc.Headers.Count; i++)
        {
            var header = tab.Doc.Headers[i];
            int index = i;
            var row = Flex.Row(gap: Azimuth.S2).Height(Azimuth.RowForm).FlexShrink(0).AlignItems(AlignItems.Center);
            row.AddChild(FormInput(header.Name, v => { tab.Doc.SetHeader(index, v, CurrentHeader(tab, index).Value); MarkDirty(); }, 220, $"header-name-{i}"));
            row.AddChild(FormInput(header.Value, v => { tab.Doc.SetHeader(index, CurrentHeader(tab, index).Name, v); MarkDirty(); }, 0, $"header-value-{i}"));
            row.AddChild(RemoveButton($"Remove header {header.Name}", () => { tab.Doc.RemoveHeader(index); MarkDirty(); }));
            col.AddChild(row);
        }
        col.AddChild(AddRowButton("＋ add header", "add-header", () =>
        {
            tab.Doc.AddHeader("Name", "value");
            MarkDirty();
        }));
        scroll.AddChild(col);
        return scroll;
    }

    private static ReqHeader CurrentHeader(RequestTab tab, int index) => tab.Doc.Headers[index];

    private Element BuildParamsEditor(RequestTab tab)
    {
        var scroll = ScrollView.New();
        var col = Flex.Column(gap: 2).Padding(new Thickness(Azimuth.S2, Azimuth.S3, Azimuth.S3, Azimuth.S3));
        var pairs = QueryParams(tab);
        for (int i = 0; i < pairs.Count; i++)
        {
            int index = i;
            var (name, value) = pairs[i];
            var row = Flex.Row(gap: Azimuth.S2).Height(Azimuth.RowForm).FlexShrink(0).AlignItems(AlignItems.Center);
            row.AddChild(FormInput(name, v => RewriteParam(tab, index, v, null), 220, $"param-name-{i}"));
            row.AddChild(FormInput(value, v => RewriteParam(tab, index, null, v), 0, $"param-value-{i}"));
            row.AddChild(RemoveButton($"Remove param {name}", () => RemoveParam(tab, index)));
            col.AddChild(row);
        }
        col.AddChild(AddRowButton("＋ add param", "add-param", () =>
        {
            var list = QueryParams(tab);
            list.Add(("name", "value"));
            WriteParams(tab, list);
        }));
        scroll.AddChild(col);
        return scroll;
    }

    /// <summary>Query pairs parsed from the raw URL text. Template-safe: the
    /// string is split on ? &amp; = literally, so <c>{{var}}</c> pieces pass
    /// through untouched.</summary>
    internal static List<(string Name, string Value)> QueryParams(RequestTab tab)
    {
        var url = tab.Doc.Url ?? "";
        int q = url.IndexOf('?');
        if (q < 0 || q == url.Length - 1) return [];
        return url[(q + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair =>
            {
                int eq = pair.IndexOf('=');
                return eq < 0 ? (pair, "") : (pair[..eq], pair[(eq + 1)..]);
            })
            .ToList();
    }

    private void RewriteParam(RequestTab tab, int index, string? name, string? value)
    {
        var list = QueryParams(tab);
        if (index >= list.Count) return;
        list[index] = (name ?? list[index].Name, value ?? list[index].Value);
        WriteParams(tab, list);
    }

    private void RemoveParam(RequestTab tab, int index)
    {
        var list = QueryParams(tab);
        if (index >= list.Count) return;
        list.RemoveAt(index);
        WriteParams(tab, list);
    }

    private void WriteParams(RequestTab tab, List<(string Name, string Value)> pairs)
    {
        var url = tab.Doc.Url ?? "";
        int q = url.IndexOf('?');
        string baseUrl = q < 0 ? url : url[..q];
        string query = string.Join('&', pairs.Select(p => p.Value.Length > 0 ? $"{p.Name}={p.Value}" : p.Name));
        tab.Doc.SetRequestLine(tab.Doc.Method ?? "GET", query.Length > 0 ? $"{baseUrl}?{query}" : baseUrl);
        MarkDirty();
    }

    private Element BuildBodyEditor(RequestTab tab)
    {
        var col = Flex.Column(gap: 0).FlexGrow(1).Padding(new Thickness(Azimuth.S2, Azimuth.S3, Azimuth.S3, Azimuth.S3));
        var box = Flex.Column(gap: 0).FlexGrow(1)
            .Padding(new Thickness(Azimuth.S2))
            .Background(Azimuth.Surface)
            .BorderColor(Azimuth.Border).BorderWidth(1f)
            .CornerRadius(Azimuth.Radius1);
        var area = TextArea.New(tab.Doc.Body?.Content ?? "");
        area.PlaceholderText("Request body — saved as a body block in the file.")
            .OnTextChanged(text => { tab.Doc.SetBody(tab.Doc.Body?.Type ?? "json", text); MarkDirty(); })
            .FontFamily(Fonts.Mono).FontSize(12f)
            .TextColor(Azimuth.Text)
            .FlexGrow(1);
        area.AutomationId("body-editor");
        box.AddChild(area);
        col.AddChild(box);
        return col;
    }

    private Element FormInput(string value, Action<string> onChanged, float width, string automationId)
    {
        var box = Flex.Row(gap: 0).Height(28).AlignItems(AlignItems.Center)
            .Padding(new Thickness(0, Azimuth.S2, 0, Azimuth.S2))
            .Background(Azimuth.Surface)
            .BorderColor(Azimuth.Border).BorderWidth(1f)
            .CornerRadius(Azimuth.Radius1);
        if (width > 0) box.Width(width).FlexShrink(0);
        else box.FlexGrow(1);
        var input = TextInput.New(value);
        input.OnTextChanged(onChanged)
            .Height(Dimension.Px(24))
            .FlexGrow(1).FlexShrink(1).FlexBasis(Dimension.Px(0))
            .FontFamily(Fonts.Mono).FontSize(12f)
            .TextColor(Azimuth.Text);
        input.AutomationId(automationId);
        box.AddChild(input);
        return box;
    }

    private Element RemoveButton(string accessibleName, Action onClick)
    {
        var b = Box.New().Width(22).Height(22)
            .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
            .CornerRadius(Azimuth.Radius1).Cursor(CursorType.Pointer)
            .AccessibleRole(AccessibilityRole.Button).AccessibleName(accessibleName)
            .Children(Text.New("✕").FontSize(10f).TextColor(Azimuth.TextFaint));
        b.OnClick(_ => onClick());
        return b.HoverBg(Azimuth.Raised, Color.Transparent);
    }

    private Element AddRowButton(string label, string automationId, Action onClick)
    {
        var b = Box.New()
            .Padding(new Thickness(5, Azimuth.S2, 5, Azimuth.S2))
            .CornerRadius(Azimuth.Radius1).Cursor(CursorType.Pointer)
            .AccessibleRole(AccessibilityRole.Button).AccessibleName(label.TrimStart('＋', ' '))
            .AutomationId(automationId)
            .Children(Text.New(label).FontSize(11.5f).TextColor(Azimuth.TextMuted));
        b.OnClick(_ => onClick());
        return b.HoverBg(Azimuth.Raised, Color.Transparent);
    }

    /// <summary>Status glyph + colour per the Azimuth register: 2xx pass ✓,
    /// 3xx skip −, 4xx warn !, 5xx fail ✗ — glyph always beside colour.</summary>
    internal static (string Glyph, Color Color) StatusFace(int status) => status switch
    {
        >= 200 and < 300 => ("✓", Azimuth.StatusPass),
        >= 300 and < 400 => ("−", Azimuth.StatusSkip),
        >= 400 and < 500 => ("!", Azimuth.StatusWarn),
        _ => ("✗", Azimuth.StatusFail),
    };

    private static string SizeLabel(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / (1024.0 * 1024.0):0.#} MB",
    };

    // ── Footer + external change bar ─────────────────────────────────────

    private Element BuildEditorFooter(RequestTab tab)
    {
        var ws = _workspace!;
        var footer = Flex.Row(gap: Azimuth.S2).Height(26).FlexShrink(0)
            .AlignItems(AlignItems.Center)
            .Padding(new Thickness(0, Azimuth.S3, 0, Azimuth.S3))
            .Background(Azimuth.Surface)
            .BorderColor(Azimuth.Border).BorderWidth(new Thickness(0, 1, 0, 0));

        string relative = System.IO.Path.GetRelativePath(ws.Root, tab.Path).Replace('\\', '/');
        footer.AddChild(Ui.Mono(relative, 10.5f, Azimuth.TextMuted)
            .TextOverflow(TextOverflow.Ellipsis).MaxLines(1).FlexShrink(1));
        footer.AddChild(Box.New().FlexGrow(1));
        footer.AddChild(tab.Dirty
            ? Flex.Row(gap: 4).AlignItems(AlignItems.Center).Children(
                Dot(Azimuth.Accent),
                Text.New("unsaved").FontSize(11f).TextColor(Azimuth.TextDim).AutomationId("save-state"))
            : Text.New("saved").FontSize(11f).TextColor(Azimuth.TextFaint).AutomationId("save-state"));
        footer.AddChild(Ui.Kbd("Ctrl S"));
        return footer;
    }

    private Element BuildExternalChangeBar(RequestTab tab)
    {
        var bar = Flex.Row(gap: Azimuth.S3).Height(34).FlexShrink(0)
            .AlignItems(AlignItems.Center)
            .Padding(new Thickness(0, Azimuth.S3, 0, Azimuth.S3))
            .Background(Azimuth.Alpha(Azimuth.StatusWarn, 0.10))
            .BorderColor(Azimuth.StatusWarn).BorderWidth(new Thickness(0, 0, 1, 0));
        bar.AutomationId("external-change-bar");

        bar.AddChild(Text.New($"! {System.IO.Path.GetFileName(tab.Path)} changed on disk")
            .FontSize(12f).TextColor(Azimuth.StatusWarn));
        bar.AddChild(Box.New().FlexGrow(1));
        bar.AddChild(BarAction("reload", "reload-external", () =>
        {
            if (TryReadFile(tab.Path) is { } text) tab.Doc = ReqDocument.Parse(text);
            tab.ExternalChanged = false;
            MarkDirty();
        }));
        bar.AddChild(BarAction("keep mine", "keep-mine", () =>
        {
            tab.ExternalChanged = false;
            MarkDirty();
        }));
        return bar;
    }

    private Element BarAction(string label, string automationId, Action onClick)
    {
        var b = Box.New()
            .Padding(new Thickness(4, Azimuth.S2, 4, Azimuth.S2))
            .CornerRadius(Azimuth.Radius1)
            .BorderColor(Azimuth.Border).BorderWidth(1f)
            .Cursor(CursorType.Pointer)
            .AccessibleRole(AccessibilityRole.Button).AccessibleName(label)
            .AutomationId(automationId)
            .Children(Text.New(label).FontSize(11.5f).TextColor(Azimuth.TextDim));
        b.OnClick(_ => onClick());
        return b.HoverBg(Azimuth.Raised, Color.Transparent);
    }

    // ── Popover (method picker) ──────────────────────────────────────────

    private Element BuildPopover(IReadOnlyList<(string Label, Action Run)> items)
    {
        float x = _popoverX, y = _popoverY;

        var layer = Box.New()
            .Width(Dimension.Percent(100)).Height(Dimension.Percent(100))
            .ZIndex(60);
        layer.Position.Type = PositionType.Absolute;
        layer.Position.Top = Dimension.Px(0);
        layer.Position.Left = Dimension.Px(0);
        layer.On<ClickEvent>(e => { e.StopPropagation = true; _popover = null; MarkDirty(); });

        var menu = Flex.Column(gap: 0)
            .Width(140).Padding(new Thickness(4))
            .Background(Azimuth.Surface).BorderColor(Azimuth.BorderStrong).BorderWidth(1f)
            .CornerRadius(Azimuth.Radius2);
        menu.Position.Type = PositionType.Absolute;
        menu.Position.Left = Dimension.Px(x);
        menu.Position.Top = Dimension.Px(y);
        menu.On<ClickEvent>(e => e.StopPropagation = true);

        foreach (var (label, run) in items)
        {
            var row = Flex.Row(gap: 8).AlignItems(AlignItems.Center)
                .Padding(new Thickness(6, 9, 6, 9)).CornerRadius(Azimuth.Radius1).Cursor(CursorType.Pointer)
                .AccessibleRole(AccessibilityRole.Button).AccessibleName(label)
                .AutomationId("popover-" + label);
            row.AddChild(Ui.Mono(label, 11f, Azimuth.MethodColor(label)).FontWeight(FontWeight.SemiBold));
            var action = run;
            row.On<ClickEvent>(e => { e.StopPropagation = true; _popover = null; action(); MarkDirty(); });
            row.HoverBg(Azimuth.Raised, Color.Transparent);
            menu.AddChild(row);
        }
        layer.AddChild(menu);
        return layer;
    }
}
