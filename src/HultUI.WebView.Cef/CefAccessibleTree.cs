using System.Text.Json;
using HultUI.Core.Accessibility;
using HultUI.Core.Primitives;
using HultUI.WebView.Cef.Interop;

namespace HultUI.WebView.Cef;

/// <summary>
/// Step 5.6: a mirror of the page's accessibility tree, as CEF hands it over (spike L0,
/// question 9): Chromium's <c>AXTreeUpdate</c>s as plain values, a full tree first and
/// incremental updates after, and locations apart. Kept on CEF's UI thread; what the platform
/// reads is <see cref="Build"/>'s immutable tree, the page's nodes as <see cref="AccessibleContentNode"/>s
/// whose keys are Chromium's node ids (DevTools' <c>backendDOMNodeId</c>s).
/// </summary>
/// <remarks>
/// <para><b>Shape.</b> Values arrive as <c>cef_value_t</c> trees and are read once into
/// dictionaries, lists, strings and numbers (<see cref="FromCef"/>); tests feed the same shape
/// from JSON (<see cref="FromJson"/>), as the spike recorded it.</para>
/// <para><b>What is kept out.</b> Chromium's tree carries every layout box. Ignored nodes,
/// unnamed generic containers and paragraphs give their children to their parent; inline
/// text boxes and list markers are dropped, as is the text inside a control that is named by
/// it (a button's label). Frames' own trees (iframes) are not followed: the main frame's
/// tree only.</para>
/// </remarks>
internal sealed class CefAccessibleTree
{
    private sealed class Node
    {
        public int Id;
        public string Role = "";
        public HashSet<string> States = [];
        public Dictionary<string, object?> Attributes = [];
        public int[] Children = [];
        public double X, Y, Width, Height;
        public int Container;
    }

    private readonly Dictionary<int, Node> _nodes = [];
    private string? _treeId;
    private int _root;
    private int _focus;

    /// <summary>The document's scroll, in device pixels, as the page says it: Chromium's own
    /// root scroll offset never reaches CEF's callbacks (see <see cref="SetDocumentScroll"/>).</summary>
    private (double X, double Y) _documentScroll;

    /// <summary>
    /// The document's scroll offset, in device pixels. On a scroll CEF calls its location
    /// callback with an empty list: newer Chromium sends scroll offsets apart from locations,
    /// and CEF's conversion drops them. So the view asks the page instead, and the root's
    /// offset comes from here. True when it changed.
    /// </summary>
    public bool SetDocumentScroll(double x, double y)
    {
        if (_documentScroll == (x, y)) return false;
        _documentScroll = (x, y);
        return true;
    }

    public int Count => _nodes.Count;


    /// <summary>Forgets the tree: accessibility went off, or the page went.</summary>
    public void Clear()
    {
        _nodes.Clear();
        _treeId = null;
        _root = _focus = 0;
        _documentScroll = default;
    }

    // ──── Updates ────

    /// <summary>An <c>on_accessibility_tree_change</c>: <c>{ax_tree_id, events, updates}</c>.
    /// True when the main frame's tree changed.</summary>
    public bool ApplyTreeChange(object? value)
    {
        if (value is not Dictionary<string, object?> change) return false;
        var treeId = change.GetValueOrDefault("ax_tree_id") as string;
        if (change.GetValueOrDefault("updates") is not List<object?> updates) return false;
        bool changed = false;
        foreach (var u in updates)
        {
            if (u is not Dictionary<string, object?> update) continue;
            var data = update.GetValueOrDefault("tree_data") as Dictionary<string, object?>;
            // Only the main frame's tree: one with no parent tree. A new one (a navigation) replaces it.
            if (data is not null && treeId != _treeId)
            {
                if (data.ContainsKey("parent_tree_id") && data["parent_tree_id"] is string { Length: > 0 }) continue;
                Clear();
                _treeId = treeId;
            }
            if (treeId != _treeId) continue;
            if (data is not null) _focus = Int(data.GetValueOrDefault("focus_id"));
            if (update.GetValueOrDefault("root_id") is { } root) _root = Int(root);
            if (Int(update.GetValueOrDefault("node_id_to_clear")) is var clear and not 0 && _nodes.TryGetValue(clear, out var cleared))
            {
                foreach (var child in cleared.Children) Remove(child);
                cleared.Children = [];
            }
            if (update.GetValueOrDefault("nodes") is List<object?> nodes)
                foreach (var n in nodes)
                    if (n is Dictionary<string, object?> d) Put(d);
            changed = true;
        }
        if (changed) Sweep();
        return changed;
    }

