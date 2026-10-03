using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using HultUI.WebView.Cef.Interop;

namespace HultUI.WebView.Cef;

/// <summary>
/// Step 5.4: navigation and its outcome, new windows, the page's process, and the four
/// requests the page waits on (context menu, script dialog, download, permission), each
/// answered exactly once and each with a default that asks nothing of the application.
/// </summary>
/// <remarks>
/// <para><b>Waiting.</b> A navigation is answered on CEF's thread, which waits for HultUI's
/// (the allowed direction: HultUI's thread never waits for CEF's). The four requests do not
/// block anything: CEF's callback is kept, with the reference CEF gave, and the answer goes
/// back to it on CEF's thread whenever it comes.</para>
/// <para><b>Nothing left waiting.</b> When the view is disposed, or the process exits, every
/// navigation still waiting is cancelled and every request answered with its default, or the
/// browser (and <c>cef_shutdown</c>, made while HultUI's thread is gone) would wait for an
/// answer that cannot come.</para>
/// </remarks>
internal sealed unsafe partial class CefView
{
    private cef_request_handler_t* _request;
    private cef_context_menu_handler_t* _menu;
    private cef_jsdialog_handler_t* _dialogs;
    private cef_download_handler_t* _downloads;
    private cef_permission_handler_t* _permissions;

    /// <summary>Answers still owed to CEF, run with their defaults if the view goes first.</summary>
    private readonly ConcurrentDictionary<long, Action> _owed = new();
    private long _nextOwed;

    private long _navigationId;
    /// <summary>Navigations allowed and not yet ended, oldest first, by the address each is
    /// loading (CEF's UI thread).</summary>
    private readonly List<(long Id, string Url)> _navigations = [];
    private string? _cancelledUrl;

    private void CreatePolicyHandlers()
    {
        _request = CefObject.New<cef_request_handler_t>(this);
        _request->on_before_browse = &OnBeforeBrowse;
        _request->on_render_process_terminated = &OnRenderProcessTerminated;
        _client->get_request_handler = &GetRequestHandler;

        _load->on_load_end = &OnLoadEnd;
        _load->on_load_error = &OnLoadError;

        _menu = CefObject.New<cef_context_menu_handler_t>(this);
        _menu->run_context_menu = &RunContextMenu;
        _client->get_context_menu_handler = &GetContextMenuHandler;

        _dialogs = CefObject.New<cef_jsdialog_handler_t>(this);
        _dialogs->on_jsdialog = &OnJsDialog;
        _dialogs->on_before_unload_dialog = &OnBeforeUnloadDialog;
        _client->get_jsdialog_handler = &GetJsDialogHandler;

        _downloads = CefObject.New<cef_download_handler_t>(this);
        _downloads->can_download = &CanDownload;
        _downloads->on_before_download = &OnBeforeDownload;
        _downloads->on_download_updated = &OnDownloadUpdated;
        _client->get_download_handler = &GetDownloadHandler;

        _permissions = CefObject.New<cef_permission_handler_t>(this);
        _permissions->on_request_media_access_permission = &OnMediaAccess;
        _permissions->on_show_permission_prompt = &OnPermissionPrompt;
        _client->get_permission_handler = &GetPermissionHandler;

        CefRuntime.ShuttingDown += AnswerEverythingOwed;
    }

    private void ReleasePolicyHandlers()
    {
        CefRuntime.ShuttingDown -= AnswerEverythingOwed;
        CefObject.Release(_request); _request = null;
        CefObject.Release(_menu); _menu = null;
        CefObject.Release(_dialogs); _dialogs = null;
        CefObject.Release(_downloads); _downloads = null;
        CefObject.Release(_permissions); _permissions = null;
    }

    /// <summary>Keeps <paramref name="byDefault"/> until <see cref="Settle"/>; its key.</summary>
    private long Owe(Action byDefault)
    {
        long key = Interlocked.Increment(ref _nextOwed);
        _owed[key] = byDefault;
        return key;
    }

    /// <summary>The answer came: it no longer needs a default. True the first time only.</summary>
    private bool Settle(long key) => _owed.TryRemove(key, out _);

    /// <summary>Disposal and process exit: every answer still owed, with its default.</summary>
    private void AnswerEverythingOwed()
    {
        foreach (var key in _owed.Keys)
            if (_owed.TryRemove(key, out var byDefault))
                try { byDefault(); }
                catch (Exception ex) { CefLog.Error("an answer owed to CEF", ex); }
    }

