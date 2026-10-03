using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using CefOsr.Capi;
using static CefOsr.Native;

namespace CefOsr;

/// <summary>
/// Question 9: accessibility. set_accessibility_state(STATE_ENABLED) and an accessibility
/// handler: what on_accessibility_tree_change and on_accessibility_location_change carry (as
/// JSON), how large for a real page, whether updates are incremental; acting on a node by id
/// (through DevTools); and whether anything appears on the AT-SPI bus. Run with --loop=mt.
/// </summary>
internal static unsafe class Q9
{
    static readonly ConcurrentQueue<(string kind, string json, double convertMs)> Updates = new();

    const string FormPage = """
        <!doctype html><html lang="en"><head><title>A11y form</title></head><body>
        <h1 id="h">Settings</h1>
        <label>Name <input id="name" value="Ada"></label>
        <label><input type="checkbox" id="cb"> Remember me</label>
        <button id="go" onclick="console.log('clicked go')" onfocus="console.log('focused go')">Press me</button>
        <ul><li>One</li><li><a href="#two">Two</a></li></ul>
        <img alt="A red square" src="data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='10' height='10'%3E%3Crect width='10' height='10' fill='red'/%3E%3C/svg%3E">
        </body></html>
        """;

    public static int Run(Options o)
    {
        if (Host.Initialize([], o) != 1) return 1;
        Host.Pump(() => Host.ContextReady, 15000);

        var handler = New<cef_accessibility_handler_t>("accessibility_handler");
        handler->on_accessibility_tree_change = &OnTree;
        handler->on_accessibility_location_change = &OnLocation;
        Host.AccessibilityHandler = () => (nint)handler;

        nint browser = 0;
        bool loaded = false;
        var page = new ConcurrentQueue<string>();
        Host.AfterCreated += b => { if (browser == 0) { AddRef((void*)b); browser = b; } };
        Host.LoadEnd += (b, f, s) => loaded = true;
        Host.ConsoleMessage += (b, m, src, line) => page.Enqueue(m);
        Host.ViewWidth = 800; Host.ViewHeight = 600; Host.Scale = 1.5f;

        var atspiBefore = AtSpiApps();
        Host.CreateBrowser("about:blank", sync: false);
        Host.Pump(() => browser != 0 && loaded, 10000);
        var b0 = (cef_browser_t*)browser;
        var h = b0->get_host(b0);
        Host.Wait(300);
        Log($"before enabling: {Updates.Count} updates");
        Host.PostUi(() => h->set_accessibility_state(h, cef_state_t.STATE_ENABLED));
        Host.Wait(300);
        Report("after set_accessibility_state(STATE_ENABLED) on about:blank", save: null);

        loaded = false;
        Navigate(b0, Host.DataUrl(FormPage));
        Host.Pump(() => loaded, 10000);
        Host.Wait(1000);
        Report("the form page loaded", save: "results/q9-tree-form-first.json");

        Eval(b0, "document.getElementById('h').textContent = 'Preferences'; document.getElementById('cb').checked = true;");
        Host.Wait(500);
        Report("heading text changed and the checkbox checked", save: "results/q9-tree-form-change.json");

        Eval(b0, "window.scrollBy(0, 0); document.body.style.marginTop = '40px';");
        Host.Wait(500);
        Report("everything moved down 40 px (margin-top)", save: "results/q9-location-change.json");

        // Acting on a node: CEF's handler is read-only; DevTools can.
        var reg = Q6.Attach(h);
        Q6.Call("DOM.enable", new { });
        Q6.Call("Accessibility.enable", new { });
        var full = Q6.Call("Accessibility.getFullAXTree", new { });
        using (var d = JsonDocument.Parse(full))
        {
            var nodes = d.RootElement.GetProperty("nodes");
            Log($"Accessibility.getFullAXTree: {nodes.GetArrayLength()} nodes, {full.Length} bytes");
            foreach (var n in nodes.EnumerateArray())
            {
                var role = n.TryGetProperty("role", out var r) ? r.GetProperty("value").GetString() : "";
                var name = n.TryGetProperty("name", out var nm) && nm.TryGetProperty("value", out var nv) ? nv.GetString() : "";
                if (role == "button")
                {
                    var id = n.GetProperty("nodeId").GetString();
                    var backend = n.GetProperty("backendDOMNodeId").GetInt32();
                    Log($"DevTools AX node: button '{name}', nodeId {id}, backendDOMNodeId {backend}; CEF's tree has the button as id {_buttonId}");
                    Log("DOM.focus(backendNodeId) → " + Q6.Call("DOM.focus", new { backendNodeId = backend }) +
                        "; document.activeElement → " + Q6.Call("Runtime.evaluate", new { expression = "document.activeElement.id + ', hasFocus ' + document.hasFocus()", returnByValue = true }));
                    var obj = Q6.Call("DOM.resolveNode", new { backendNodeId = backend });
                    var objectId = JsonDocument.Parse(obj).RootElement.GetProperty("object").GetProperty("objectId").GetString();
                    Log("Runtime.callFunctionOn(this.click()) → " + Q6.Call("Runtime.callFunctionOn", new { objectId, functionDeclaration = "function() { this.click(); }" }));
                }
            }
        }
        Host.Wait(300);
        while (page.TryDequeue(out var m)) Log("page: " + m);
        Report("after focusing and clicking the button through DevTools", save: null);

        // A real page.
        loaded = false;
        var sw = Stopwatch.StartNew();
        Navigate(b0, "https://en.wikipedia.org/wiki/Web_browser");
        Host.Pump(() => loaded, 20000);
        Host.Wait(3000);
        Report($"https://en.wikipedia.org/wiki/Web_browser, {sw.Elapsed.TotalSeconds:F1} s after navigating", save: null);
        Eval(b0, "window.scrollBy(0, 2000)");
        Host.Wait(1000);
        Report("wikipedia scrolled down 2000 px", save: null);

        var atspiDuring = AtSpiApps();
        Log($"AT-SPI registry's applications before CEF: [{atspiBefore}]; with a windowless browser and accessibility on: [{atspiDuring}]");

        Release(reg);
        Release(h);
        Host.Close(b0);
        Q1.Shutdown();
        return 0;
    }

