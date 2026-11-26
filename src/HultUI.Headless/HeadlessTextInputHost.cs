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

    /// <summary>The kind of field the most recent start was for.</summary>
    public TextInputKind Kind { get; private set; }

    /// <summary>Whether the most recent start was for a field of several lines (a text area, the
    /// rich text editor), for which a phone's keyboard shows a new-line key.</summary>
    public bool Multiline { get; private set; }

    /// <summary>How many times text input was started, switches between kinds included.</summary>
    public int StartCount { get; private set; }

    public void StartTextInput() => StartTextInput(TextInputKind.Text, multiline: false);

    public void StartTextInput(TextInputKind kind) => StartTextInput(kind, multiline: false);

    public void StartTextInput(TextInputKind kind, bool multiline)
    {
        IsTextInputActive = true;
        Kind = kind;
        Multiline = multiline;
        StartCount++;
    }

    public void StopTextInput() => IsTextInputActive = false;

    public void SetInputArea(Rect caretRect, int cursorOffset) => LastInputArea = caretRect;

    public void ClearComposition() => CompositionClearCount++;
}
