namespace HultUI.Core.Platform;

/// <summary>
/// A file-type filter for an open or save dialog.
/// </summary>
/// <param name="Label">What the user sees in the file-type dropdown, e.g. "Patch files".</param>
/// <param name="Pattern">
/// Extensions without dots, separated by semicolons: <c>"patch"</c>, <c>"png;jpg;jpeg"</c>.
/// A single <c>"*"</c> matches everything.
/// </param>
public readonly record struct FileDialogFilter(string Label, string Pattern);

/// <summary>
/// The OS file and folder dialogs. Every app that opens a document, picks a workspace, or
/// exports anything needs these, and each one that hand-rolls them re-solves the same
/// platform problem — so the framework owns the abstraction.
///
/// <para>All three methods resolve to <c>null</c> (or an empty list) when the user
/// cancels, and behave the same way when <see cref="IsAvailable"/> is false, so a caller
/// that only wants to handle "no path chosen" does not need to branch on availability
/// first.</para>
///
/// <para><b>Threading.</b> Call these from the UI thread. The returned task completes with
/// continuations scheduled rather than run inline, so an <c>await</c> from UI-thread code
/// resumes on the UI thread through the installed synchronization context — the result can
/// be assigned straight to reactive state.</para>
/// </summary>
public interface IFileDialogs
{
    /// <summary>
    /// Whether the platform can show dialogs at all. False in headless sessions unless the
    /// test has scripted a result, and on a desktop with no usable dialog backend.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>Asks for one existing folder. Returns its full path, or null if cancelled.</summary>
    /// <param name="initialDirectory">Where to start browsing. Ignored if it does not exist.</param>
    /// <param name="cancellationToken">
    /// Abandons the await. It does <b>not</b> close a dialog the OS has already put on
    /// screen — no platform offers that — so the user still has to dismiss it.
    /// </param>
    Task<string?> OpenFolderAsync(string? initialDirectory = null,
        CancellationToken cancellationToken = default);

    /// <summary>Asks for one existing file. Returns its full path, or null if cancelled.</summary>
    Task<string?> OpenFileAsync(string? initialDirectory = null,
        IReadOnlyList<FileDialogFilter>? filters = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks for one or more existing files. Returns an empty list if cancelled, never null.
    /// </summary>
    Task<IReadOnlyList<string>> OpenFilesAsync(string? initialDirectory = null,
        IReadOnlyList<FileDialogFilter>? filters = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks where to save. Returns the chosen full path, or null if cancelled. The platform
    /// dialog handles its own overwrite confirmation, so a returned path means the user has
    /// already agreed to replace anything there.
    /// </summary>
    /// <param name="suggestedFileName">Pre-filled name, with extension.</param>
    /// <param name="initialDirectory">Where the dialog opens. Null lets the platform decide,
    /// which usually means wherever it was left last.</param>
    /// <param name="filters">The file-type choices offered. Null offers no filtering.</param>
    /// <param name="cancellationToken">Abandons the wait. The platform dialog may stay on
    /// screen — cancelling here stops the caller waiting for it, not the dialog itself.</param>
    Task<string?> SaveFileAsync(string suggestedFileName, string? initialDirectory = null,
        IReadOnlyList<FileDialogFilter>? filters = null,
        CancellationToken cancellationToken = default);
}
