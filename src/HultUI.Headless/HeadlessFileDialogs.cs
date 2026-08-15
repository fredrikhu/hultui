using HultUI.Core.Platform;

namespace HultUI.Headless;

/// <summary>
/// The file dialogs for a headless session: nothing opens, and the test says what the user
/// would have chosen.
///
/// <para>Every app that opens a picker had to hand-roll a fake to test the code around it —
/// Meridian's suite carried ten copies of the same three-line <c>NoPicker</c>. Scripting
/// belongs with the headless platform, so a test can drive the whole flow instead of only
/// the branch where the user cancels.</para>
///
/// <para>Unscripted, every call resolves to "cancelled", which is the shape app code has to
/// handle anyway.</para>
/// </summary>
public sealed class HeadlessFileDialogs : IFileDialogs
{
    private readonly Queue<IReadOnlyList<string>> _scripted = new();

    /// <summary>
    /// What the last call was asked for, so a test can assert the dialog was opened with
    /// the right starting directory or suggested name. Null until something is opened.
    /// </summary>
    public FileDialogRequest? LastRequest { get; private set; }

    /// <summary>How many dialogs have been opened this session.</summary>
    public int OpenCount { get; private set; }

    /// <summary>
    /// Whether calls resolve at all. Set false to test the branch an app takes on a
    /// platform with no dialog backend.
    /// </summary>
    public bool IsAvailable { get; set; } = true;

    /// <summary>
    /// Queues what the next dialog returns. Call once per expected dialog; results are
    /// handed out in order, and a call past the end of the queue reads as cancelled.
    /// </summary>
    public void EnqueueResult(params string[] paths) => _scripted.Enqueue(paths);

    /// <summary>Queues an explicit cancellation, for readability at the call site.</summary>
    public void EnqueueCancel() => _scripted.Enqueue([]);

    public Task<string?> OpenFolderAsync(string? initialDirectory = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Next(FileDialogKind.OpenFolder, initialDirectory, null, null).FirstOrDefault());

    public Task<string?> OpenFileAsync(string? initialDirectory = null,
        IReadOnlyList<FileDialogFilter>? filters = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Next(FileDialogKind.OpenFile, initialDirectory, null, filters).FirstOrDefault());

    public Task<IReadOnlyList<string>> OpenFilesAsync(string? initialDirectory = null,
        IReadOnlyList<FileDialogFilter>? filters = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Next(FileDialogKind.OpenFiles, initialDirectory, null, filters));

    public Task<string?> SaveFileAsync(string suggestedFileName, string? initialDirectory = null,
        IReadOnlyList<FileDialogFilter>? filters = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Next(FileDialogKind.SaveFile, initialDirectory, suggestedFileName, filters).FirstOrDefault());

    private IReadOnlyList<string> Next(FileDialogKind kind, string? initialDirectory,
        string? suggestedFileName, IReadOnlyList<FileDialogFilter>? filters)
    {
        LastRequest = new FileDialogRequest(kind, initialDirectory, suggestedFileName, filters ?? []);
        OpenCount++;

        if (!IsAvailable) return [];
        return _scripted.Count > 0 ? _scripted.Dequeue() : [];
    }
}

public enum FileDialogKind { OpenFile, OpenFiles, OpenFolder, SaveFile }

/// <summary>What a headless session was asked to open.</summary>
public sealed record FileDialogRequest(
    FileDialogKind Kind,
    string? InitialDirectory,
    string? SuggestedFileName,
    IReadOnlyList<FileDialogFilter> Filters);
