namespace Junction;

public static class RepositoryPathResolver
{
    public static string? FindGitRoot(string startPath)
    {
        if (string.IsNullOrWhiteSpace(startPath)) return null;

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(startPath);
        }
        catch
        {
            return null;
        }

        var dir = File.Exists(fullPath)
            ? new FileInfo(fullPath).Directory
            : new DirectoryInfo(fullPath);

        while (dir != null)
        {
            var gitPath = Path.Combine(dir.FullName, ".git");
            if (Directory.Exists(gitPath))
                return dir.FullName;

            dir = dir.Parent;
        }

        return null;
    }
}
