namespace HultUI.Core.Dialogs;

/// <summary>
/// Handed to a dialog's content builder so the content can close the dialog and
/// resolve its awaited result. A fully-fledged (reactive, data-bound) dialog holds
/// onto this and calls <see cref="Close"/> when the user finishes.
/// </summary>
public interface IDialogController<in TResult>
{
    /// <summary>Closes the dialog, completing the awaited task with <paramref name="result"/>.</summary>
    void Close(TResult result);
}
