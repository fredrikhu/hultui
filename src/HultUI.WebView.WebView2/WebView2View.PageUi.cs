using HultUI.Core.Events;
using HultUI.WebView.WebView2.Interop;

namespace HultUI.WebView.WebView2;

/// <summary>
/// The page's context menu, its dialogs and its downloads, taken from WebView2 and handed to
/// the controller as requests with a continuation, so HultUI draws them and the application
/// decides. WebView2's own menu, dialogs and download flyout never show.
/// </summary>
/// <remarks>
/// Each is answered later than the event that raised it — after a menu has closed, a dialog
/// been clicked, a save location chosen — so each takes WebView2's deferral and a reference
/// on its event arguments, and gives both back when the request completes. Completion comes
/// on the UI thread, where WebView2's objects live.
/// </remarks>
internal sealed unsafe partial class WebView2View
{
    private const int DownloadUserCancelled = 26; // COREWEBVIEW2_DOWNLOAD_INTERRUPT_REASON_USER_CANCELED
    private nint _webview4, _webview11, _composition3;
    private nint _dragData; // a data object we made for a drag that came without one
    private long _dragEntered, _dragForwarded;
    private bool _dragAccepted;

    /// <summary>
    /// How often a hover is passed on. OLE calls DragOver on every mouse event — hundreds a
    /// second from a fast mouse — and each call WebView2 takes while it is still telling the
    /// page the drag has entered holds that up: measured from DragEnter to the page's
    /// acceptance, 4 s with every call passed on, 0.5 s one per 50 ms (what a quick flick into
    /// a drop zone felt like), 0.25 s with a quiet 150 ms and then one per 25 ms. Edge, whose
    /// drop target is in-process, has no such cost. Between calls the last answer stands.
    /// </summary>
    private const int DragQuietMs = 150, DragEveryMs = 25;
    private readonly List<ActiveDownload> _downloads = [];

    private void SubscribePageUi()
    {
        On(_webview, ICoreWebView2.add_ScriptDialogOpening, ICoreWebView2.remove_ScriptDialogOpening,
            ICoreWebView2ScriptDialogOpeningEventHandler.IID, OnScriptDialogOpening, "add_ScriptDialogOpening");
        On(_webview, ICoreWebView2.add_PermissionRequested, ICoreWebView2.remove_PermissionRequested,
            ICoreWebView2PermissionRequestedEventHandler.IID, OnPermissionRequested, "add_PermissionRequested");

        // Both interfaces are years old in the Evergreen runtime; without them the page keeps
        // WebView2's own menu and download flyout rather than failing.
        _webview11 = Com.TryQueryInterface(_webview, ICoreWebView2_11.IID);
        if (_webview11 != 0)
            On(_webview11, ICoreWebView2_11.add_ContextMenuRequested, ICoreWebView2_11.remove_ContextMenuRequested,
                ICoreWebView2ContextMenuRequestedEventHandler.IID, OnContextMenuRequested, "add_ContextMenuRequested");
        _composition3 = Com.TryQueryInterface(_composition, ICoreWebView2CompositionController3.IID);
        _webview4 = Com.TryQueryInterface(_webview, ICoreWebView2_4.IID);
        if (_webview4 != 0)
            On(_webview4, ICoreWebView2_4.add_DownloadStarting, ICoreWebView2_4.remove_DownloadStarting,
                ICoreWebView2DownloadStartingEventHandler.IID, OnDownloadStarting, "add_DownloadStarting");
    }

    private void ReleasePageUi()
    {
        foreach (var download in _downloads.ToArray()) download.Close();
        Com.Release(ref _dragData);
        Com.Release(ref _composition3);
        Com.Release(ref _webview11);
        Com.Release(ref _webview4);
    }

    /// <summary>Holds an event's arguments and deferral until its request completes, then
    /// completes the deferral and lets both go.</summary>
    private static (nint Args, nint Deferral) Hold(nint args, int getDeferralSlot)
    {
        Com.AddRef(args);
        return (args, Com.GetPtr(args, getDeferralSlot, "GetDeferral"));
    }

    private static void Complete(ref (nint Args, nint Deferral) held)
    {
        if (held.Deferral != 0) Com.Call(held.Deferral, ICoreWebView2Deferral.Complete);
        Com.Release(ref held.Deferral);
        Com.Release(ref held.Args);
    }

    // ──── Drags over the page ────

