namespace Meridian.Platform;

/// <summary>
/// Native "save as" dialog abstraction, mirroring <see cref="IFolderPicker"/>.
/// Returns the chosen full path, or null when cancelled/unavailable.
/// </summary>
public interface ISaveFilePicker
{
    bool IsAvailable { get; }

    /// <param name="suggestedFileName">Pre-filled file name (with extension).</param>
    /// <param name="initialDirectory">Starting directory, when the platform supports it.</param>
    /// <param name="filterLabel">Human label for the file-type filter (e.g. "Patch files").</param>
    /// <param name="extension">Extension without the dot (e.g. "patch").</param>
    Task<string?> PickSaveFileAsync(string suggestedFileName, string? initialDirectory = null,
        string? filterLabel = null, string? extension = null, CancellationToken cancellationToken = default);
}
