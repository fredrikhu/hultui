using Junction.GitLab;
using Xunit;

namespace Junction.Tests.GitLab;

/// <summary>
/// A remote URL says where a project lives. git accepts three spellings of a hosted
/// repository and a handful of things that are not hosted at all; the parser must read the
/// first three identically and refuse the rest, because a wrong host here means the app asks
/// the wrong server for merge requests and shows "project not found" on a repo that is fine.
/// </summary>
public class GitLabRemoteTests
{
    [Theory]
    [InlineData("https://gitlab.com/acme/core-api.git", "gitlab.com", "https", null, "acme/core-api")]
    [InlineData("https://gitlab.com/acme/core-api", "gitlab.com", "https", null, "acme/core-api")]
    [InlineData("https://gitlab.com/acme/core-api/", "gitlab.com", "https", null, "acme/core-api")]
    [InlineData("https://oauth2:glpat-secret@gitlab.com/acme/core-api.git", "gitlab.com", "https", null, "acme/core-api")]
    [InlineData("http://gitlab.lab.example:8080/acme/platform/infra.git", "gitlab.lab.example", "http", 8080, "acme/platform/infra")]
    [InlineData("HTTPS://GitLab.Com/Acme/Core-API.GIT", "gitlab.com", "https", null, "Acme/Core-API")]
    [InlineData("git@gitlab.com:acme/core-api.git", "gitlab.com", "ssh", null, "acme/core-api")]
    [InlineData("git@gitlab.com:acme/platform/infra.git", "gitlab.com", "ssh", null, "acme/platform/infra")]
    [InlineData("ssh://git@gitlab.com/acme/core-api.git", "gitlab.com", "ssh", null, "acme/core-api")]
    [InlineData("ssh://git@gitlab.acme.internal:2222/acme/core-api.git", "gitlab.acme.internal", "ssh", 2222, "acme/core-api")]
    public void TheThreeHostedSpellingsReadTheSame(string url, string host, string scheme, int? port, string path)
    {
        var remote = GitLabRemote.TryParse(url);

        Assert.NotNull(remote);
        Assert.Equal(host, remote.Host);
        Assert.Equal(scheme, remote.Scheme);
        Assert.Equal(port, remote.Port);
        Assert.Equal(path, remote.ProjectPath);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"C:\Users\fredr\source\repos\demo")]
    [InlineData("C:/Users/fredr/source/repos/demo")]
    [InlineData("/home/fredr/repos/demo")]
    [InlineData("../sibling")]
    [InlineData("file:///home/fredr/repos/demo.git")]
    [InlineData("https://gitlab.com/just-a-user.git")]       // one segment: not a project
    [InlineData("https://gitlab.com/")]
    [InlineData("git@gitlab.com:only-one.git")]
    [InlineData("ftp://gitlab.com/acme/core-api.git")]
    public void EverythingElseIsNotHosted(string? url)
    {
        Assert.Null(GitLabRemote.TryParse(url));
    }

    [Fact]
    public void TheApiRootKeepsAnHttpRemotesSchemeAndPort()
    {
        var remote = GitLabRemote.TryParse("http://gitlab.lab:8080/acme/core-api.git")!;

        Assert.Equal("http://gitlab.lab:8080/api/v4", remote.ApiBaseUrl);
        Assert.Equal("http://gitlab.lab:8080", remote.WebBaseUrl);
    }

    [Fact]
    public void AnSshRemoteSaysNothingAboutTheWebSideSoHttpsIsAssumed()
    {
        var remote = GitLabRemote.TryParse("ssh://git@gitlab.acme.internal:2222/acme/core-api.git")!;

        Assert.Equal("https://gitlab.acme.internal/api/v4", remote.ApiBaseUrl);
    }

    [Fact]
    public void TheProjectPathIsOneUrlSegmentWhenAddressed()
    {
        var remote = GitLabRemote.TryParse("git@gitlab.com:acme/platform/infra.git")!;

        Assert.Equal("acme%2Fplatform%2Finfra", remote.EncodedProjectPath);
        Assert.Equal("infra", remote.ProjectName);
    }
}
