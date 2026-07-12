using HultUI.Core.Primitives;

namespace HultUI.Core.Dialogs;

/// <summary>Where a dialog is hosted. Overlay renders in the current window; Window
/// (not yet implemented) will spawn a separate OS window with the same content.</summary>
public enum DialogPresentation
{
    Overlay,
    Window
}

public sealed class DialogOptions
{
    /// <summary>Presentation backend. Only <see cref="DialogPresentation.Overlay"/> is implemented so far.</summary>
    public DialogPresentation Presentation { get; init; } = DialogPresentation.Overlay;

    /// <summary>Draw a dimming scrim behind the dialog. When false the app shows through.</summary>
    public bool ShowScrim { get; init; } = true;

    /// <summary>Clicking the scrim closes the dialog with the default result.</summary>
    public bool DismissOnScrimClick { get; init; } = true;

    /// <summary>Scrim colour. Null uses a default translucent black.</summary>
    public Color? ScrimColor { get; init; }
}
