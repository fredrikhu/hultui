using HultUI.Core.Accessibility;
using HultUI.Core.Animation;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;

namespace HultUI.Core.Dialogs;

/// <summary>
/// Presents modal dialogs. <see cref="Show{TResult}"/> is the general form: the
/// content is any element (including a reactive, data-bound component) and closes
/// itself with a result through the supplied controller. <see cref="Confirm"/> and
/// <see cref="Alert"/> are conveniences built on top for the common message-box case.
/// </summary>
public static class Dialog
{
    private static readonly Color DefaultScrim = new(0, 0, 0, 120);

    /// <summary>
    /// Shows a dialog whose content is built by <paramref name="content"/>, which
    /// receives a controller to close the dialog with a <typeparamref name="TResult"/>.
    /// Returns a task that completes when the dialog closes (with the default result
    /// if dismissed via the scrim).
    /// </summary>
    public static Task<TResult?> Show<TResult>(
        IOverlayHost host,
        Func<IDialogController<TResult>, Element> content,
        DialogOptions? options = null)
    {
        options ??= new DialogOptions();
        if (options.Presentation == DialogPresentation.Window)
            throw new NotSupportedException("DialogPresentation.Window is not implemented yet; use Overlay.");

        var tcs = new TaskCompletionSource<TResult?>();
        var controller = new DialogController<TResult>(tcs);

        var scrim = Box.New()
            .Width(Dimension.Percent(100)).Height(Dimension.Percent(100))
            .JustifyContent(JustifyContent.Center)
            .AlignItems(AlignItems.Center);
        if (options.ShowScrim)
            scrim.Background(options.ScrimColor ?? DefaultScrim);
        if (options.TopOffset is { } top)
            scrim.JustifyContent(JustifyContent.FlexStart).Padding(new Thickness(top, 0, 0, 0));

        var panel = content(controller);
        // Clicks inside the panel must not fall through to the scrim's dismiss.
        panel.On<ClickEvent>(e => e.StopPropagation = true);
        // What a screen reader announces it as, unless the content said otherwise.
        if (panel.Role == AccessibilityRole.None) panel.Role = AccessibilityRole.Dialog;
        scrim.ReplaceChildren(panel);

        // A box with a click is a button to a reader, but not one around a dialog: the scrim is
        // the dialog's backdrop, a node of no role and no name (AccessibilityTreeBuilder.InferRole).
        if (options.DismissOnScrimClick)
            scrim.On<ClickEvent>(_ => controller.Close(default!));
        // Last in line: the panel and everything in it see Escape first, and one that stops it
        // — a dropdown closing its list, a panel that answers on Escape — keeps it.
        if (options.DismissOnEscape)
            scrim.On<KeyDownEvent>(e =>
            {
                if (e.Key != KeyCode.Escape) return;
                e.StopPropagation = true;
                controller.Close(default!);
            });

        // Somewhere for the keyboard to be when a click inside the dialog lands on nothing that
        // takes it: keys go to the focus, and Escape has to reach the handler above. Not a Tab
        // stop. (A dialog with nothing focusable takes the keyboard itself; see below.)
        scrim.IsFocusable = true;
        scrim.TabIndex = -1;

        // Modal: Tab stays inside, and keys reach nothing behind the dialog.
        var handle = host.ShowOverlay(scrim, 0, 0, dismissOnOutsideClick: false, hitTestable: true, modal: true);
        controller.Bind(host, handle);

        // The dialog takes the keyboard, as a modal one has to: its first control, or the
        // dialog itself when it has none — the node a reader announces, a dialog named by its
        // title (it was the scrim, which a reader hears as an unnamed pane). Not a Tab stop.
        // Content that wants a particular field focuses it after this, and wins. Whatever had
        // the focus gets it back when the dialog closes.
        if (!Contains(scrim, scrim.FocusManager?.FocusedElement))
        {
            var first = FocusTraversal.FindNext(panel, null);
            if (first is null && !panel.IsFocusable)
            {
                panel.IsFocusable = true;
                panel.TabIndex = -1;
            }
            (first ?? panel).Focus();
        }

        // Subtle fade-in of the whole dialog.
        scrim.Opacity = 0f;
        Animate.Opacity(scrim, 0f, 1f).Duration(0.12f).Start();

        return tcs.Task;
    }

    private static bool Contains(Element root, Element? element)
    {
        for (var e = element; e is not null; e = e.Parent)
            if (e == root) return true;
        return false;
    }

    /// <summary>A yes/no confirmation. Resolves true if confirmed, false if cancelled or dismissed.</summary>
    public static Task<bool> Confirm(IOverlayHost host, string title, string message,
        string confirmText = "OK", string cancelText = "Cancel", bool destructive = false, DialogOptions? options = null)
    {
        return Show<bool>(host, ctrl =>
        {
            var theme = host.ActiveTheme ?? Theme.Light;
            return MessagePanel(theme, title, message,
                Button(cancelText, ButtonVariant.Secondary, () => ctrl.Close(false)),
                Button(confirmText, destructive ? ButtonVariant.Danger : ButtonVariant.Primary, () => ctrl.Close(true)));
        }, options)!;
    }

    /// <summary>A single-button acknowledgement.</summary>
    public static Task Alert(IOverlayHost host, string title, string message,
        string okText = "OK", DialogOptions? options = null)
    {
        return Show<bool>(host, ctrl =>
        {
            var theme = host.ActiveTheme ?? Theme.Light;
            return MessagePanel(theme, title, message,
                Button(okText, ButtonVariant.Primary, () => ctrl.Close(true)));
        }, options);
    }

    private static Element MessagePanel(Theme theme, string title, string message, params Element[] buttons)
    {
        var actions = Flex.Row(gap: 8).JustifyContent(JustifyContent.FlexEnd).AlignItems(AlignItems.Center);
        actions.ReplaceChildren(buttons);

        var panel = Box.New()
            .Width(400).Padding(new Thickness(20, 24, 20, 24)).CornerRadius(12f)
            .Background(theme.Surface).BorderColor(theme.Border).BorderWidth(1f)
            .BoxShadow(0, 18, 48, new Color(0, 0, 0, 90));
        panel.AccessibleName = title;
        return panel
            .Children(Flex.Column(gap: 16).Children(
                Text.New(title).FontSize(16f).FontWeight(FontWeight.Bold).TextColor(theme.TextColor),
                Text.New(message).FontSize(13f).TextColor(theme.Secondary).LineHeight(19f),
                actions));
    }

    /// <summary>
    /// The framework's own button, so a dialog's actions look like the app's and take the
    /// app's <see cref="ButtonTheme"/>: cancel is the outlined secondary, the confirming
    /// action primary, and a destructive one danger.
    /// </summary>
    private static Element Button(string label, ButtonVariant variant, Action onClick)
    {
        var btn = Builders.Button.New(label, variant);
        btn.On<ClickEvent>(e => { e.StopPropagation = true; onClick(); });
        return btn;
    }

    private sealed class DialogController<TResult> : IDialogController<TResult>
    {
        private readonly TaskCompletionSource<TResult?> _tcs;
        private IOverlayHost? _host;
        private OverlayHandle _handle;
        private bool _closed;

        public DialogController(TaskCompletionSource<TResult?> tcs) => _tcs = tcs;

        public void Bind(IOverlayHost host, OverlayHandle handle)
        {
            _host = host;
            _handle = handle;
        }

        public void Close(TResult result)
        {
            if (_closed) return;
            _closed = true;
            _host?.CloseOverlay(_handle);
            _tcs.TrySetResult(result);
        }
    }
}
