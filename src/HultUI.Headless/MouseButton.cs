namespace HultUI.Headless;

/// <summary>
/// Mouse buttons in the framework's convention (the SDL3 backend normalizes
/// SDL's 1/2/3 to these values in <c>MapMouseButton</c>; element event
/// handlers compare against them, e.g. <c>Button == 0</c> for left).
/// </summary>
public enum MouseButton
{
    Left = 0,
    Middle = 1,
    Right = 2,
    Back = 4,    // X1 side button (SDL passes 4 through unchanged)
    Forward = 5, // X2 side button (SDL passes 5 through unchanged)
}