    /// <summary>An <c>on_accessibility_location_change</c>: a list of <c>{ax_tree_id, id,
    /// new_location: {bounds, offset_container_id}}</c>. True when a node of the tree moved.</summary>
    public bool ApplyLocationChange(object? value)
    {
        if (value is not List<object?> changes) return false;
        // An empty list is what a scroll sends (see SetDocumentScroll): something moved.
        bool changed = changes.Count == 0 && _nodes.Count > 0;
        foreach (var c in changes)
        {
            if (c is not Dictionary<string, object?> change || change.GetValueOrDefault("ax_tree_id") as string != _treeId) continue;
            if (!_nodes.TryGetValue(Int(change.GetValueOrDefault("id")), out var node)) continue;
            if (change.GetValueOrDefault("new_location") is not Dictionary<string, object?> location) continue;
            if (location.GetValueOrDefault("bounds") is Dictionary<string, object?> bounds) Place(node, bounds);
            node.Container = Int(location.GetValueOrDefault("offset_container_id"));
            changed = true;
        }
        return changed;
    }

    private void Put(Dictionary<string, object?> d)
    {
        int id = Int(d.GetValueOrDefault("id"));
        if (!_nodes.TryGetValue(id, out var node)) _nodes[id] = node = new Node { Id = id };
        node.Role = d.GetValueOrDefault("role") as string ?? "";
        node.States = d.GetValueOrDefault("state") is List<object?> states
            ? [.. states.OfType<string>().Where(s => s.Length > 0)]
            : [];
        node.Attributes = d.GetValueOrDefault("attributes") as Dictionary<string, object?> ?? [];
        node.Children = d.GetValueOrDefault("child_ids") is List<object?> children ? [.. children.Select(Int)] : [];
        if (d.GetValueOrDefault("location") is Dictionary<string, object?> bounds) Place(node, bounds);
        node.Container = Int(d.GetValueOrDefault("offset_container_id"));
    }

    private static void Place(Node node, Dictionary<string, object?> bounds)
    {
        node.X = Num(bounds.GetValueOrDefault("x"));
        node.Y = Num(bounds.GetValueOrDefault("y"));
        node.Width = Num(bounds.GetValueOrDefault("width"));
        node.Height = Num(bounds.GetValueOrDefault("height"));
    }

    private void Remove(int id)
    {
        if (!_nodes.Remove(id, out var node)) return;
        foreach (var child in node.Children) Remove(child);
    }

    /// <summary>Drops what the root no longer reaches: a node moved away is sent again under its new parent.</summary>
    private void Sweep()
    {
        if (!_nodes.ContainsKey(_root)) return;
        var reached = new HashSet<int>();
        var pending = new Stack<int>([_root]);
        while (pending.TryPop(out var id))
            if (reached.Add(id) && _nodes.TryGetValue(id, out var n))
                foreach (var child in n.Children) pending.Push(child);
        foreach (var id in _nodes.Keys.Where(k => !reached.Contains(k)).ToList()) _nodes.Remove(id);
    }

    // ──── The tree the platform reads ────

    /// <summary>The page's nodes, as the platform's accessibility tree takes them; bounds in
    /// device pixels from the page's top-left.</summary>
    public IReadOnlyList<AccessibleContentNode> Build()
    {
        if (!_nodes.TryGetValue(_root, out var root)) return [];
        var absolute = new Dictionary<int, (double X, double Y)>();
        return Make(root, absolute, labelledByParent: false);
    }