    private const uint MK_LBUTTON = 0x1, MK_SHIFT = 0x4, MK_CONTROL = 0x8, MK_ALT = 0x20;
    private const uint DROPEFFECT_COPY = 1, DROPEFFECT_MOVE = 2, DROPEFFECT_LINK = 4;

    /// <summary>
    /// A drag over the page, through the drop-target half of the composition controller — the
    /// host is the page's drop target in visual hosting, for files from outside and for drags
    /// the page started itself alike. The point is bounds-relative device pixels, as for
    /// <c>SendMouseInput</c>.
    /// </summary>
    public bool SendDrag(in WebDragInput input)
    {
        if (_phase != Phase.Ready || _composition3 == 0) return false;
        var point = new Native.POINT { X = input.X, Y = input.Y };
        long now = Environment.TickCount64;
        if (input.Action == WebDragAction.Enter)
        {
            _dragEntered = now;
            _dragAccepted = false;
        }
        else if (input.Action == WebDragAction.Over
                 && (now - _dragEntered < DragQuietMs || now - _dragForwarded < DragEveryMs))
            return _dragAccepted;
        if (input.Action == WebDragAction.Over) _dragForwarded = now;
        // During an OLE drag the left button is down, and up by the drop; the modifiers pick
        // the effect.
        uint keys = (input.Action == WebDragAction.Drop ? 0 : MK_LBUTTON)
            | ((input.Modifiers & KeyModifiers.Shift) != 0 ? MK_SHIFT : 0)
            | ((input.Modifiers & KeyModifiers.Ctrl) != 0 ? MK_CONTROL : 0)
            | ((input.Modifiers & KeyModifiers.Alt) != 0 ? MK_ALT : 0);
        uint effect = DROPEFFECT_COPY | DROPEFFECT_MOVE | DROPEFFECT_LINK;
        var vtable = *(nint**)_composition3;
        int hr;
        switch (input.Action)
        {
            case WebDragAction.Enter:
                hr = ((delegate* unmanaged[Stdcall]<nint, nint, uint, Native.POINT, uint*, int>)vtable[ICoreWebView2CompositionController3.DragEnter])(
                    _composition3, DataFor(input), keys, point, &effect);
                break;
            case WebDragAction.Over:
                hr = ((delegate* unmanaged[Stdcall]<nint, uint, Native.POINT, uint*, int>)vtable[ICoreWebView2CompositionController3.DragOver])(
                    _composition3, keys, point, &effect);
                _dragAccepted = hr >= 0 && effect != 0;
                break;
            case WebDragAction.Drop:
                hr = ((delegate* unmanaged[Stdcall]<nint, nint, uint, Native.POINT, uint*, int>)vtable[ICoreWebView2CompositionController3.Drop])(
                    _composition3, DataFor(input), keys, point, &effect);
                Com.Release(ref _dragData);
                break;
            default:
                ((delegate* unmanaged[Stdcall]<nint, int>)vtable[ICoreWebView2CompositionController3.DragLeave])(_composition3);
                Com.Release(ref _dragData);
                return false;
        }
        return hr >= 0 && effect != 0;
    }

    /// <summary>The drag's own data object, or one made from its files and text when the
    /// platform gave none.</summary>
    private nint DataFor(in WebDragInput input)
    {
        if (input.NativeData != 0) return input.NativeData;
        if (_dragData == 0) _dragData = DropData.Create(input.Files, input.Text);
        return _dragData;
    }

    // ──── Context menu ────

    private void OnContextMenuRequested(nint sender, nint args)
    {
        // Ours to draw: WebView2's own menu does not open.
        Com.Call(args, ICoreWebView2ContextMenuRequestedEventArgs.put_Handled, 1);

        var collection = Com.GetPtr(args, ICoreWebView2ContextMenuRequestedEventArgs.get_MenuItems, "get_MenuItems");
        List<WebContextMenuItem> items;
        try { items = MenuItems(collection); }
        finally { Com.Release(ref collection); }
        var target = MenuTarget(args);
        // In the page's CSS pixels (measured: a click at (150, 52) device pixels at scale 1.5
        // reports (100, 35)); the contract's are device pixels.
        var at = Com.Get<Native.POINT>(args, ICoreWebView2ContextMenuRequestedEventArgs.get_Location, "get_Location");
        float scale = _applied?.Scale ?? 1f;
        at = new Native.POINT { X = (int)MathF.Round(at.X * scale), Y = (int)MathF.Round(at.Y * scale) };

        var held = Hold(args, ICoreWebView2ContextMenuRequestedEventArgs.GetDeferral);
        Raise(new WebEngineEvent.ContextMenuRequested(new WebContextMenuRequest(at.X, at.Y, target, items, chosen =>
        {
            if (chosen is not null && _phase != Phase.Disposed)
                Com.Call(held.Args, ICoreWebView2ContextMenuRequestedEventArgs.put_SelectedCommandId, chosen.CommandId);
            Complete(ref held);
        }, _options.DevTools)));
    }

