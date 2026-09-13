using System.Text.Json.Serialization;

namespace Junction.GitLab;

// The shapes GitLab's REST API v4 answers with, trimmed to what Junction reads. Property
// names follow the JSON (snake_case through the context's naming policy), so a field here is
// the field in docs.gitlab.com/api — no renaming layer to keep in sync. Everything nullable
// that the API can omit is nullable here; a missing field must never fail a whole list.

public sealed class GlUser
{
    public long Id { get; set; }
    public string Username { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Email { get; set; }
    public string? AvatarUrl { get; set; }
    public string? WebUrl { get; set; }
    public string? State { get; set; }
}

/// <summary><c>GET /personal_access_tokens/self</c> — what a pasted token can do and how
/// long it lasts. An OAuth access token answers this endpoint with 404.</summary>
public sealed class GlTokenInfo
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public List<string> Scopes { get; set; } = [];
    public bool Active { get; set; }
    public bool Revoked { get; set; }
    /// <summary>A date only (<c>2026-10-12</c>), or null for a token that never expires.</summary>
    public string? ExpiresAt { get; set; }
    public string? CreatedAt { get; set; }
    /// <summary>Only in the answer to a rotate: the new token, shown once.</summary>
    public string? Token { get; set; }

    public DateTime? Expires =>
        DateTime.TryParse(ExpiresAt, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var d) ? d : null;
}

public sealed class GlNamespace
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string FullPath { get; set; } = "";
    public string? Kind { get; set; }
    public string? AvatarUrl { get; set; }
}

public sealed class GlProject
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string PathWithNamespace { get; set; } = "";
    public string? Description { get; set; }
    public string? DefaultBranch { get; set; }
    public string? WebUrl { get; set; }
    public string? HttpUrlToRepo { get; set; }
    public string? SshUrlToRepo { get; set; }
    public string? AvatarUrl { get; set; }
    public string? Visibility { get; set; }
    /// <summary><c>merge</c>, <c>rebase_merge</c> or <c>ff</c>.</summary>
    public string? MergeMethod { get; set; }
    /// <summary><c>never</c>, <c>always</c>, <c>default_on</c>, <c>default_off</c>.</summary>
    public string? SquashOption { get; set; }
    public bool? RemoveSourceBranchAfterMerge { get; set; }
    public bool? OnlyAllowMergeIfPipelineSucceeds { get; set; }
    public bool? OnlyAllowMergeIfAllDiscussionsAreResolved { get; set; }
    public GlNamespace? Namespace { get; set; }
    public DateTimeOffset? LastActivityAt { get; set; }
    public GlProjectPermissions? Permissions { get; set; }

    public bool SquashByDefault => SquashOption is "always" or "default_on";
}

public sealed class GlProjectPermissions
{
    public GlAccess? ProjectAccess { get; set; }
    public GlAccess? GroupAccess { get; set; }
}

public sealed class GlAccess
{
    public int AccessLevel { get; set; }
}

public sealed class GlAccessLevel
{
    public long? Id { get; set; }
    /// <summary>0 none · 30 developer · 40 maintainer · 60 admin.</summary>
    public int AccessLevel { get; set; }
    public string? AccessLevelDescription { get; set; }
    public long? UserId { get; set; }
    public long? GroupId { get; set; }
}

public sealed class GlProtectedBranch
{
    public long Id { get; set; }
    /// <summary>May be a wildcard (<c>release/*</c>).</summary>
    public string Name { get; set; } = "";
    public bool AllowForcePush { get; set; }
    public bool? CodeOwnerApprovalRequired { get; set; }
    public List<GlAccessLevel> PushAccessLevels { get; set; } = [];
    public List<GlAccessLevel> MergeAccessLevels { get; set; } = [];

    /// <summary>The highest role that may push; 0 when nobody can.</summary>
    public int PushLevel => PushAccessLevels.Count == 0 ? 0 : PushAccessLevels.Max(l => l.AccessLevel);

    /// <summary>Whether <paramref name="branch"/> is covered, with git's own wildcard rule:
    /// <c>*</c> matches any run of characters including slashes.</summary>
    public bool Matches(string branch)
    {
        if (!Name.Contains('*')) return string.Equals(Name, branch, StringComparison.Ordinal);
        var parts = Name.Split('*');
        int pos = 0;
        for (int i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (part.Length == 0) continue;
            int idx = i == 0
                ? (branch.StartsWith(part, StringComparison.Ordinal) ? 0 : -1)
                : branch.IndexOf(part, pos, StringComparison.Ordinal);
            if (idx < 0) return false;
            pos = idx + part.Length;
        }
        return parts[^1].Length == 0 || branch.EndsWith(parts[^1], StringComparison.Ordinal);
    }
}

