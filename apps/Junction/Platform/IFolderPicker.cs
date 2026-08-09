namespace Junction.Platform;

public interface IFolderPicker
{
    bool IsAvailable { get; }

    Task<string?> PickFolderAsync(string? initialDirectory = null, CancellationToken cancellationToken = default);
}