    private List<AccessibleContentNode> Make(Node node, Dictionary<int, (double X, double Y)> absolute, bool labelledByParent)
    {
        if (Dropped(node) || (labelledByParent && node.Role is "staticText")) return [];
        bool namesFromContents = (node.Attributes.GetValueOrDefault("nameFrom") as string) == "contents"
            && node.Role is not ("rootWebArea" or "genericContainer" or "paragraph" or "staticText");
        var children = new List<AccessibleContentNode>();
        foreach (var id in node.Children)
            if (_nodes.TryGetValue(id, out var child))
                children.AddRange(Make(child, absolute, namesFromContents));
        if (Flattened(node)) return children;

        var (x, y) = Absolute(node, absolute);
        var role = Role(node);
        // A field's text is its value: its insides are the editor's own boxes.
        if (role == AccessibilityRole.TextInput) children = [];
        var checkedState = node.Attributes.GetValueOrDefault("checkedState") as string;
        return
        [
            new AccessibleContentNode(node.Id, role, Name(node), new Rect((float)x, (float)y, (float)node.Width, (float)node.Height))
            {
                Description = node.Attributes.GetValueOrDefault("description") as string,
                Value = role == AccessibilityRole.TextInput ? node.Attributes.GetValueOrDefault("value") as string ?? "" : null,
                IsFocusable = node.States.Contains("focusable"),
                IsFocused = node.Id == _focus,
                IsEnabled = (node.Attributes.GetValueOrDefault("restriction") as string) != "disabled",
                IsToggled = checkedState switch { "true" => true, "false" => false, _ => null },
                Expanded = node.States.Contains("expanded") ? true : node.States.Contains("collapsed") ? false : null,
                Children = children,
            },
        ];
    }

    /// <summary>A node's top-left from the page's: its location is relative to its offset
    /// container, whose scroll it moves with.</summary>
    private (double X, double Y) Absolute(Node node, Dictionary<int, (double X, double Y)> absolute)
    {
        if (absolute.TryGetValue(node.Id, out var known)) return known;
        (double X, double Y) at = (node.X, node.Y);
        if (node.Container != 0 && node.Container != node.Id && _nodes.TryGetValue(node.Container, out var container))
        {
            var origin = Absolute(container, absolute);
            var scroll = container.Id == _root
                ? _documentScroll
                : (Num(container.Attributes.GetValueOrDefault("scrollX")), Num(container.Attributes.GetValueOrDefault("scrollY")));
            at = (origin.X + node.X - scroll.Item1, origin.Y + node.Y - scroll.Item2);
        }
        absolute[node.Id] = at;
        return at;
    }

    private static bool Dropped(Node node) =>
        node.Role is "inlineTextBox" or "listMarker" || node.States.Contains("invisible");

    private static bool Flattened(Node node) =>
        node.States.Contains("ignored")
        || (node.Role is "genericContainer" or "paragraph" or "labelText" or "none" or "presentational" or "section"
            or "group" or "region" or "unknown" or "pre" or "blockquote" or "figure" or "article" or "main"
            or "navigation" or "banner" or "contentInfo" or "complementary" or "form" or "row" or "cell"
            or "gridCell" or "table" or "rowGroup" or "column" or "columnHeader" or "rowHeader" or "abbr"
            or "emphasis" or "strong" or "mark" or "time" or "code" or "deletion" or "insertion"
            && string.IsNullOrEmpty(Name(node)));

    private static string? Name(Node node) => node.Attributes.GetValueOrDefault("name") as string is { Length: > 0 } name ? name : null;

