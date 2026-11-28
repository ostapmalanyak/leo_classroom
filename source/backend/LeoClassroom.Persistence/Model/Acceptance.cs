using LeoClassroom.Shared;

namespace LeoClassroom.Persistence.Model;

public class Acceptance
{
    public long Id { get; set; }
    public long AssignmentId { get; set; }
    public Assignment Assignment { get; set; } = null!;
    public long StudentId { get; set; }
    public User Student { get; set; } = null!;
    public required string RepoOwner { get; set; }
    public required string RepoName { get; set; }
    public Instant AcceptedAt { get; set; }
    public SubmissionStatus Status { get; set; }
    public string? FailureReason { get; set; }
    public string? RepoUrl { get; set; }
    public Instant? LastPushAt { get; set; }
    public Instant? LastCommitAt { get; set; }
    public FeedbackState FeedbackState { get; set; }
    public Instant? FeedbackReadAt { get; set; }
    public long? FeedbackPrNumber { get; set; }
    public CommitAnalytics Analytics { get; set; } = new();

    /// <summary>
    ///     Worked out on every read, never stored, so it follows a moved deadline. Only a soft deadline can be
    ///     late. <c>AssignmentRepository</c> repeats this rule inside its queries.
    /// </summary>
    public bool IsLate(Assignment assignment) =>
        assignment.DeadlineKind == DeadlineKind.Soft && assignment.Deadline is { } deadline && LastPushAt >= deadline;
}

public class CommitAnalytics
{
    public int PushCount { get; set; }
    public int CommitCount { get; set; }
    public Instant? FirstPushAt { get; set; }
    public Instant? LastPushAt { get; set; }
    public int ActiveDayCount { get; set; }
    public string? LastPushHeadSha { get; set; }
}
