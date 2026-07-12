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

        var panel = content(controller);
        // Clicks inside the panel must not fall through to the scrim's dismiss.
        panel.On<ClickEvent>(e => e.StopPropagation = true);
        scrim.ReplaceChildren(panel);

        if (options.DismissOnScrimClick)
            scrim.On<ClickEvent>(_ => controller.Close(default!));

        var handle = host.ShowOverlay(scrim, 0, 0, dismissOnOutsideClick: false, hitTestable: true);
        controller.Bind(host, handle);

        // Subtle fade-in of the whole dialog.
        scrim.Opacity = 0f;
        Animate.Opacity(scrim, 0f, 1f).Duration(0.12f).Start();

        return tcs.Task;
    }

    /// <summary>A yes/no confirmation. Resolves true if confirmed, false if cancelled or dismissed.</summary>
    public static Task<bool> Confirm(IOverlayHost host, string title, string message,
        string confirmText = "OK", string cancelText = "Cancel", bool destructive = false, DialogOptions? options = null)
    {
        return Show<bool>(host, ctrl =>
        {
            var theme = host.ActiveTheme ?? Theme.Light;
            return MessagePanel(theme, title, message,
                Button(theme, cancelText, ButtonKind.Neutral, () => ctrl.Close(false)),
                Button(theme, confirmText, destructive ? ButtonKind.Danger : ButtonKind.Primary, () => ctrl.Close(true)));
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
                Button(theme, okText, ButtonKind.Primary, () => ctrl.Close(true)));
        }, options);
    }

    private static Element MessagePanel(Theme theme, string title, string message, params Element[] buttons)
    {
        var actions = Flex.Row(gap: 8).JustifyContent(JustifyContent.FlexEnd).AlignItems(AlignItems.Center);
        actions.ReplaceChildren(buttons);

        return Box.New()
            .Width(400).Padding(new Thickness(20, 24, 20, 24)).CornerRadius(12f)
            .Background(theme.Surface).BorderColor(theme.Border).BorderWidth(1f)
            .BoxShadow(0, 18, 48, new Color(0, 0, 0, 90))
            .Children(Flex.Column(gap: 16).Children(
                Text.New(title).FontSize(16f).FontWeight(FontWeight.Bold).TextColor(theme.TextColor),
                Text.New(message).FontSize(13f).TextColor(theme.Secondary).LineHeight(19f),
                actions));
    }

    private enum ButtonKind { Neutral, Primary, Danger }

    private static Element Button(Theme theme, string label, ButtonKind kind, Action onClick)
    {
        var (bg, fg) = kind switch
        {
            ButtonKind.Danger => (theme.Danger, theme.DangerText),
            ButtonKind.Primary => (theme.Primary, theme.PrimaryText),
            _ => (theme.SurfaceVariant, theme.TextColor),
        };

        var btn = Box.New()
            .Padding(new Thickness(8, 16, 8, 16)).CornerRadius(8f)
            .Background(bg).Cursor(CursorType.Pointer)
            .OnHover(b => b.Background(Darken(bg)))
            .Transition(TransitionProperty.Background, 120f)
            .Children(Text.New(label).FontSize(13f).FontWeight(FontWeight.SemiBold).TextColor(fg));

        btn.On<ClickEvent>(e => { e.StopPropagation = true; onClick(); });
        return btn;
    }

    private static Color Darken(Color c) => new((byte)(c.R * 0.85f), (byte)(c.G * 0.85f), (byte)(c.B * 0.85f), c.A);

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