    private bool IsReady { get { lock (_gate) return _phase == Phase.Ready; } }

    // ──── Navigation ────

    /// <summary>
    /// A main-frame navigation, before any request: the controller's handlers decide. CEF's
    /// thread waits for the answer; one still waiting when the view or the process goes is
    /// cancelled.
    /// </summary>
    private bool Browse(cef_frame_t* frame, string url, bool userGesture, bool isRedirect)
    {
        if (frame->is_main(frame) == 0 || !IsReady || !Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        // A redirect is the navigation it redirects, asked again.
        long id = isRedirect && _navigations.Count > 0 ? _navigations[^1].Id : Interlocked.Increment(ref _navigationId);
        var answered = new ManualResetEventSlim();
        bool allow = true;
        var request = new WebNavigationRequest(id, StringPages.IsStringPage(uri) ? AboutBlank : uri, userGesture, isRedirect, a =>
        {
            allow = a;
            answered.Set();
        });
        long owed = Owe(() => { allow = false; answered.Set(); });
        if (!IsReady && Settle(owed)) return true;   // disposed meanwhile: nothing will answer
        Raise(new WebEngineEvent.NavigationStarting(request));
        answered.Wait();
        Settle(owed);
        _navigations.RemoveAll(n => n.Id == id);
        if (allow)
        {
            _navigations.Add((id, url));
            return false;
        }
        _cancelledUrl = url;
        Raise(new WebEngineEvent.NavigationCompleted(id, false, null, "Cancelled"));
        return true;
    }

    /// <summary>
    /// The navigation loading <paramref name="url"/> ended: it, or the newest if none matches.
    /// Any older one still open was replaced, and ends cancelled.
    /// </summary>
    private void Ended(string url, Func<long, WebEngineEvent> completed)
    {
        int at = _navigations.FindLastIndex(n => n.Url == url);
        if (at < 0) at = _navigations.Count - 1;
        if (at < 0) return;
        var ended = _navigations.Take(at + 1).ToList();
        _navigations.RemoveRange(0, at + 1);
        foreach (var (old, _) in ended.SkipLast(1)) Raise(new WebEngineEvent.NavigationCompleted(old, false, null, "Cancelled"));
        Raise(completed(ended[^1].Id));
    }

    private void LoadEnded(cef_frame_t* frame, int httpStatus)
    {
        if (frame->is_main(frame) == 0) return;
        // As WebView2 has it: a server's error page (4xx, 5xx) loaded, but the navigation failed.
        bool ok = httpStatus is 0 or < 400;
        Ended(CefString.Take(frame->get_url(frame)),
            id => new WebEngineEvent.NavigationCompleted(id, ok, httpStatus > 0 ? httpStatus : null, ok ? null : "Unknown"));
    }

    private void LoadFailed(cef_frame_t* frame, cef_errorcode_t code, string url)
    {
        if (frame->is_main(frame) == 0) return;
        if (code == cef_errorcode_t.ERR_ABORTED && url == _cancelledUrl) { _cancelledUrl = null; return; }   // ours, reported
        if (code == cef_errorcode_t.ERR_ABORTED && _navigations.FindIndex(n => n.Url == url) is var at
            && at >= 0 && at < _navigations.Count - 1)
        {
            // Replaced by a newer navigation, which goes on.
            var (old, _) = _navigations[at];
            _navigations.RemoveAt(at);
            Raise(new WebEngineEvent.NavigationCompleted(old, false, null, "Cancelled"));
            return;
        }
        Ended(url, id => new WebEngineEvent.NavigationCompleted(id, false, null, ErrorText(code)));
    }

    /// <summary>A network error as WebView2's status names read (and the fake's), so an
    /// application tests one set of names.</summary>
    internal static string ErrorText(cef_errorcode_t code) => code switch
    {
        cef_errorcode_t.ERR_ABORTED => "Cancelled",
        cef_errorcode_t.ERR_NAME_NOT_RESOLVED or cef_errorcode_t.ERR_NAME_RESOLUTION_FAILED => "HostNameNotResolved",
        cef_errorcode_t.ERR_CONNECTION_REFUSED or cef_errorcode_t.ERR_CONNECTION_FAILED or cef_errorcode_t.ERR_ADDRESS_UNREACHABLE => "CannotConnect",
        cef_errorcode_t.ERR_CONNECTION_RESET => "ConnectionReset",
        cef_errorcode_t.ERR_CONNECTION_ABORTED => "ConnectionAborted",
        cef_errorcode_t.ERR_CONNECTION_CLOSED or cef_errorcode_t.ERR_INTERNET_DISCONNECTED => "Disconnected",
        cef_errorcode_t.ERR_TIMED_OUT or cef_errorcode_t.ERR_CONNECTION_TIMED_OUT => "Timeout",
        cef_errorcode_t.ERR_ADDRESS_INVALID => "ServerUnreachable",
        cef_errorcode_t.ERR_CERT_COMMON_NAME_INVALID => "CertificateCommonNameIsIncorrect",
        cef_errorcode_t.ERR_CERT_DATE_INVALID => "CertificateExpired",
        cef_errorcode_t.ERR_CERT_REVOKED => "CertificateRevoked",
        cef_errorcode_t.ERR_CERT_AUTHORITY_INVALID or cef_errorcode_t.ERR_CERT_INVALID => "CertificateIsInvalid",
        cef_errorcode_t.ERR_BAD_SSL_CLIENT_AUTH_CERT => "ClientCertificateContainsErrors",
        cef_errorcode_t.ERR_TOO_MANY_REDIRECTS or cef_errorcode_t.ERR_UNSAFE_REDIRECT => "RedirectFailed",
        cef_errorcode_t.ERR_INVALID_RESPONSE or cef_errorcode_t.ERR_EMPTY_RESPONSE => "HttpInvalidServerResponse",
        cef_errorcode_t.ERR_PROXY_AUTH_REQUESTED => "ValidProxyAuthenticationRequired",
        _ => "Unknown",   // WebView2's name for the rest
    };

    // ──── Context menu ────

    /// <summary>
    /// CEF's menu model as the contract's entries: named as WebView2's are, so the standard
    /// menu (editing, copying, spelling) keeps the same ones. CEF's browser entries (back,
    /// reload, print, view source) carry names the standard menu leaves out.
    /// </summary>
    internal static string MenuName(int commandId) => commandId switch
    {
        (int)cef_menu_id_t.MENU_ID_UNDO => "undo",
        (int)cef_menu_id_t.MENU_ID_REDO => "redo",
        (int)cef_menu_id_t.MENU_ID_CUT => "cut",
        (int)cef_menu_id_t.MENU_ID_COPY => "copy",
        (int)cef_menu_id_t.MENU_ID_PASTE => "paste",
        (int)cef_menu_id_t.MENU_ID_PASTE_MATCH_STYLE => "pasteAndMatchStyle",
        (int)cef_menu_id_t.MENU_ID_DELETE => "delete",
        (int)cef_menu_id_t.MENU_ID_SELECT_ALL => "selectAll",
        >= (int)cef_menu_id_t.MENU_ID_SPELLCHECK_SUGGESTION_0 and <= (int)cef_menu_id_t.MENU_ID_SPELLCHECK_SUGGESTION_LAST => "spellCheck",
        (int)cef_menu_id_t.MENU_ID_NO_SPELLING_SUGGESTIONS or (int)cef_menu_id_t.MENU_ID_ADD_TO_DICTIONARY => "spellCheck",
        (int)cef_menu_id_t.MENU_ID_BACK => "back",
        (int)cef_menu_id_t.MENU_ID_FORWARD => "forward",
        (int)cef_menu_id_t.MENU_ID_RELOAD or (int)cef_menu_id_t.MENU_ID_RELOAD_NOCACHE => "reload",
        (int)cef_menu_id_t.MENU_ID_STOPLOAD => "stop",
        (int)cef_menu_id_t.MENU_ID_FIND => "find",
        (int)cef_menu_id_t.MENU_ID_PRINT => "print",
        (int)cef_menu_id_t.MENU_ID_VIEW_SOURCE => "viewPageSource",
        _ => "other",
    };

    /// <summary>The "Inspect" entry's command: the first id CEF leaves to the embedder.</summary>
    private const int InspectCommand = (int)cef_menu_id_t.MENU_ID_USER_FIRST;

    private static List<WebContextMenuItem> MenuItems(cef_menu_model_t* model)
    {
        var items = new List<WebContextMenuItem>();
        ulong n = model->get_count(model);
        for (ulong i = 0; i < n; i++)
        {
            var type = model->get_type_at(model, i);
            if (type == cef_menu_item_type_t.MENUITEMTYPE_SEPARATOR) { items.Add(WebContextMenuItem.Separator()); continue; }
            int id = model->get_command_id_at(model, i);
            var label = CefString.Take(model->get_label_at(model, i)).Replace("&", "");
            var kind = type switch
            {
                cef_menu_item_type_t.MENUITEMTYPE_CHECK => WebContextMenuItemKind.CheckBox,
                cef_menu_item_type_t.MENUITEMTYPE_RADIO => WebContextMenuItemKind.Radio,
                cef_menu_item_type_t.MENUITEMTYPE_SUBMENU => WebContextMenuItemKind.Submenu,
                _ => WebContextMenuItemKind.Command,
            };
            List<WebContextMenuItem> children = [];
            if (kind == WebContextMenuItemKind.Submenu && model->get_sub_menu_at(model, i) is var sub && sub != null)
            {
                children = MenuItems(sub);
                CefObject.Release(sub);
            }
            items.Add(new WebContextMenuItem(MenuName(id), label, kind, id)
            {
                IsEnabled = model->is_enabled_at(model, i) != 0,
                IsChecked = model->is_checked_at(model, i) != 0,
                Children = children,
            });
        }
        return items;
    }

    private bool RunMenu(cef_context_menu_params_t* p, cef_menu_model_t* model, cef_run_context_menu_callback_t* callback)
    {
        var flags = p->get_type_flags(p);
        var media = p->get_media_type(p);
        static Uri? U(string s) => Uri.TryCreate(s, UriKind.Absolute, out var u) ? u : null;
        var target = new WebContextMenuTarget
        {
            Kind = media switch
            {
                cef_context_menu_media_type_t.CM_MEDIATYPE_IMAGE => WebContextMenuTargetKind.Image,
                cef_context_menu_media_type_t.CM_MEDIATYPE_VIDEO => WebContextMenuTargetKind.Video,
                cef_context_menu_media_type_t.CM_MEDIATYPE_AUDIO => WebContextMenuTargetKind.Audio,
                _ => WebContextMenuTargetKind.Page,
            } | ((flags & cef_context_menu_type_flags_t.CM_TYPEFLAG_SELECTION) != 0 ? WebContextMenuTargetKind.SelectedText : 0),
            IsEditable = p->is_editable(p) != 0,
            PageUri = U(CefString.Take(p->get_page_url(p))),
            LinkUri = U(CefString.Take(p->get_link_url(p))),
            SourceUri = U(CefString.Take(p->get_source_url(p))),
            SelectionText = CefString.Take(p->get_selection_text(p)) is { Length: > 0 } sel ? sel : null,
        };
        // CEF's point is in view coordinates (CSS pixels); the contract's in device pixels.
        float scale = ScaleNow();
        int cssX = p->get_xcoord(p), cssY = p->get_ycoord(p);
        int x = (int)MathF.Round(cssX * scale), y = (int)MathF.Round(cssY * scale);
        var items = MenuItems(model);
        // CEF's menu has no "Inspect"; with developer tools on, the page's menu gets one, named
        // as WebView2 names it, which opens the tools at the menu's point.
        if (_options.DevTools)
        {
            if (items.Count > 0) items.Add(WebContextMenuItem.Separator());
            items.Add(new WebContextMenuItem("inspectElement", "Inspect", WebContextMenuItemKind.Command, InspectCommand));
        }
        long owed = Owe(() => OnUi(() => { callback->cancel(callback); CefObject.Release(callback); }));
        var request = new WebContextMenuRequest(x, y, target, items, chosen =>
        {
            if (!Settle(owed)) return;
            OnUi(() =>
            {
                if (chosen is { CommandId: InspectCommand })
                {
                    callback->cancel(callback);
                    OpenDevToolsAt((cssX, cssY));
                }
                else if (chosen is { CommandId: var id and not 0 }) callback->cont(callback, id, 0);
                else callback->cancel(callback);
                CefObject.Release(callback);
            });
        }, _options.DevTools);
        Raise(new WebEngineEvent.ContextMenuRequested(request));
        return true;   // the element draws the menu; CEF keeps none of its own
    }

    // ──── Script dialogs ────

    private void Dialog(WebScriptDialogKind kind, string url, string message, string? defaultText, cef_jsdialog_callback_t* callback)
    {
        Uri? origin = Uri.TryCreate(url, UriKind.Absolute, out var u) && !StringPages.IsStringPage(u) ? OriginOf(u.GetLeftPart(UriPartial.Authority)) : null;
        long owed = Owe(() => OnUi(() =>
        {
            // The page leaves without asking (before-unload) or gets a "no".
            callback->cont(callback, kind == WebScriptDialogKind.BeforeUnload ? 1 : 0, null);
            CefObject.Release(callback);
        }));
        var request = new WebScriptDialogRequest(kind, message, defaultText, origin, (ok, text) =>
        {
            if (!Settle(owed)) return;
            OnUi(() =>
            {
                var t = CefString.Make(text ?? "");
                callback->cont(callback, ok ? 1 : 0, &t);
                CefString.Clear(&t);
                CefObject.Release(callback);
            });
        });
        Raise(new WebEngineEvent.ScriptDialogRequested(request));
    }

    // ──── Downloads ────

    private readonly ConcurrentDictionary<uint, (WebDownload Download, nint Callback)> _activeDownloads = new();
    private readonly ConcurrentDictionary<uint, bool> _cancelWanted = new();

    private void BeforeDownload(cef_download_item_t* item, string suggested, cef_before_download_callback_t* callback)
    {
        uint id = item->get_id(item);
        var url = CefString.Take(item->get_url(item));
        var mime = CefString.Take(item->get_mime_type(item));
        long total = item->get_total_bytes(item);
        WebDownload? download = null;
        download = new WebDownload(() => OnUi(() =>
        {
            _cancelWanted[id] = true;   // CEF may not have given the item's callback yet
            if (_activeDownloads.TryGetValue(id, out var d) && d.Callback != 0)
            {
                var cb = (cef_download_item_callback_t*)d.Callback;
                cb->cancel(cb);
            }
        }));
        long owed = Owe(() => OnUi(() => CefObject.Release(callback)));   // not continued: cancelled
        var request = new WebDownloadRequest(Uri.TryCreate(url, UriKind.Absolute, out var u) ? u : AboutBlank,
            suggested, mime.Length > 0 ? mime : null, total > 0 ? total : null, download, path =>
            {
                if (!Settle(owed)) return;
                OnUi(() =>
                {
                    if (path is not null)
                    {
                        _activeDownloads[id] = (download, 0);
                        var p = CefString.Make(path);
                        callback->cont(callback, &p, 0);
                        CefString.Clear(&p);
                    }
                    CefObject.Release(callback);
                });
            });
        Raise(new WebEngineEvent.DownloadRequested(request));
    }

    private void DownloadUpdated(cef_download_item_t* item, cef_download_item_callback_t* callback)
    {
        uint id = item->get_id(item);
        if (!_activeDownloads.TryGetValue(id, out var d)) return;
        // Keep the newest callback, for cancelling; give back the one before.
        var previous = d.Callback;
        _activeDownloads[id] = (d.Download, (nint)CefObject.AddRef(callback));
        if (previous != 0) CefObject.Release((cef_download_item_callback_t*)previous);
        long received = item->get_received_bytes(item), total = item->get_total_bytes(item);
        WebDownloadState? done = item->is_complete(item) != 0 ? WebDownloadState.Completed
            : item->is_canceled(item) != 0 ? WebDownloadState.Cancelled
            : item->is_interrupted(item) != 0 ? WebDownloadState.Failed
            : null;
        if (done is null && _cancelWanted.ContainsKey(id)) callback->cancel(callback);
        if (done is not null)
        {
            _cancelWanted.TryRemove(id, out _);
            if (_activeDownloads.TryRemove(id, out var gone) && gone.Callback != 0)
                CefObject.Release((cef_download_item_callback_t*)gone.Callback);
        }
        var download = d.Download;
        // WebDownload is updated on the UI thread.
        _host?.Post(() =>
        {
            download.Report(received, total > 0 ? total : null);
            if (done is { } state) download.Finish(state);
        });
    }

    // ──── Permissions ────

    private static readonly (uint Bit, WebPermission Permission)[] PromptKinds =
    [
        ((uint)cef_permission_request_types_t.CEF_PERMISSION_TYPE_GEOLOCATION, WebPermission.Geolocation),
        ((uint)cef_permission_request_types_t.CEF_PERMISSION_TYPE_NOTIFICATIONS, WebPermission.Notifications),
        ((uint)cef_permission_request_types_t.CEF_PERMISSION_TYPE_CLIPBOARD, WebPermission.ClipboardRead),
        ((uint)cef_permission_request_types_t.CEF_PERMISSION_TYPE_MULTIPLE_DOWNLOADS, WebPermission.MultipleDownloads),
        ((uint)cef_permission_request_types_t.CEF_PERMISSION_TYPE_LOCAL_FONTS, WebPermission.LocalFonts),
        ((uint)cef_permission_request_types_t.CEF_PERMISSION_TYPE_MIDI_SYSEX, WebPermission.MidiSystemExclusive),
        ((uint)cef_permission_request_types_t.CEF_PERMISSION_TYPE_WINDOW_MANAGEMENT, WebPermission.WindowManagement),
        ((uint)cef_permission_request_types_t.CEF_PERMISSION_TYPE_FILE_SYSTEM_ACCESS, WebPermission.FileReadWrite),
        ((uint)cef_permission_request_types_t.CEF_PERMISSION_TYPE_CAMERA_STREAM, WebPermission.Camera),
        ((uint)cef_permission_request_types_t.CEF_PERMISSION_TYPE_MIC_STREAM, WebPermission.Microphone),
    ];

    private static readonly (uint Bit, WebPermission Permission)[] MediaKinds =
    [
        ((uint)cef_media_access_permission_types_t.CEF_MEDIA_PERMISSION_DEVICE_AUDIO_CAPTURE, WebPermission.Microphone),
        ((uint)cef_media_access_permission_types_t.CEF_MEDIA_PERMISSION_DEVICE_VIDEO_CAPTURE, WebPermission.Camera),
    ];

    /// <summary>
    /// One request per permission asked; CEF hears back once every one is answered, with the
    /// set allowed. A bit the contract has no name for asks as <see cref="WebPermission.Unknown"/>.
    /// </summary>
    private void AskPermissions(uint asked, (uint Bit, WebPermission Permission)[] kinds, string origin, Action<uint> answer)
    {
        var parts = new List<(uint Bit, WebPermission Permission)>();
        uint named = 0;
        foreach (var (bit, permission) in kinds)
            if ((asked & bit) != 0) { parts.Add((bit, permission)); named |= bit; }
        for (int i = 0; i < 32; i++)
            if ((asked & ~named & (1u << i)) != 0) parts.Add((1u << i, WebPermission.Unknown));
        if (parts.Count == 0) { answer(0); return; }

        int left = parts.Count;
        uint allowed = 0;
        long owed = Owe(() => answer(0));
        Uri? from = OriginOf(origin);
        foreach (var (bit, permission) in parts)
        {
            Raise(new WebEngineEvent.PermissionRequested(new WebPermissionRequest(permission, from, isUserInitiated: false, ok =>
            {
                if (ok) Interlocked.Or(ref allowed, bit);
                if (Interlocked.Decrement(ref left) == 0 && Settle(owed)) answer(allowed);
            })));
        }
    }

    // ──── Callbacks ────

    [UnmanagedCallersOnly]
    private static cef_request_handler_t* GetRequestHandler(cef_client_t* s)
    {
        try { return Of(s) is { } v ? CefObject.Give(v._request) : null; }
        catch (Exception ex) { CefLog.Error("get_request_handler", ex); return null; }
    }

    [UnmanagedCallersOnly]
    private static cef_context_menu_handler_t* GetContextMenuHandler(cef_client_t* s)
    {
        try { return Of(s) is { } v ? CefObject.Give(v._menu) : null; }
        catch (Exception ex) { CefLog.Error("get_context_menu_handler", ex); return null; }
    }

    [UnmanagedCallersOnly]
    private static cef_jsdialog_handler_t* GetJsDialogHandler(cef_client_t* s)
    {
        try { return Of(s) is { } v ? CefObject.Give(v._dialogs) : null; }
        catch (Exception ex) { CefLog.Error("get_jsdialog_handler", ex); return null; }
    }

    [UnmanagedCallersOnly]
    private static cef_download_handler_t* GetDownloadHandler(cef_client_t* s)
    {
        try { return Of(s) is { } v ? CefObject.Give(v._downloads) : null; }
        catch (Exception ex) { CefLog.Error("get_download_handler", ex); return null; }
    }

    [UnmanagedCallersOnly]
    private static cef_permission_handler_t* GetPermissionHandler(cef_client_t* s)
    {
        try { return Of(s) is { } v ? CefObject.Give(v._permissions) : null; }
        catch (Exception ex) { CefLog.Error("get_permission_handler", ex); return null; }
    }

    [UnmanagedCallersOnly]
    private static int OnBeforeBrowse(cef_request_handler_t* s, cef_browser_t* b, cef_frame_t* f, cef_request_t* request,
        int userGesture, int isRedirect)
    {
        try
        {
            var url = CefString.Take(request->get_url(request));
            return Of(s)?.Browse(f, url, userGesture != 0, isRedirect != 0) == true ? 1 : 0;
        }
        catch (Exception ex) { CefLog.Error("on_before_browse", ex); return 0; }
        finally
        {
            CefObject.Release(b);
            CefObject.Release(f);
            CefObject.Release(request);
        }
    }

    [UnmanagedCallersOnly]
    private static void OnRenderProcessTerminated(cef_request_handler_t* s, cef_browser_t* b, cef_termination_status_t status,
        int errorCode, cef_string_utf16_t* errorText)
    {
        try
        {
            var message = status switch
            {
                cef_termination_status_t.TS_PROCESS_WAS_KILLED => "The page's process was killed.",
                cef_termination_status_t.TS_PROCESS_CRASHED => "The page's process crashed.",
                cef_termination_status_t.TS_PROCESS_OOM => "The page's process ran out of memory.",
                cef_termination_status_t.TS_LAUNCH_FAILED => "The page's process could not be started.",
                _ => "The page's process exited.",
            };
            Of(s)?.Raise(new WebEngineEvent.ProcessFailed(message, CanReload: true));
        }
        catch (Exception ex) { CefLog.Error("on_render_process_terminated", ex); }
        finally { CefObject.Release(b); }
    }

    [UnmanagedCallersOnly]
    private static void OnLoadEnd(cef_load_handler_t* s, cef_browser_t* b, cef_frame_t* f, int httpStatus)
    {
        try { Of(s)?.LoadEnded(f, httpStatus); }
        catch (Exception ex) { CefLog.Error("on_load_end", ex); }
        finally
        {
            CefObject.Release(b);
            CefObject.Release(f);
        }
    }

    [UnmanagedCallersOnly]
    private static void OnLoadError(cef_load_handler_t* s, cef_browser_t* b, cef_frame_t* f, cef_errorcode_t code,
        cef_string_utf16_t* text, cef_string_utf16_t* url)
    {
        try { Of(s)?.LoadFailed(f, code, CefString.Read(url)); }
        catch (Exception ex) { CefLog.Error("on_load_error", ex); }
        finally
        {
            CefObject.Release(b);
            CefObject.Release(f);
        }
    }

    [UnmanagedCallersOnly]
    private static int RunContextMenu(cef_context_menu_handler_t* s, cef_browser_t* b, cef_frame_t* f,
        cef_context_menu_params_t* p, cef_menu_model_t* model, cef_run_context_menu_callback_t* callback)
    {
        try
        {
            if (Of(s) is { } v && v.RunMenu(p, model, callback)) return 1;   // the callback is kept, with its reference
            CefObject.Release(callback);
            return 0;
        }
        catch (Exception ex)
        {
            CefLog.Error("run_context_menu", ex);
            return 0;   // CEF cancels the menu, and the callback's reference is CEF's to lose
        }
        finally
        {
            CefObject.Release(b);
            CefObject.Release(f);
            CefObject.Release(p);
            CefObject.Release(model);
        }
    }

    [UnmanagedCallersOnly]
    private static int OnJsDialog(cef_jsdialog_handler_t* s, cef_browser_t* b, cef_string_utf16_t* originUrl,
        cef_jsdialog_type_t type, cef_string_utf16_t* message, cef_string_utf16_t* defaultPrompt,
        cef_jsdialog_callback_t* callback, int* suppressMessage)
    {
        try
        {
            *suppressMessage = 0;
            if (Of(s) is not { } v) { CefObject.Release(callback); return 0; }
            var kind = type switch
            {
                cef_jsdialog_type_t.JSDIALOGTYPE_CONFIRM => WebScriptDialogKind.Confirm,
                cef_jsdialog_type_t.JSDIALOGTYPE_PROMPT => WebScriptDialogKind.Prompt,
                _ => WebScriptDialogKind.Alert,
            };
            v.Dialog(kind, CefString.Read(originUrl), CefString.Read(message),
                kind == WebScriptDialogKind.Prompt ? CefString.Read(defaultPrompt) : null, callback);
            return 1;
        }
        catch (Exception ex) { CefLog.Error("on_jsdialog", ex); return 0; }
        finally { CefObject.Release(b); }
    }

    [UnmanagedCallersOnly]
    private static int OnBeforeUnloadDialog(cef_jsdialog_handler_t* s, cef_browser_t* b, cef_string_utf16_t* message,
        int isReload, cef_jsdialog_callback_t* callback)
    {
        try
        {
            if (Of(s) is not { } v) { CefObject.Release(callback); return 0; }
            var frame = v._browser != null ? v._browser->get_main_frame(v._browser) : null;
            var url = frame != null ? UrlOfAndRelease(frame) : "";
            v.Dialog(WebScriptDialogKind.BeforeUnload, url, "", null, callback);
            return 1;
        }
        catch (Exception ex) { CefLog.Error("on_before_unload_dialog", ex); return 0; }
        finally { CefObject.Release(b); }
    }

    private static string UrlOfAndRelease(cef_frame_t* frame)
    {
        try { return CefString.Take(frame->get_url(frame)); }
        finally { CefObject.Release(frame); }
    }

    [UnmanagedCallersOnly]
    private static int CanDownload(cef_download_handler_t* s, cef_browser_t* b, cef_string_utf16_t* url,
        cef_string_utf16_t* method)
    {
        try { return 1; }   // asked, and decided, in on_before_download
        catch (Exception ex) { CefLog.Error("can_download", ex); return 0; }
        finally { CefObject.Release(b); }
    }

    [UnmanagedCallersOnly]
    private static int OnBeforeDownload(cef_download_handler_t* s, cef_browser_t* b, cef_download_item_t* item,
        cef_string_utf16_t* suggestedName, cef_before_download_callback_t* callback)
    {
        try
        {
            if (Of(s) is not { } v) { CefObject.Release(callback); return 0; }
            v.BeforeDownload(item, CefString.Read(suggestedName), callback);
            return 1;
        }
        catch (Exception ex) { CefLog.Error("on_before_download", ex); return 0; }
        finally
        {
            CefObject.Release(b);
            CefObject.Release(item);
        }
    }

    [UnmanagedCallersOnly]
    private static void OnDownloadUpdated(cef_download_handler_t* s, cef_browser_t* b, cef_download_item_t* item,
        cef_download_item_callback_t* callback)
    {
        try { Of(s)?.DownloadUpdated(item, callback); }
        catch (Exception ex) { CefLog.Error("on_download_updated", ex); }
        finally
        {
            CefObject.Release(b);
            CefObject.Release(item);
            CefObject.Release(callback);
        }
    }

    [UnmanagedCallersOnly]
    private static int OnMediaAccess(cef_permission_handler_t* s, cef_browser_t* b, cef_frame_t* f,
        cef_string_utf16_t* origin, uint requested, cef_media_access_callback_t* callback)
    {
        try
        {
            if (Of(s) is not { } v) { CefObject.Release(callback); return 0; }
            v.AskPermissions(requested, MediaKinds, CefString.Read(origin), allowed => v.OnUi(() =>
            {
                if (allowed == requested) callback->cont(callback, allowed);
                else callback->cancel(callback);   // CEF wants all asked, or none
                CefObject.Release(callback);
            }));
            return 1;
        }
        catch (Exception ex) { CefLog.Error("on_request_media_access_permission", ex); return 0; }
        finally
        {
            CefObject.Release(b);
            CefObject.Release(f);
        }
    }

    [UnmanagedCallersOnly]
    private static int OnPermissionPrompt(cef_permission_handler_t* s, cef_browser_t* b, ulong promptId,
        cef_string_utf16_t* origin, uint requested, cef_permission_prompt_callback_t* callback)
    {
        try
        {
            if (Of(s) is not { } v) { CefObject.Release(callback); return 0; }
            v.AskPermissions(requested, PromptKinds, CefString.Read(origin), allowed => v.OnUi(() =>
            {
                callback->cont(callback, allowed == requested
                    ? cef_permission_request_result_t.CEF_PERMISSION_RESULT_ACCEPT
                    : cef_permission_request_result_t.CEF_PERMISSION_RESULT_DENY);
                CefObject.Release(callback);
            }));
            return 1;
        }
        catch (Exception ex) { CefLog.Error("on_show_permission_prompt", ex); return 0; }
        finally { CefObject.Release(b); }
    }
}
