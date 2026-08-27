using Junction.Git;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// Pushing a branch that has never been pushed.
///
/// <para>A branch created locally tracks nothing, and a bare <c>git push</c> answers that
/// with a fatal error and a command to copy into a terminal — which is the one thing a
/// git client exists to save you from. Junction publishes it instead, and these pin both
/// halves: the decision (which remote, and whether an upstream is being created) and that
/// the branch really does track afterwards.</para>
/// </summary>
public class PushUpstreamTests : IDisposable
{
    private readonly string _remotePath;

    public PushUpstreamTests()
    {
        _remotePath = Path.Combine(Path.GetTempPath(), "junction-git-remote-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_remotePath);
        GitProcess.Run(_remotePath, "init", "--bare", "-b", "main");
    }

    public void Dispose() => TempRepo.ForceDelete(_remotePath);

    /// <summary>A repository with <c>origin</c> pointing at a bare repository on disk, so
    /// a push is a real push rather than a mock of one.</summary>
    private TempRepo WithOrigin(string remoteName = "origin")
    {
        var repo = TempRepo.CreateWithInitialCommit();
        repo.Git("remote", "add", remoteName, _remotePath);
        return repo;
    }

    [Fact]
    public void ABranchWithNoUpstreamIsPlannedAsAPublish()
    {
        using var t = WithOrigin();
        t.Git("checkout", "-b", "feature/new");

        var plan = t.Repo.PlanPush();

        Assert.Null(plan.Problem);
        Assert.True(plan.SetUpstream);
        Assert.Equal("origin", plan.Remote);
        Assert.Equal("feature/new", plan.Branch);
    }

    [Fact]
    public async Task PublishingActuallyMakesTheBranchTrack()
    {
        using var t = WithOrigin();
        t.Git("checkout", "-b", "feature/new");
        Assert.Null(t.Repo.GetUpstream("feature/new"));

        var plan = t.Repo.PlanPush();
        var result = await t.Repo.RunPushAsync(plan);

        Assert.True(result.Success, result.Stderr);
        Assert.Equal("origin/feature/new", t.Repo.GetUpstream("feature/new"));
        // And the next push is an ordinary one, not a second publish.
        Assert.False(t.Repo.PlanPush().SetUpstream);
    }

    [Fact]
    public void ABranchThatAlreadyTracksIsPushedAsIs()
    {
        using var t = WithOrigin();
        t.Git("push", "--set-upstream", "origin", "main");

        var plan = t.Repo.PlanPush();

        Assert.False(plan.SetUpstream);
        Assert.Null(plan.Remote);
        Assert.Null(plan.Problem);
    }

    [Fact]
    public async Task ARepositoryWithNoRemoteSaysSoInsteadOfFailingInGit()
    {
        using var t = TempRepo.CreateWithInitialCommit();

        var plan = t.Repo.PlanPush();

        Assert.NotNull(plan.Problem);
        Assert.Contains("no remote", plan.Problem!, StringComparison.OrdinalIgnoreCase);
        // And running it stays a failure rather than shelling out to a push that cannot work.
        var result = await t.Repo.RunPushAsync(plan);
        Assert.False(result.Success);
    }

    [Fact]
    public void ADetachedHeadIsRefusedWithAReasonRatherThanPublished()
    {
        using var t = WithOrigin();
        var head = t.Repo.GetCommit("HEAD")!;
        t.Git("checkout", "--detach", head.Hash);

        var plan = t.Repo.PlanPush();

        Assert.NotNull(plan.Problem);
        Assert.Contains("detached", plan.Problem!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheOnlyRemoteIsUsedWhenItIsNotCalledOrigin()
    {
        using var t = WithOrigin("upstream");
        t.Git("checkout", "-b", "feature/new");

        Assert.Equal("upstream", t.Repo.PlanPush().Remote);
    }

    [Fact]
    public void RemotePushDefaultWinsOverOrigin()
    {
        using var t = WithOrigin();
        t.Git("remote", "add", "fork", _remotePath);
        t.Git("config", "remote.pushDefault", "fork");
        t.Git("checkout", "-b", "feature/new");

        Assert.Equal("fork", t.Repo.PlanPush().Remote);
    }

    [Fact]
    public void ANamedBranchCanBePlannedWithoutCheckingItOut()
    {
        using var t = WithOrigin();
        t.Git("branch", "side");

        var plan = t.Repo.PlanPush("side");

        Assert.True(plan.SetUpstream);
        Assert.Equal("side", plan.Branch);
    }
}