    internal static AccessibilityRole Role(string role, string? checkedState = null) => role switch
    {
        "rootWebArea" or "webArea" or "document" => AccessibilityRole.Document,
        "button" or "popUpButton" or "comboBoxMenuButton" => AccessibilityRole.Button,
        "toggleButton" => checkedState is null ? AccessibilityRole.Button : AccessibilityRole.Switch,
        "link" => AccessibilityRole.Link,
        "heading" => AccessibilityRole.Heading,
        "textField" or "searchBox" or "textFieldWithComboBox" or "date" or "dateTime" or "inputTime" => AccessibilityRole.TextInput,
        "checkBox" or "menuItemCheckBox" => AccessibilityRole.Checkbox,
        "radioButton" or "menuItemRadio" => AccessibilityRole.RadioButton,
        "switch" => AccessibilityRole.Switch,
        "comboBoxSelect" or "comboBoxGrouping" => AccessibilityRole.ComboBox,
        "listBox" or "list" or "menuListPopup" or "menu" or "menuBar" or "descriptionList" => AccessibilityRole.List,
        "listItem" or "listBoxOption" or "menuListOption" or "menuItem" or "descriptionListTerm"
            or "descriptionListDetail" => AccessibilityRole.ListItem,
        "image" or "svgRoot" or "canvas" => AccessibilityRole.Image,
        "slider" => AccessibilityRole.Slider,
        "spinButton" => AccessibilityRole.SpinButton,
        "progressIndicator" or "meter" => AccessibilityRole.ProgressBar,
        "tab" => AccessibilityRole.Tab,
        "tabPanel" => AccessibilityRole.TabPanel,
        "dialog" or "alertDialog" => AccessibilityRole.Dialog,
        "alert" or "status" => AccessibilityRole.Alert,
        "splitter" or "separator" => AccessibilityRole.Separator,
        "tree" or "treeGrid" => AccessibilityRole.Tree,
        "treeItem" => AccessibilityRole.TreeItem,
        "staticText" => AccessibilityRole.Label,
        _ => AccessibilityRole.Group,
    };

    private static AccessibilityRole Role(Node node) =>
        Role(node.Role, node.Attributes.GetValueOrDefault("checkedState") as string);

    private static int Int(object? value) => value switch
    {
        int i => i,
        long l => (int)l,
        double d => (int)d,
        _ => 0,
    };

    private static double Num(object? value) => value switch
    {
        int i => i,
        long l => l,
        double d => d,
        _ => 0,
    };

    // ──── Values ────

    /// <summary>A <c>cef_value_t</c> as dictionaries, lists, strings and numbers; releases nothing
    /// (the caller owns <paramref name="value"/>). CEF's UI thread.</summary>
    public static unsafe object? FromCef(cef_value_t* value)
    {
        if (value == null) return null;
        switch (value->get_type(value))
        {
            case cef_value_type_t.VTYPE_BOOL: return value->get_bool(value) != 0;
            case cef_value_type_t.VTYPE_INT: return value->get_int(value);
            case cef_value_type_t.VTYPE_DOUBLE: return value->get_double(value);
            case cef_value_type_t.VTYPE_STRING: return CefString.Take(value->get_string(value));
            case cef_value_type_t.VTYPE_DICTIONARY:
            {
                var d = value->get_dictionary(value);
                try { return FromCef(d); }
                finally { CefObject.Release(d); }
            }
            case cef_value_type_t.VTYPE_LIST:
            {
                var l = value->get_list(value);
                try
                {
                    var list = new List<object?>((int)l->get_size(l));
                    for (ulong i = 0, n = l->get_size(l); i < n; i++)
                    {
                        var v = l->get_value(l, i);
                        try { list.Add(FromCef(v)); }
                        finally { CefObject.Release(v); }
                    }
                    return list;
                }
                finally { CefObject.Release(l); }
            }
            default: return null;
        }
    }

    private static unsafe Dictionary<string, object?> FromCef(cef_dictionary_value_t* d)
    {
        var result = new Dictionary<string, object?>();
        var keys = Capi.cef_string_list_alloc();
        try
        {
            d->get_keys(d, keys);
            for (ulong i = 0, n = Capi.cef_string_list_size(keys); i < n; i++)
            {
                cef_string_utf16_t k = default;
                Capi.cef_string_list_value(keys, i, &k);
                var v = d->get_value(d, &k);
                try { result[CefString.Read(&k)] = FromCef(v); }
                finally
                {
                    CefObject.Release(v);
                    CefString.Clear(&k);
                }
            }
        }
        finally { Capi.cef_string_list_free(keys); }
        return result;
    }

    /// <summary>The same shape from JSON, as the spike recorded CEF's values. For tests.</summary>
    public static object? FromJson(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Object => e.EnumerateObject().ToDictionary(p => p.Name, p => FromJson(p.Value)),
        JsonValueKind.Array => e.EnumerateArray().Select(FromJson).ToList(),
        JsonValueKind.String => e.GetString(),
        JsonValueKind.Number => e.TryGetInt32(out var i) ? i : e.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };
}
