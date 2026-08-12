using Meridian.Core;
using Xunit;

namespace Meridian.Tests;

public class CollectionScannerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("meridian-scan-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void Write(string relative, string content)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private CollectionTree Scan() => CollectionScanner.Scan(CollectionWorkspace.TryOpen(_root)!);

    [Fact]
    public void RequestsFoldersAndEnvironmentsAllAppear()
    {
        Write("auth/login.req", "POST {{base_url}}/auth/login\nname Login\n");
        Write("charges/create-charge.req", "POST {{base_url}}/v1/charges\n");
        Write("charges/get-charge.req", "GET {{base_url}}/v1/charges/{{id}}\n");
        Write("charges/_folder.req", "auth oauth2 client_credentials\n");
        Write("envs/local.env", "base_url=http://localhost:8080\n");
        Write("envs/ci.env", "base_url=https://ci\n");

        var tree = Scan();

        Assert.Equal(["auth", "charges"], tree.Root.Folders.Select(f => f.Name));
        Assert.Equal("Login", tree.Root.Folders[0].Requests.Single().DisplayName);

        var charges = tree.Root.Folders[1];
        Assert.True(charges.HasFolderDefaults);
        // _folder.req is folder configuration, not a request row.
        Assert.Equal(["Create charge", "Get charge"], charges.Requests.Select(r => r.DisplayName));
        Assert.Equal(["POST", "GET"], charges.Requests.Select(r => r.Method));

        Assert.Equal(["ci", "local"], tree.Environments.Select(e => e.Name));
    }

    [Fact]
    public void FoldersWithNoRequestsAnywhereBelowArePruned()
    {
        Write("api/ping.req", "GET https://x/ping\n");
        Write("src/Program.cs", "// not a request");
        Write("docs/readme.md", "hello");
        Directory.CreateDirectory(Path.Combine(_root, "empty"));

        var tree = Scan();

        Assert.Equal(["api"], tree.Root.Folders.Select(f => f.Name));
    }

    [Fact]
    public void DotDirectoriesAreNeverEntered()
    {
        Write("real.req", "GET https://x\n");
        Write(".git/objects/fake.req", "GET https://evil\n");
        Write(".meridian/history.req", "GET https://cache\n");

        var tree = Scan();

        Assert.Empty(tree.Root.Folders);
        Assert.Single(tree.Root.Requests);
    }

    [Fact]
    public void ARequestWithErrorsStillGetsARow()
    {
        // The tree must not hide a file the user needs to fix.
        Write("broken.req", "not a request at all\n");

        var req = Scan().Root.Requests.Single();
        Assert.Equal("Broken", req.DisplayName);
        Assert.Null(req.Method);
    }

    [Fact]
    public void AnEmptyWorkspaceScansToAnEmptyRootNotNothing()
    {
        var tree = Scan();
        Assert.Empty(tree.Root.Requests);
        Assert.Empty(tree.Root.Folders);
        Assert.Empty(tree.Environments);
    }

    [Theory]
    [InlineData("get-charge", "Get charge")]
    [InlineData("list_all_refunds", "List all refunds")]
    [InlineData("ping", "Ping")]
    public void FileStemsPrettifyIntoDisplayNames(string stem, string expected)
    {
        Assert.Equal(expected, CollectionScanner.Prettify(stem));
    }
}