    private static List<WebContextMenuItem> MenuItems(nint collection)
    {
        uint count = Com.Get<uint>(collection, ICoreWebView2ContextMenuItemCollection.get_Count, "get_Count");
        var items = new List<WebContextMenuItem>((int)count);
        for (uint i = 0; i < count; i++)
        {
            var item = Com.GetPtr(collection, ICoreWebView2ContextMenuItemCollection.GetValueAtIndex, (nint)i, "GetValueAtIndex");
            try
            {
                var kind = (WebContextMenuItemKind)Com.Get<int>(item, ICoreWebView2ContextMenuItem.get_Kind, "get_Kind");
                var children = Array.Empty<WebContextMenuItem>() as IReadOnlyList<WebContextMenuItem>;
                if (kind == WebContextMenuItemKind.Submenu)
                {
                    var inner = Com.GetPtr(item, ICoreWebView2ContextMenuItem.get_Children, "get_Children");
                    try { children = MenuItems(inner); }
                    finally { Com.Release(ref inner); }
                }
                string? shortcut = Com.GetString(item, ICoreWebView2ContextMenuItem.get_ShortcutKeyDescription, "get_ShortcutKeyDescription");
                items.Add(new WebContextMenuItem(
                    Com.GetString(item, ICoreWebView2ContextMenuItem.get_Name, "get_Name") ?? "",
                    WithoutMnemonics(Com.GetString(item, ICoreWebView2ContextMenuItem.get_Label, "get_Label") ?? ""),
                    kind,
                    Com.Get<int>(item, ICoreWebView2ContextMenuItem.get_CommandId, "get_CommandId"))
                {
                    ShortcutHint = string.IsNullOrEmpty(shortcut) ? null : shortcut,
                    IsEnabled = Com.GetBool(item, ICoreWebView2ContextMenuItem.get_IsEnabled, "get_IsEnabled"),
                    IsChecked = Com.GetBool(item, ICoreWebView2ContextMenuItem.get_IsChecked, "get_IsChecked"),
                    Children = children,
                });
            }
            finally { Com.Release(ref item); }
        }
        return items;
    }

    /// <summary>"&amp;Copy" → "Copy", "Save &amp;&amp; exit" → "Save &amp; exit": Win32 menu mnemonics,
    /// which a HultUI menu does not draw.</summary>
    internal static string WithoutMnemonics(string label)
    {
        if (!label.Contains('&')) return label;
        var text = new System.Text.StringBuilder(label.Length);
        for (int i = 0; i < label.Length; i++)
        {
            if (label[i] == '&')
            {
                if (i + 1 < label.Length && label[i + 1] == '&') { text.Append('&'); i++; }
                continue;
            }
            text.Append(label[i]);
        }
        return text.ToString();
    }

    private static WebContextMenuTarget MenuTarget(nint args)
    {
        var target = Com.GetPtr(args, ICoreWebView2ContextMenuRequestedEventArgs.get_ContextMenuTarget, "get_ContextMenuTarget");
        try
        {
            string? Text(int has, int get) => Com.GetBool(target, has, "has") ? Com.GetString(target, get, "get") : null;
            Uri? Address(int has, int get) => Uri.TryCreate(Text(has, get), UriKind.Absolute, out var uri) ? uri : null;
            return new WebContextMenuTarget
            {
                Kind = Com.Get<int>(target, ICoreWebView2ContextMenuTarget.get_Kind, "get_Kind") switch
                {
                    1 => WebContextMenuTargetKind.Image,
                    2 => WebContextMenuTargetKind.SelectedText,
                    3 => WebContextMenuTargetKind.Audio,
                    4 => WebContextMenuTargetKind.Video,
                    _ => WebContextMenuTargetKind.Page,
                },
                IsEditable = Com.GetBool(target, ICoreWebView2ContextMenuTarget.get_IsEditable, "get_IsEditable"),
                PageUri = Uri.TryCreate(Com.GetString(target, ICoreWebView2ContextMenuTarget.get_PageUri, "get_PageUri"), UriKind.Absolute, out var page) ? page : null,
                LinkUri = Address(ICoreWebView2ContextMenuTarget.get_HasLinkUri, ICoreWebView2ContextMenuTarget.get_LinkUri),
                LinkText = Text(ICoreWebView2ContextMenuTarget.get_HasLinkText, ICoreWebView2ContextMenuTarget.get_LinkText),
                SourceUri = Address(ICoreWebView2ContextMenuTarget.get_HasSourceUri, ICoreWebView2ContextMenuTarget.get_SourceUri),
                SelectionText = Text(ICoreWebView2ContextMenuTarget.get_HasSelection, ICoreWebView2ContextMenuTarget.get_SelectionText),
            };
        }
        finally { Com.Release(ref target); }
    }