    static int _buttonId = -1;

    static void Report(string what, string? save)
    {
        var list = new List<(string kind, string json, double ms)>();
        while (Updates.TryDequeue(out var u)) list.Add(u);
        int trees = list.Count(u => u.kind == "tree"), locs = list.Count(u => u.kind == "location");
        long bytes = list.Sum(u => (long)u.json.Length);
        Console.WriteLine($"--- {what}: {trees} tree change(s), {locs} location change(s), {bytes} bytes of JSON, " +
                          $"conversion {list.Sum(u => u.ms):F1} ms in all");
        foreach (var u in list.Take(6))
        {
            using var d = JsonDocument.Parse(u.json);
            Console.WriteLine($"      {u.kind}: {Shape(d.RootElement)}");
            FindButton(d.RootElement);
        }
        if (save != null && list.Count > 0)
            File.WriteAllText(save, JsonSerializer.Serialize(list.Select(u => JsonDocument.Parse(u.json).RootElement).ToArray(),
                new JsonSerializerOptions { WriteIndented = true }));
    }

    static void FindButton(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.Object)
        {
            if (e.TryGetProperty("role", out var r) && r.ValueKind == JsonValueKind.String && r.GetString() == "button"
                && e.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number)
                _buttonId = id.GetInt32();
            foreach (var p in e.EnumerateObject()) FindButton(p.Value);
        }
        else if (e.ValueKind == JsonValueKind.Array) foreach (var x in e.EnumerateArray()) FindButton(x);
    }

    /// <summary>A one-line summary: the keys at the top, the update and node counts.</summary>
    static string Shape(JsonElement root)
    {
        int nodes = 0, updates = 0, events = 0;
        void Walk(JsonElement e, string key)
        {
            if (e.ValueKind == JsonValueKind.Object)
            {
                if (e.TryGetProperty("id", out _) && e.TryGetProperty("role", out _)) nodes++;
                foreach (var p in e.EnumerateObject()) Walk(p.Value, p.Name);
            }
            else if (e.ValueKind == JsonValueKind.Array)
            {
                if (key == "updates") updates += e.GetArrayLength();
                if (key == "events") events += e.GetArrayLength();
                foreach (var x in e.EnumerateArray()) Walk(x, key);
            }
        }
        Walk(root, "");
        var keys = root.ValueKind == JsonValueKind.Object ? string.Join(", ", root.EnumerateObject().Select(p => p.Name)) : root.ValueKind.ToString();
        return $"keys [{keys}]; {updates} update(s), {events} event(s), {nodes} node(s)";
    }

    static string AtSpiApps()
    {
        try
        {
            var psi = new ProcessStartInfo("busctl", "--address=unix:path=/run/user/1000/at-spi/bus_0 call org.a11y.atspi.Registry " +
                "/org/a11y/atspi/accessible/root org.a11y.atspi.Accessible GetChildren")
            { RedirectStandardOutput = true, RedirectStandardError = true };
            using var p = Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit();
            return output.Trim();
        }
        catch (Exception e) { return "busctl: " + e.Message; }
    }

    static void Eval(cef_browser_t* b, string js)
    {
        var f = b->get_main_frame(b);
        var code = Make(js);
        var empty = Make("");
        f->execute_java_script(f, &code, &empty, 0);
        Release(f);
    }

    static void Navigate(cef_browser_t* b, string url)
    {
        var f = b->get_main_frame(b);
        var u = Make(url);
        f->load_url(f, &u);
        Release(f);
    }

    // ---- cef_value_t → JSON ----

    [UnmanagedCallersOnly]
    static void OnTree(cef_accessibility_handler_t* self, cef_value_t* value) => Take("tree", value);

    [UnmanagedCallersOnly]
    static void OnLocation(cef_accessibility_handler_t* self, cef_value_t* value) => Take("location", value);

    static void Take(string kind, cef_value_t* value)
    {
        var sw = Stopwatch.StartNew();
        var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms)) Write(w, value);
        Updates.Enqueue((kind, Encoding.UTF8.GetString(ms.ToArray()), sw.Elapsed.TotalMilliseconds));
        Release(value);
    }

    static void Write(Utf8JsonWriter w, cef_value_t* v)
    {
        if (v == null) { w.WriteNullValue(); return; }
        switch (v->get_type(v))
        {
            case cef_value_type_t.VTYPE_BOOL: w.WriteBooleanValue(v->get_bool(v) != 0); break;
            case cef_value_type_t.VTYPE_INT: w.WriteNumberValue(v->get_int(v)); break;
            case cef_value_type_t.VTYPE_DOUBLE: w.WriteNumberValue(v->get_double(v)); break;
            case cef_value_type_t.VTYPE_STRING: w.WriteStringValue(Native.Take(v->get_string(v))); break;
            case cef_value_type_t.VTYPE_DICTIONARY:
                var d = v->get_dictionary(v); WriteDict(w, d); Release(d); break;
            case cef_value_type_t.VTYPE_LIST:
                var l = v->get_list(v); WriteList(w, l); Release(l); break;
            case cef_value_type_t.VTYPE_BINARY: w.WriteStringValue("<binary>"); break;
            default: w.WriteNullValue(); break;
        }
    }

    static void WriteDict(Utf8JsonWriter w, cef_dictionary_value_t* d)
    {
        var keys = Cef.cef_string_list_alloc();
        d->get_keys(d, keys);
        ulong n = Cef.cef_string_list_size(keys);
        w.WriteStartObject();
        for (ulong i = 0; i < n; i++)
        {
            cef_string_utf16_t k = default;
            Cef.cef_string_list_value(keys, i, &k);
            w.WritePropertyName(Get(&k));
            var v = d->get_value(d, &k);
            Write(w, v);
            Release(v);
            Cef.cef_string_utf16_clear(&k);
        }
        w.WriteEndObject();
        Cef.cef_string_list_free(keys);
    }

    static void WriteList(Utf8JsonWriter w, cef_list_value_t* l)
    {
        ulong n = l->get_size(l);
        w.WriteStartArray();
        for (ulong i = 0; i < n; i++)
        {
            var v = l->get_value(l, i);
            Write(w, v);
            Release(v);
        }
        w.WriteEndArray();
    }
}
