using System.Text;
using HultUI.Core.Platform;

namespace HultUI.WinForms.Services;

/// <summary>
/// The OS file and folder dialogs, through WinForms — so they are parented to the host
/// application's active form and look like every other dialog it opens.
/// </summary>
/// <remarks>
/// The framework documents these as awaitable and resuming on the UI thread. They are
/// modal here and run inline: WinForms' <c>ShowDialog</c> pumps its own message loop, so
/// the call returns only once the user is done, and the completed task's continuation
/// resumes on the thread that awaited it. The cancellation token therefore only
/// short-circuits before the dialog opens — as the interface says, no platform can
/// dismiss a dialog the user is looking at.
/// </remarks>
internal sealed class WinFormsFileDialogs : IFileDialogs
{
    public bool IsAvailable => true;

    public Task<string?> OpenFolderAsync(string? initialDirectory = null,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested) return Task.FromResult<string?>(null);

        using var dialog = new FolderBrowserDialog();
        if (Exists(initialDirectory)) dialog.InitialDirectory = initialDirectory!;
        return Task.FromResult(Show(dialog) ? dialog.SelectedPath : null);
    }

    public Task<string?> OpenFileAsync(string? initialDirectory = null,
        IReadOnlyList<FileDialogFilter>? filters = null,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested) return Task.FromResult<string?>(null);

        using var dialog = new OpenFileDialog { Multiselect = false };
        Prepare(dialog, initialDirectory, filters);
        return Task.FromResult(Show(dialog) ? dialog.FileName : null);
    }

    public Task<IReadOnlyList<string>> OpenFilesAsync(string? initialDirectory = null,
        IReadOnlyList<FileDialogFilter>? filters = null,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromResult<IReadOnlyList<string>>([]);

        using var dialog = new OpenFileDialog { Multiselect = true };
        Prepare(dialog, initialDirectory, filters);
        return Task.FromResult<IReadOnlyList<string>>(Show(dialog) ? dialog.FileNames : []);
    }

    public Task<string?> SaveFileAsync(string suggestedFileName, string? initialDirectory = null,
        IReadOnlyList<FileDialogFilter>? filters = null,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested) return Task.FromResult<string?>(null);

        // OverwritePrompt is the default, and the interface promises a returned path
        // means the user already agreed to replace whatever was there.
        using var dialog = new SaveFileDialog { FileName = suggestedFileName, OverwritePrompt = true };
        Prepare(dialog, initialDirectory, filters);
        return Task.FromResult(Show(dialog) ? dialog.FileName : null);
    }

    private static void Prepare(FileDialog dialog, string? initialDirectory,
        IReadOnlyList<FileDialogFilter>? filters)
    {
        if (Exists(initialDirectory)) dialog.InitialDirectory = initialDirectory!;
        if (filters is { Count: > 0 }) dialog.Filter = ToWindowsFilter(filters);
    }

    /// <summary>Parents the dialog to whatever form is active, so it centres on the app
    /// and blocks the right window instead of floating free.</summary>
    private static bool Show(CommonDialog dialog)
        => dialog.ShowDialog(Form.ActiveForm) == DialogResult.OK;

    private static bool Exists(string? directory)
        => !string.IsNullOrEmpty(directory) && Directory.Exists(directory);

    /// <summary>
    /// The framework's filters carry extensions without dots (<c>"png;jpg"</c>); Windows
    /// wants <c>"Label|*.png;*.jpg"</c> pairs joined by more pipes.
    /// </summary>
    private static string ToWindowsFilter(IReadOnlyList<FileDialogFilter> filters)
    {
        var builder = new StringBuilder();
        foreach (var filter in filters)
        {
            if (builder.Length > 0) builder.Append('|');
            builder.Append(filter.Label).Append('|');

            var patterns = filter.Pattern.Split(';', StringSplitOptions.RemoveEmptyEntries
                | StringSplitOptions.TrimEntries);
            for (int i = 0; i < patterns.Length; i++)
            {
                if (i > 0) builder.Append(';');
                builder.Append(patterns[i] == "*" ? "*.*" : "*." + patterns[i]);
            }
        }
        return builder.ToString();
    }
}