    // ──── The page's dialogs ────

    private void OnScriptDialogOpening(nint sender, nint args)
    {
        var kind = (WebScriptDialogKind)Com.Get<int>(args, ICoreWebView2ScriptDialogOpeningEventArgs.get_Kind, "get_Kind");
        string message = Com.GetString(args, ICoreWebView2ScriptDialogOpeningEventArgs.get_Message, "get_Message") ?? "";
        string? defaultText = Com.GetString(args, ICoreWebView2ScriptDialogOpeningEventArgs.get_DefaultText, "get_DefaultText");
        var origin = OriginOf(Com.GetString(args, ICoreWebView2ScriptDialogOpeningEventArgs.get_Uri, "get_Uri"));

        var held = Hold(args, ICoreWebView2ScriptDialogOpeningEventArgs.GetDeferral);
        Raise(new WebEngineEvent.ScriptDialogRequested(new WebScriptDialogRequest(kind, message, defaultText, origin, (accepted, text) =>
        {
            if (accepted && _phase != Phase.Disposed)
            {
                if (text is not null) Com.CallString(held.Args, ICoreWebView2ScriptDialogOpeningEventArgs.put_ResultText, text);
                Com.Call(held.Args, ICoreWebView2ScriptDialogOpeningEventArgs.Accept);
            }
            Complete(ref held);
        })));
    }

    // ──── Permissions ────

    private void OnPermissionRequested(nint sender, nint args)
    {
        int kind = Com.Get<int>(args, ICoreWebView2PermissionRequestedEventArgs.get_PermissionKind, "get_PermissionKind");
        var permission = kind is >= 0 and <= (int)WebPermission.PersistentStorage ? (WebPermission)kind : WebPermission.Unknown;
        var origin = OriginOf(Com.GetString(args, ICoreWebView2PermissionRequestedEventArgs.get_Uri, "get_Uri"));
        bool user = Com.GetBool(args, ICoreWebView2PermissionRequestedEventArgs.get_IsUserInitiated, "get_IsUserInitiated");

        // Answered with an explicit allow or deny, never left at "default": that is what
        // would bring up WebView2's own prompt.
        // Not remembered in the profile: WebView2 would otherwise keep the first answer — the
        // default denial, most likely — and never ask the application again.
        var args3 = Com.TryQueryInterface(args, ICoreWebView2PermissionRequestedEventArgs3.IID);
        if (args3 != 0)
        {
            Com.Call(args3, ICoreWebView2PermissionRequestedEventArgs3.put_SavesInProfile, 0);
            Com.Release(ref args3);
        }

        var held = Hold(args, ICoreWebView2PermissionRequestedEventArgs.GetDeferral);
        Raise(new WebEngineEvent.PermissionRequested(new WebPermissionRequest(permission, origin, user, allowed =>
        {
            if (_phase != Phase.Disposed)
                Com.Call(held.Args, ICoreWebView2PermissionRequestedEventArgs.put_State, allowed ? 1 : 2); // ALLOW : DENY
            Complete(ref held);
        })));
    }

    // ──── Downloads ────

