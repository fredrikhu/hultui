using HultUI.Core.Platform;
using HultUI.Core.Primitives;

namespace HultUI.Headless;

/// <summary>
/// Records the IME/text-input requests a focused text element makes, so tests
/// can assert the framework armed and disarmed OS text input correctly —
/// there is no real IME headless.
/// </summary>
public sealed class HeadlessTextInputHost : ITextInputHost
{
    /// <summary>True between StartTextInput and StopTextInput — i.e. while a text-editable element has focus.</summary>
    public bool IsTextInputActive { get; private set; }

    /// <summary>The caret rectangle most recently reported for IME candidate-window placement.</summary>
    public Rect? LastInputArea { get; private set; }

    /// <summary>Number of times an in-flight composition was cancelled (focus left a text input mid-preedit).</summary>
    public int CompositionClearCount { get; private set; }

    public void StartTextInput() => IsTextInputActive = true;

    public void StopTextInput() => IsTextInputActive = false;

    public void SetInputArea(Rect caretRect, int cursorOffset) => LastInputArea = caretRect;

    public void ClearComposition() => CompositionClearCount++;
}
