using HultUI.Core.Builders;
using HultUI.Core.Elements;
using Meridian.Core;
using Meridian.Theme;

namespace Meridian;

public partial class MeridianApp
{
    // ── Made in the app: a collection, its requests and its folders ──────
    // Nothing here needs a hand-written file. Each asks for a name in the prompt, which refuses
    // what can't be one (CollectionNames, and a clash in any letter case) before anything is
    // written, and each file is created with FileMode.CreateNew: one the last scan missed is
    // never overwritten.

    /// <summary>A new collection's first request, a GET of the base URL its local environment holds.</summary>
    internal const string FirstRequestFile = "first-request.req";

    /// <summary>What a new request starts as, the first one of a collection too: the URL is the
    /// one thing it needs, and the editor opens on it.</summary>
    private const string NewRequestText = "GET {{base_url}}/\n";

    /// <summary>A new folder's <c>_folder.req</c>. The scanner keeps a folder that has one (it
    /// prunes folders with no requests below them), so the folder shows before its first request,
    /// and it is where the folder's own auth and headers go.</summary>
    private const string FolderDefaultsText = "# Defaults for every request in this folder: auth and headers set here apply below.\n";

    // ── New collection (the front door's card, Ctrl+N) ───────────────────

    /// <summary>Where, then a name, then the base URL. Nothing is written until the last prompt's
    /// OK, so a cancel anywhere leaves the disk as it was.</summary>
    private async void NewCollection()
    {
        if (_pickingFolder) return;
        _pickingFolder = true;
        string? parent;
        try
        {
            parent = await _fileDialogs.OpenFolderAsync(
                _workspace is { } ws ? System.IO.Path.GetDirectoryName(ws.Root) : null);
        }
        finally
        {
            _pickingFolder = false;
        }
        if (parent != null) AskCollectionName(parent);
    }

    /// <summary>Test seam: what the picker callback does once the parent folder is chosen.</summary>
    internal void AskCollectionName(string parent) =>
        OpenDialog($"New collection in {parent}", "name — payments-api",
            name => OpenDialog($"Base URL for {name.Trim()}", "https://api.example.com — or blank, to set it later",
                url => CreateCollection(parent, name, url), BaseUrlProblem),
            name => CollectionNameProblem(parent, name));

    private static string? CollectionNameProblem(string parent, string input)
    {
        string name = input.Trim();
        if (CollectionNames.Problem(name, "collection") is { } problem) return problem;
        if (CollectionNames.Taken(parent, name) is { } existing) return $"{existing} already exists there.";
        return null;
    }

    /// <summary>Blank, or an absolute http(s) URL: <c>localhost:8080</c> would read as a URL
    /// whose scheme is localhost.</summary>
    private static string? BaseUrlProblem(string input)
    {
        string url = input.Trim();
        if (url.Length == 0) return null;
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? null
            : "A base URL starts with http:// or https://";
    }

    /// <summary>Makes <c>parent/name/</c> with <see cref="FirstRequestFile"/> and
    /// <c>envs/local.env</c> holding <c>base_url</c>, opens it, and opens the first request.</summary>
    internal void CreateCollection(string parent, string nameInput, string urlInput)
    {
        string name = nameInput.Trim();
        if (CollectionNameProblem(parent, name) is { } problem) { Flash(problem); return; }   // the prompt refuses these first
        // The first request is {{base_url}}/, so the base URL keeps no slash of its own.
        string url = urlInput.Trim().TrimEnd('/');
        var root = System.IO.Path.Combine(parent, name);
        try
        {
            Directory.CreateDirectory(System.IO.Path.Combine(root, "envs"));
            WriteNewFile(System.IO.Path.Combine(root, FirstRequestFile), NewRequestText);
            WriteNewFile(System.IO.Path.Combine(root, "envs", "local.env"), $"base_url={url}\n");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Flash($"Could not create {name}: {e.Message}");
            return;
        }
        OpenWorkspace(root);
        if (_workspace is { } ws)
            OpenRequest(System.IO.Path.Combine(ws.Root, FirstRequestFile));
    }

    private static void WriteNewFile(string path, string text)
    {
        using var stream = new FileStream(path, FileMode.CreateNew);
        using var writer = new StreamWriter(stream);   // UTF-8, no BOM
        writer.Write(text);
    }

    // ── New request / new folder (the Requests tree) ─────────────────────

    /// <summary>The tree's ＋ for <paramref name="folder"/> (the collection's root, or a folder in it).</summary>
    internal void OpenNewRequestDialog(string folder) =>
        OpenDialog($"New request in {FolderTitle(folder)}", "name — list-users",
            name => CreateRequest(folder, name), name => RequestNameProblem(folder, name));

    internal void OpenNewFolderDialog(string folder) =>
        OpenDialog($"New folder in {FolderTitle(folder)}", "name — users",
            name => CreateFolder(folder, name), name => FolderNameProblem(folder, name));

    /// <summary>"payments-api/" for the collection's root, "payments-api/users/" below it.</summary>
    private string FolderTitle(string folder)
    {
        var ws = _workspace!;
        string relative = Relative(folder);
        return relative == "." ? ws.Name + "/" : $"{ws.Name}/{relative}/";
    }

    /// <summary>Writes <c>name.req</c>, rescans, and opens it with its URL box taking the keyboard.
    /// The name rules are a rename's too (<see cref="RequestNameProblem"/>).</summary>
    internal void CreateRequest(string folder, string input)
    {
        if (_workspace is null) return;
        if (RequestNameProblem(folder, input) is { } problem) { Flash(problem); return; }
        string name = RequestNameFrom(input);
        var path = System.IO.Path.Combine(folder, name + ".req");
        try
        {
            WriteNewFile(path, NewRequestText);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Flash($"Could not create {name}.req: {e.Message}");
            return;
        }
        RescanWorkspace();
        OpenRequest(path, editUrl: true);
    }

    /// <summary>Makes the folder with its <c>_folder.req</c> (see <see cref="FolderDefaultsText"/>) and rescans.</summary>
    internal void CreateFolder(string parent, string input)
    {
        if (_workspace is null) return;
        if (FolderNameProblem(parent, input) is { } problem) { Flash(problem); return; }
        string name = FolderNameFrom(input);
        var dir = System.IO.Path.Combine(parent, name);
        try
        {
            Directory.CreateDirectory(dir);
            WriteNewFile(System.IO.Path.Combine(dir, "_folder.req"), FolderDefaultsText);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Flash($"Could not create {name}/: {e.Message}");
            return;
        }
        RescanWorkspace();
        MarkDirty();
    }

    /// <summary>A 22px icon action in the tree: ＋ request and ＋ folder, on the header and on each folder.</summary>
    private static Element TreeAction(string glyph, string name, string automationId, Action onClick) =>
        Ui.IconButton(glyph, 13f, Azimuth.TextMuted, name, onClick, automationId, Ui.TreeAction).Tooltip(name);
}