    private void OnDownloadStarting(nint sender, nint args)
    {
        // No flyout: where the file goes, and whether it goes at all, is the application's.
        Com.Call(args, ICoreWebView2DownloadStartingEventArgs.put_Handled, 1);

        var operation = Com.GetPtr(args, ICoreWebView2DownloadStartingEventArgs.get_DownloadOperation, "get_DownloadOperation");
        var address = Com.GetString(operation, ICoreWebView2DownloadOperation.get_Uri, "get_Uri");
        string? mime = Com.GetString(operation, ICoreWebView2DownloadOperation.get_MimeType, "get_MimeType");
        long total = Com.Get<long>(operation, ICoreWebView2DownloadOperation.get_TotalBytesToReceive, "get_TotalBytesToReceive");
        // WebView2 has picked a path in the Downloads folder already; its name is the suggestion.
        string suggested = Path.GetFileName(Com.GetString(args, ICoreWebView2DownloadStartingEventArgs.get_ResultFilePath, "get_ResultFilePath") ?? "");

        var active = new ActiveDownload(this, operation);
        var held = Hold(args, ICoreWebView2DownloadStartingEventArgs.GetDeferral);
        var request = new WebDownloadRequest(
            Uri.TryCreate(address, UriKind.Absolute, out var uri) ? uri : new Uri("about:blank"),
            suggested, string.IsNullOrEmpty(mime) ? null : mime, total >= 0 ? total : null, active.Download, path =>
            {
                if (_phase == Phase.Disposed || path is null)
                {
                    Com.Call(held.Args, ICoreWebView2DownloadStartingEventArgs.put_Cancel, 1);
                    active.Close();
                }
                else
                {
                    Com.CallString(held.Args, ICoreWebView2DownloadStartingEventArgs.put_ResultFilePath, path);
                    active.Watch();
                }
                Complete(ref held);
            });
        Raise(new WebEngineEvent.DownloadRequested(request));
    }

    /// <summary>A download WebView2 is running for us: its operation, and the two events that
    /// report on it, held until it ends.</summary>
    private sealed class ActiveDownload
    {
        private readonly WebView2View _view;
        private nint _operation;
        private ComHandler? _bytes, _state;
        private long _bytesToken, _stateToken;

        public ActiveDownload(WebView2View view, nint operation)
        {
            _view = view;
            _operation = operation;
            Download = new WebDownload(() =>
            {
                if (_operation != 0) Com.Call(_operation, ICoreWebView2DownloadOperation.Cancel);
            });
        }

        public WebDownload Download { get; }

        public void Watch()
        {
            _view._downloads.Add(this);
            _bytes = ComHandler.ForEvent(ICoreWebView2BytesReceivedChangedEventHandler.IID, (_, _) => Report());
            _bytesToken = Com.AddHandler(_operation, ICoreWebView2DownloadOperation.add_BytesReceivedChanged, _bytes.Pointer, "add_BytesReceivedChanged");
            _state = ComHandler.ForEvent(ICoreWebView2StateChangedEventHandler.IID, (_, _) => OnState());
            _stateToken = Com.AddHandler(_operation, ICoreWebView2DownloadOperation.add_StateChanged, _state.Pointer, "add_StateChanged");
        }

        private void Report()
        {
            if (_operation == 0) return;
            long total = Com.Get<long>(_operation, ICoreWebView2DownloadOperation.get_TotalBytesToReceive, "get_TotalBytesToReceive");
            Download.Report(Com.Get<long>(_operation, ICoreWebView2DownloadOperation.get_BytesReceived, "get_BytesReceived"),
                total >= 0 ? total : null);
        }

        private void OnState()
        {
            if (_operation == 0) return;
            switch (Com.Get<int>(_operation, ICoreWebView2DownloadOperation.get_State, "get_State"))
            {
                case 2: // COMPLETED
                    Report();
                    Download.Finish(WebDownloadState.Completed);
                    Close();
                    break;
                case 1: // INTERRUPTED
                    int reason = Com.Get<int>(_operation, ICoreWebView2DownloadOperation.get_InterruptReason, "get_InterruptReason");
                    Download.Finish(reason == DownloadUserCancelled ? WebDownloadState.Cancelled : WebDownloadState.Failed);
                    Close();
                    break;
            }
        }

        public void Close()
        {
            if (_operation == 0) return;
            _view._downloads.Remove(this);
            if (_bytes is not null) Com.RemoveHandler(_operation, ICoreWebView2DownloadOperation.remove_BytesReceivedChanged, _bytesToken);
            if (_state is not null) Com.RemoveHandler(_operation, ICoreWebView2DownloadOperation.remove_StateChanged, _stateToken);
            _bytes?.Release();
            _state?.Release();
            _bytes = _state = null;
            Com.Release(ref _operation);
        }
    }
}
