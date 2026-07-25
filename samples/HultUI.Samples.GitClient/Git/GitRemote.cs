namespace GitClient.Git;

/// <summary>A configured remote and its URLs. <see cref="PushUrl"/> equals
/// <see cref="FetchUrl"/> unless a separate push URL is set.</summary>
public record GitRemote(string Name, string FetchUrl, string PushUrl)
{
    /// <summary>True when the push URL differs from the fetch URL.</summary>
    public bool HasSeparatePushUrl => !string.Equals(FetchUrl, PushUrl, StringComparison.Ordinal);
}
