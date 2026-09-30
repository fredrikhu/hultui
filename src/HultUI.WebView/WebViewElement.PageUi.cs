using HultUI.Core.Builders;
using HultUI.Core.Dialogs;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;

namespace HultUI.WebView;

// Inside an element, Box and Flex name its own layout members, and Text is the HultUI.Text
// namespace; the builders are reached by these names instead.
using BoxBuilder = HultUI.Core.Builders.Box;
using FlexBuilder = HultUI.Core.Builders.Flex;
using TextBuilder = HultUI.Core.Builders.Text;

/// <summary>
/// The page's own menus and dialogs, drawn by HultUI rather than the engine: a context menu
/// and <c>alert</c>/<c>confirm</c>/<c>prompt</c> look like the rest of the application, sit
/// in its overlay layer, take its keyboard and are in its accessibility tree.
/// </summary>
public partial class WebViewElement
{
    // ──── Context menu ────

    private void ShowPageMenu(WebContextMenuRequest request)
    {
        if (!IsHosted || OverlayHost is not Window window) { request.Dismiss(); return; }

        float scale = window.DisplayScale;
        var box = window.ContentBoxInWindow(this);
        var at = new Point(box.X + request.X / scale, box.Y + request.Y / scale);

        // While the menu is open the keyboard is the menu's: arrows, Enter and Escape must
        // reach HultUI, not the page. It goes back to the page when the menu closes.
        bool pageHadKeyboard = IsFocused;
        if (pageHadKeyboard) _controller?.Blur();

        window.OpenContextMenu(MenuItems(request.Items, request), at, onClosed: () =>
        {
            request.Dismiss(); // nothing chosen, unless an item already answered
            if (pageHadKeyboard && IsFocused && IsHosted) _controller?.Focus(FocusReason.Programmatic);
        });
    }

    private static List<ContextMenuItem> MenuItems(IReadOnlyList<WebContextMenuItem> items, WebContextMenuRequest request)
    {
        var menu = new List<ContextMenuItem>(items.Count);
        foreach (var item in items)
        {
            if (item.Kind == WebContextMenuItemKind.Separator)
            {
                menu.Add(ContextMenuItem.Separator());
                continue;
            }
            menu.Add(new ContextMenuItem
            {
                Label = item.Label,
                ShortcutHint = item.ShortcutHint,
                IsDisabled = !item.IsEnabled,
                IsChecked = item.Kind is WebContextMenuItemKind.CheckBox or WebContextMenuItemKind.Radio ? item.IsChecked : null,
                Children = item.Kind == WebContextMenuItemKind.Submenu ? MenuItems(item.Children, request) : null,
                Action = item.Kind == WebContextMenuItemKind.Submenu ? null : () => request.Select(item),
            });
        }
        return menu;
    }

    // ──── The page's dialogs ────

    private void ShowScriptDialog(WebScriptDialogRequest request)
    {
        if (!IsHosted || OverlayHost is not { } host) { request.Cancel(); return; }

        // The dialog takes the keyboard, so its Enter and Escape and a prompt's typing are
        // HultUI's; the page gets it back after, if it had it.
        bool pageHadKeyboard = IsFocused;
        if (pageHadKeyboard) _controller?.Blur();

        var focus = FocusManager;
        var shown = Dialog.Show<bool>(host, close => ScriptDialogPanel(host, request, close, focus),
            new DialogOptions { DismissOnScrimClick = false });
        shown.ContinueWith(_ =>
        {
            // Closed by the panel's own buttons, which answered; or by the element going
            // away, which did not — the page's script must not wait for ever.
            request.Cancel();
            if (pageHadKeyboard && IsHosted) Focus();
        }, TaskContinuationOptions.ExecuteSynchronously);
    }

    private static Element ScriptDialogPanel(IOverlayHost host, WebScriptDialogRequest request, IDialogController<bool> close,
        FocusManager? focus)
    {
        var theme = host.ActiveTheme ?? Theme.Light;
        // Whose page is asking, so a page cannot pass its dialog off as the application's.
        string title = request.Kind == WebScriptDialogKind.BeforeUnload
            ? "Leave this page?"
            : request.Origin is { Host.Length: > 0 } origin ? $"{origin.Host} says" : "This page says";
        string message = request.Kind == WebScriptDialogKind.BeforeUnload && request.Message.Length == 0
            ? "Changes you made may not be saved."
            : request.Message;

        TextInputElement? input = request.Kind == WebScriptDialogKind.Prompt ? TextInput.New(request.DefaultText ?? "") : null;
        void Accept() { request.Accept(input?.Text); close.Close(true); }
        void Cancel() { request.Cancel(); close.Close(false); }

        (string ok, string? cancel) = request.Kind switch
        {
            WebScriptDialogKind.Alert => ("OK", null),
            WebScriptDialogKind.BeforeUnload => ("Leave", "Stay"),
            _ => ("OK", "Cancel"),
        };
        var actions = FlexBuilder.Row(gap: 8).JustifyContent(JustifyContent.FlexEnd).AlignItems(AlignItems.Center);
        var okButton = DialogButton(ok, ButtonVariant.Primary, Accept);
        actions.ReplaceChildren(cancel is null ? [okButton] : [DialogButton(cancel, ButtonVariant.Secondary, Cancel), okButton]);

        var body = FlexBuilder.Column(gap: 16).Children(
            TextBuilder.New(title).FontSize(16f).FontWeight(FontWeight.Bold).TextColor(theme.TextColor),
            TextBuilder.New(message).FontSize(13f).TextColor(theme.Secondary).LineHeight(19f));
        if (input is not null) body.AddChild(input);
        body.AddChild(actions);

        var panel = BoxBuilder.New()
            .Width(420).Padding(new Thickness(20, 24, 20, 24)).CornerRadius(12f)
            .Background(theme.Surface).BorderColor(theme.Border).BorderWidth(1f)
            .BoxShadow(0, 18, 48, new Color(0, 0, 0, 90))
            .Children(body);
        // Enter answers yes and Escape no, wherever the focus is inside the dialog.
        panel.OnKeyDown(e =>
        {
            if (e.Key == KeyCode.Enter) { Accept(); e.StopPropagation = true; }
            else if (e.Key == KeyCode.Escape && cancel is not null) { Cancel(); e.StopPropagation = true; }
            else if (e.Key == KeyCode.Escape) { Accept(); e.StopPropagation = true; }
        });

        // Focus the answer the person is most likely to give — the text, or OK. Through the
        // window's focus manager, which overlay content is not given: Element.Focus() finds
        // none from inside a dialog and does nothing.
        Element first = input is not null ? input : okButton;
        if (focus is not null) Application.InvokeOnMainThread(() => focus.SetFocus(first));
        return panel;
    }

    private static Element DialogButton(string label, ButtonVariant variant, Action onClick)
    {
        var button = Button.New(label, variant);
        button.On<ClickEvent>(e => { e.StopPropagation = true; onClick(); });
        return button;
    }
}
