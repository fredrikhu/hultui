using System.Runtime.InteropServices;
using System.Text.Json;
using HultUI.Core.Accessibility;
using HultUI.WebView.Cef.Interop;

namespace HultUI.WebView.Cef;

/// <summary>
/// Step 5.6: the page's accessibility tree (spike L0, question 9; seam W11). While assistive
/// technology listens, CEF hands Chromium's tree over as data; the view mirrors it
/// (<see cref="CefAccessibleTree"/>) and publishes it as <see cref="AccessibleTree"/>, which
/// the web view element serves as its children. CEF cannot act on a node, but its ids are
/// DevTools' <c>backendDOMNodeId</c>s, so actions go through the DevTools session the view
/// already has.
/// </summary>
internal sealed unsafe partial class CefView
{
    private cef_accessibility_handler_t* _accessibility;
    private readonly CefAccessibleTree _axTree = new();
    private volatile IReadOnlyList<AccessibleContentNode> _axPublished = [];
    private volatile bool _axWanted;
    private bool _axRebuildQueued;

    public IReadOnlyList<AccessibleContentNode> AccessibleTree => _axPublished;

    /// <summary>For tests: how many nodes the mirror holds.</summary>
    internal int AccessibleMirrorSize => _axTree.Count;

    private void CreateAccessibilityHandler()
    {
        _accessibility = CefObject.New<cef_accessibility_handler_t>(this);
        _accessibility->on_accessibility_tree_change = &OnAccessibilityTreeChange;
        _accessibility->on_accessibility_location_change = &OnAccessibilityLocationChange;
        _render->get_accessibility_handler = &GetAccessibilityHandler;
    }

    private void ReleaseAccessibilityHandler()
    {
        CefObject.Release(_accessibility);
        _accessibility = null;
    }

    /// <summary>On only while assistive technology listens: a large page's tree costs hundreds
    /// of milliseconds to read, and every change after. UI thread.</summary>
    public void SetAccessibilityEnabled(bool enabled)
    {
        // Kept at once: asked before CEF runs (a controller made while someone listens), the
        // posted half is dropped, and the browser applies it when it is made.
        _axWanted = enabled;
        if (CefRuntime.IsRunning) OnUi(ApplyAccessibilityState);
    }

    /// <summary>CEF's UI thread, whenever the wish or the browser changes.</summary>
    private void ApplyAccessibilityState()
    {
        if (_browserHost == null) return;
        _browserHost->set_accessibility_state(_browserHost, _axWanted ? cef_state_t.STATE_ENABLED : cef_state_t.STATE_DISABLED);
        if (_axWanted) return;
        _axTree.Clear();
        Publish();
    }

    /// <summary>Rebuilt once per burst of updates: CEF sends many in a row as a page loads or scrolls.</summary>
    private void AccessibilityChanged()
    {
        if (_axRebuildQueued) return;
        _axRebuildQueued = true;
        OnUi(() =>
        {
            _axRebuildQueued = false;
            // The document's scroll, which CEF does not pass on, from the page; then the tree.
            if (_devTools is not { } devTools) { Publish(); return; }
            float scale = ScaleNow();
            _ = CefScript.Evaluate(devTools, "[scrollX, scrollY]").ContinueWith(t => OnUi(() =>
            {
                try
                {
                    // No reflection: an application may be trimmed or AOT, with it off.
                    if (t.IsCompletedSuccessfully)
                    {
                        using var scroll = JsonDocument.Parse(t.Result);
                        if (scroll.RootElement is { ValueKind: JsonValueKind.Array } at && at.GetArrayLength() == 2)
                            _axTree.SetDocumentScroll(at[0].GetDouble() * scale, at[1].GetDouble() * scale);
                    }
                }
                catch (JsonException) { }
                finally { Publish(); }
            }), TaskScheduler.Default);
        });
    }

    private void Publish()
    {
        _axPublished = _axWanted ? _axTree.Build() : [];
        CefLog.Info($"accessibility: {_axTree.Count} nodes mirrored, {_axPublished.Count} roots published");
        Raise(new WebEngineEvent.AccessibleTreeChanged());
    }

    // ──── Actions, through DevTools by backendDOMNodeId ────

    /// <summary>The element a node is, or the text's element for a text node.</summary>
    private const string Element = "const e = this.nodeType === 1 ? this : this.parentElement;";

    public void PerformAccessibleAction(int key, AccessibleContentAction action, string? value)
    {
        if (_devTools is not { } devTools) return;
        _ = action switch
        {
            AccessibleContentAction.Focus => devTools.Send("DOM.focus", w => w.WriteNumber("backendNodeId", key)),
            AccessibleContentAction.SetValue => CefScript.CallOnNode(devTools, key,
                "function(v) { " + Element + " e.focus(); e.value = v; " +
                "e.dispatchEvent(new Event('input', { bubbles: true })); e.dispatchEvent(new Event('change', { bubbles: true })); }",
                value ?? ""),
            // Invoke, toggle and select are what a click does to a button, a check box, an option.
            _ => CefScript.CallOnNode(devTools, key, "function() { " + Element + " e.click(); }", null),
        };
    }

    // ──── Callbacks ────

    [UnmanagedCallersOnly]
    private static cef_accessibility_handler_t* GetAccessibilityHandler(cef_render_handler_t* s)
    {
        try { return Of(s) is { } v ? CefObject.Give(v._accessibility) : null; }
        catch (Exception ex) { CefLog.Error("get_accessibility_handler", ex); return null; }
    }

    [UnmanagedCallersOnly]
    private static void OnAccessibilityTreeChange(cef_accessibility_handler_t* s, cef_value_t* value)
    {
        try
        {
            var change = CefAccessibleTree.FromCef(value);
            if (Of(s) is { _axWanted: true } v && v._axTree.ApplyTreeChange(change))
                v.AccessibilityChanged();
        }
        catch (Exception ex) { CefLog.Error("on_accessibility_tree_change", ex); }
        finally { CefObject.Release(value); }
    }

    [UnmanagedCallersOnly]
    private static void OnAccessibilityLocationChange(cef_accessibility_handler_t* s, cef_value_t* value)
    {
        try
        {
            var change = CefAccessibleTree.FromCef(value);
            if (Of(s) is { _axWanted: true } v && v._axTree.ApplyLocationChange(change))
                v.AccessibilityChanged();
        }
        catch (Exception ex) { CefLog.Error("on_accessibility_location_change", ex); }
        finally { CefObject.Release(value); }
    }
}