public sealed class GlReferences
{
    public string? Short { get; set; }
    public string? Relative { get; set; }
    public string? Full { get; set; }
}

public sealed class GlMilestone
{
    public long Id { get; set; }
    public long Iid { get; set; }
    public string Title { get; set; } = "";
    public string? State { get; set; }
}

public sealed class GlDiffRefs
{
    public string? BaseSha { get; set; }
    public string? HeadSha { get; set; }
    public string? StartSha { get; set; }
}

public sealed class GlPipeline
{
    public long Id { get; set; }
    public long? Iid { get; set; }
    public long? ProjectId { get; set; }
    public string? Sha { get; set; }
    public string? Ref { get; set; }
    /// <summary><c>created · waiting_for_resource · preparing · pending · running · success
    /// · failed · canceled · skipped · manual · scheduled</c>.</summary>
    public string Status { get; set; } = "";
    public string? Source { get; set; }
    public string? WebUrl { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
}

public sealed class GlMergeRequest
{
    public long Id { get; set; }
    public long Iid { get; set; }
    public long ProjectId { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    /// <summary><c>opened · closed · merged · locked</c>.</summary>
    public string State { get; set; } = "";
    public bool Draft { get; set; }
    public string SourceBranch { get; set; } = "";
    public string TargetBranch { get; set; } = "";
    public long? SourceProjectId { get; set; }
    public long? TargetProjectId { get; set; }
    public string? Sha { get; set; }
    public string? MergeCommitSha { get; set; }
    public string? SquashCommitSha { get; set; }
    /// <summary>The mergeability word: <c>mergeable · checking · unchecked · conflict ·
    /// need_rebase · ci_still_running · ci_must_pass · not_approved ·
    /// discussions_not_resolved · draft_status · not_open · …</c>.</summary>
    public string? DetailedMergeStatus { get; set; }
    public bool HasConflicts { get; set; }
    public bool? BlockingDiscussionsResolved { get; set; }
    public string? WebUrl { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? MergedAt { get; set; }
    public GlUser? Author { get; set; }
    public GlUser? MergedBy { get; set; }
    public List<GlUser> Assignees { get; set; } = [];
    public List<GlUser> Reviewers { get; set; } = [];
    public List<string> Labels { get; set; } = [];
    public GlMilestone? Milestone { get; set; }
    public GlPipeline? HeadPipeline { get; set; }
    /// <summary>A string in the API ("5", or "5+" when truncated).</summary>
    public string? ChangesCount { get; set; }
    public GlDiffRefs? DiffRefs { get; set; }
    public bool? Squash { get; set; }
    public bool? ForceRemoveSourceBranch { get; set; }
    public bool? ShouldRemoveSourceBranch { get; set; }
    public bool MergeWhenPipelineSucceeds { get; set; }
    public int UserNotesCount { get; set; }
    public GlReferences? References { get; set; }
    public GlMergeRequestUser? User { get; set; }

    public bool IsOpen => State == "opened";
}

public sealed class GlMergeRequestUser
{
    public bool CanMerge { get; set; }
}

public sealed class GlApprovedBy
{
    public GlUser? User { get; set; }
}

/// <summary><c>GET …/merge_requests/:iid/approvals</c> — Free tier fields only.</summary>
public sealed class GlApprovals
{
    public bool Approved { get; set; }
    public int? ApprovalsRequired { get; set; }
    public int? ApprovalsLeft { get; set; }
    public List<GlApprovedBy> ApprovedBy { get; set; } = [];
    public bool? UserHasApproved { get; set; }
    public bool? UserCanApprove { get; set; }

    public int ApprovedCount => ApprovedBy.Count;
}

public sealed class GlCommitStatus
{
    public long Id { get; set; }
    public string? Sha { get; set; }
    public string? Ref { get; set; }
    /// <summary><c>pending · running · success · failed · canceled · skipped</c>.</summary>
    public string Status { get; set; } = "";
    public string? Name { get; set; }
    public string? TargetUrl { get; set; }
    public string? Description { get; set; }
    public long? PipelineId { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public GlUser? Author { get; set; }
}

public sealed class GlIssue
{
    public long Id { get; set; }
    public long Iid { get; set; }
    public long ProjectId { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    /// <summary><c>opened · closed</c>.</summary>
    public string State { get; set; } = "";
    public List<string> Labels { get; set; } = [];
    public string? WebUrl { get; set; }
    public GlUser? Author { get; set; }
    public List<GlUser> Assignees { get; set; } = [];
    public GlMilestone? Milestone { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public GlReferences? References { get; set; }
    public int? MergeRequestsCount { get; set; }
}

public sealed class GlProjectRef
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string? PathWithNamespace { get; set; }
    public string? WebUrl { get; set; }
}

/// <summary>The MR or issue a todo points at; both carry these four.</summary>
public sealed class GlTodoTarget
{
    public long? Id { get; set; }
    public long? Iid { get; set; }
    public string? Title { get; set; }
    public string? State { get; set; }
    public string? WebUrl { get; set; }
}

public sealed class GlTodo
{
    public long Id { get; set; }
    public GlProjectRef? Project { get; set; }
    public GlUser? Author { get; set; }
    /// <summary><c>assigned · mentioned · directly_addressed · review_requested ·
    /// approval_required · build_failed · unmergeable · marked · …</c></summary>
    public string ActionName { get; set; } = "";
    /// <summary><c>MergeRequest</c> or <c>Issue</c> (also epics, designs, alerts).</summary>
    public string? TargetType { get; set; }
    public GlTodoTarget? Target { get; set; }
    public string? TargetUrl { get; set; }
    public string? Body { get; set; }
    /// <summary><c>pending</c> or <c>done</c>.</summary>
    public string State { get; set; } = "";
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

public sealed class GlLinePosition
{
    /// <summary><c>&lt;sha1 of file path&gt;_&lt;old_line&gt;_&lt;new_line&gt;</c>.</summary>
    public string? LineCode { get; set; }
    /// <summary><c>new</c> for added lines, otherwise <c>old</c>.</summary>
    public string? Type { get; set; }
    public int? OldLine { get; set; }
    public int? NewLine { get; set; }
}

public sealed class GlLineRange
{
    public GlLinePosition? Start { get; set; }
    public GlLinePosition? End { get; set; }
}

/// <summary>Where a diff note anchors. The three SHAs must equal the MR's
/// <see cref="GlDiffRefs"/> for GitLab to accept a new note.</summary>
public sealed class GlPosition
{
    public string? BaseSha { get; set; }
    public string? HeadSha { get; set; }
    public string? StartSha { get; set; }
    public string? OldPath { get; set; }
    public string? NewPath { get; set; }
    /// <summary><c>text</c> or <c>image</c>.</summary>
    public string? PositionType { get; set; }
    public int? OldLine { get; set; }
    public int? NewLine { get; set; }
    public GlLineRange? LineRange { get; set; }
}

public sealed class GlNote
{
    public long Id { get; set; }
    /// <summary><c>DiffNote</c>, <c>DiscussionNote</c>, or null for a plain note.</summary>
    public string? Type { get; set; }
    public string Body { get; set; } = "";
    public GlUser? Author { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    /// <summary>Written by GitLab itself ("added 2 commits", "changed the description").</summary>
    public bool System { get; set; }
    public bool Resolvable { get; set; }
    public bool? Resolved { get; set; }
    public GlUser? ResolvedBy { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public GlPosition? Position { get; set; }
    public long? NoteableId { get; set; }
    public long? NoteableIid { get; set; }
    public string? NoteableType { get; set; }
}

public sealed class GlDiscussion
{
    public string Id { get; set; } = "";
    /// <summary>A lone comment rather than a thread; cannot be resolved.</summary>
    public bool IndividualNote { get; set; }
    public List<GlNote> Notes { get; set; } = [];

    public GlNote? First => Notes.Count > 0 ? Notes[0] : null;
    public bool IsResolved => First?.Resolved == true;
    public GlPosition? Position => First?.Position;
}

/// <summary>A project member (<c>GET /projects/:id/members/all</c>) — the reviewer picker's pool.</summary>
public sealed class GlMember
{
    public long Id { get; set; }
    public string Username { get; set; } = "";
    public string Name { get; set; } = "";
    public string? AvatarUrl { get; set; }
    public string? State { get; set; }
    /// <summary>10 guest · 20 reporter · 30 developer · 40 maintainer · 50 owner.</summary>
    public int AccessLevel { get; set; }
}

/// <summary>A project label (<c>GET /projects/:id/labels</c>).</summary>
public sealed class GlLabel
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string? Color { get; set; }
    public string? TextColor { get; set; }
    public string? Description { get; set; }
}

/// <summary>One file of a merge request's diff (<c>GET …/merge_requests/:iid/diffs</c>).</summary>
public sealed class GlDiff
{
    public string? OldPath { get; set; }
    public string? NewPath { get; set; }
    public bool NewFile { get; set; }
    public bool RenamedFile { get; set; }
    public bool DeletedFile { get; set; }
    public bool? GeneratedFile { get; set; }
    /// <summary>Present unless the diff was collapsed for size.</summary>
    public string? Diff { get; set; }

    public string Path => NewPath ?? OldPath ?? "";
    public char StatusLetter => NewFile ? 'A' : DeletedFile ? 'D' : RenamedFile ? 'R' : 'M';
}

/// <summary>A note written but not yet published — the "Start review" flow.</summary>
public sealed class GlDraftNote
{
    public long Id { get; set; }
    public long? AuthorId { get; set; }
    public long? MergeRequestId { get; set; }
    public bool ResolveDiscussion { get; set; }
    public string? DiscussionId { get; set; }
    public string Note { get; set; } = "";
    public string? CommitId { get; set; }
    public GlPosition? Position { get; set; }
}

/// <summary><c>POST /oauth/token</c>.</summary>
public sealed class GlTokenResponse
{
    public string AccessToken { get; set; } = "";
    public string? TokenType { get; set; }
    public long? ExpiresIn { get; set; }
    public string? RefreshToken { get; set; }
    public string? Scope { get; set; }
    public long? CreatedAt { get; set; }
}

/// <summary>What GitLab puts in an error body: <c>{"message": …}</c> for the API,
/// <c>{"error": …, "error_description": …}</c> for OAuth. <c>message</c> can be an object
/// on validation errors, which is read as raw JSON.</summary>
public sealed class GlErrorBody
{
    public System.Text.Json.JsonElement? Message { get; set; }
    public string? Error { get; set; }
    public string? ErrorDescription { get; set; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    NumberHandling = JsonNumberHandling.AllowReadingFromString)]
[JsonSerializable(typeof(GlUser))]
[JsonSerializable(typeof(GlTokenInfo))]
[JsonSerializable(typeof(GlProject))]
[JsonSerializable(typeof(List<GlProject>))]
[JsonSerializable(typeof(GlProtectedBranch))]
[JsonSerializable(typeof(List<GlProtectedBranch>))]
[JsonSerializable(typeof(GlMergeRequest))]
[JsonSerializable(typeof(List<GlMergeRequest>))]
[JsonSerializable(typeof(GlApprovals))]
[JsonSerializable(typeof(GlPipeline))]
[JsonSerializable(typeof(List<GlPipeline>))]
[JsonSerializable(typeof(GlCommitStatus))]
[JsonSerializable(typeof(List<GlCommitStatus>))]
[JsonSerializable(typeof(GlIssue))]
[JsonSerializable(typeof(List<GlIssue>))]
[JsonSerializable(typeof(GlTodo))]
[JsonSerializable(typeof(List<GlTodo>))]
[JsonSerializable(typeof(GlDiscussion))]
[JsonSerializable(typeof(List<GlDiscussion>))]
[JsonSerializable(typeof(GlNote))]
[JsonSerializable(typeof(GlDiff))]
[JsonSerializable(typeof(List<GlDiff>))]
[JsonSerializable(typeof(GlMember))]
[JsonSerializable(typeof(List<GlMember>))]
[JsonSerializable(typeof(GlLabel))]
[JsonSerializable(typeof(List<GlLabel>))]
[JsonSerializable(typeof(GlDraftNote))]
[JsonSerializable(typeof(List<GlDraftNote>))]
[JsonSerializable(typeof(GlPosition))]
[JsonSerializable(typeof(GlTokenResponse))]
[JsonSerializable(typeof(GlErrorBody))]
internal partial class GitLabJson : JsonSerializerContext;
