using System.Reflection;
using HultUI.Headless;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// The branch-name prompts validate what you type against git's ref-format rules.
///
/// <para>These used to accept anything non-blank and let git refuse it afterwards, so a
/// name like <c>my branch</c> came back as "fatal: 'my branch' is not a valid branch
/// name" — a sentence that names no character and arrives only after the dialog has
/// closed. <see cref="BranchName.Validate"/> already existed for the new-worktree prompt,
/// which derives a branch name from a folder; these tests pin the three typed prompts to
/// the same rules, so the same typo gets the same explanation everywhere.</para>
/// </summary>
public class BranchPromptValidationTests
{
    // the prompt's method on JunctionApp · the arguments it needs
    public static TheoryData<string, object?[]> Prompts => new()
    {
        { "PromptNewBranch", [] },
        { "PromptCreateBranchAt", new object?[] { "HEAD", "HEAD" } },
        { "PromptRenameBranch", new object?[] { "old-name" } },
    };

    [Theory]
    [MemberData(nameof(Prompts))]
    public void EveryBranchPromptRefusesANameGitWouldRefuse(string method, object?[] args)
    {
        Hosted((session, app) =>
        {
            var validate = ValidatorOf(app, method, args);

            // The sentence matters as much as the refusal: it names the character.
            var error = validate("my branch");
            Assert.NotNull(error);
            Assert.Contains("space", error, StringComparison.Ordinal);

            Assert.NotNull(validate(".hidden"));
            Assert.NotNull(validate("feature/x..y"));
            Assert.NotNull(validate(""));

            // ...and an ordinary name still gets through, or the prompt is unusable.
            Assert.Null(validate("feature/my-branch"));
        });
    }

    [Fact]
    public void ThePromptsShareOneValidatorRatherThanDriftingApart()
    {
        Hosted((session, app) =>
        {
            // Three prompts naming the same rules is the thing that rots: this asserts
            // they are literally the same delegate target, not three lookalikes.
            var targets = Prompts
                .Select(row => ValidatorOf(app, (string)row[0]!, (object?[])row[1]!).Method)
                .Distinct()
                .ToList();

            Assert.Single(targets);
            Assert.Equal(nameof(BranchName.Validate), targets[0].Name);
        });
    }

    /// <summary>Opens a prompt and hands back the validator its dialog was built with.</summary>
    private static Func<string, string?> ValidatorOf(JunctionApp app, string method, object?[] args)
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        var prompt = typeof(JunctionApp).GetMethod(method, Private);
        Assert.NotNull(prompt);
        prompt!.Invoke(app, args);

        var spec = typeof(JunctionApp).GetField("_dialog", Private)!.GetValue(app);
        Assert.NotNull(spec);

        var validate = spec!.GetType().GetProperty("Validate")!.GetValue(spec) as Func<string, string?>;
        Assert.NotNull(validate);
        return validate!;
    }

    private static void Hosted(Action<HeadlessSession, JunctionApp> body)
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        using var session = HeadlessSession.Create(1440, 900);
        JunctionApp? app = null;
        try
        {
            app = new JunctionApp(session.Application, session.Window.Window, new JunctionSettingsData
            {
                RepositoryPaths = [repo.Path],
                ActiveRepoPath = repo.Path,
                Workspaces = [new WorkspaceData { Id = "w1", Name = "Tests", RepoPaths = [repo.Path] }],
                ActiveWorkspaceId = "w1",
            });
            session.Application.SetRoot(app.Mount());
            session.PumpFrame();
            body(session, app);
        }
        finally { app?.Dispose(); }
    }
}
