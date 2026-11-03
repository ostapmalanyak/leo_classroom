namespace LeoClassroom.Shared;

public sealed record CourseOverview(
    long Id,
    string Title,
    long RosterId,
    string RosterName,
    int MemberCount,
    bool IsReadOnly,
    bool StudentsRetainAccess);

public sealed record RosterOverview(
    long Id,
    string Name,
    RosterKind Kind,
    int MemberCount,
    long? OwnerId);

public sealed record UserSummary(
    long Id,
    string StudentId,
    string FirstName,
    string LastName,
    string? Class,
    Role Role);

public sealed record StudentSubmission(
    long AcceptanceId,
    long StudentId,
    string StudentNumber,
    string FirstName,
    string LastName,
    SubmissionStatus Status,
    Instant? LastCommitAt,
    Instant? LastPushAt,
    string? RepoUrl,
    bool Late,
    Instant? LateSince,
    FeedbackState FeedbackState,
    Instant? FeedbackReadAt,
    long? FeedbackPrNumber);

public sealed record CommitAnalyticsView(
    int PushCount,
    int CommitCount,
    double CommitsPerPush,
    Instant? FirstPushAt,
    Instant? LastPushAt,
    int ActiveDayCount,
    IReadOnlyCollection<CommitAnalyticsEntry> Commits);

public sealed record CommitAnalyticsEntry(string Sha, Instant At, bool Late);

public sealed record AuditEventView(
    long Id,
    Instant At,
    string ActorStudentId,
    string ActorRoles,
    AuditAction Action,
    string TargetType,
    string? TargetId,
    string Metadata);

public sealed record NotificationRecipient(long UserId, string Email);

public sealed record NotificationPreferenceView(bool NewAssignment, bool DeadlineChanged);

public sealed record MoodleAssignmentTarget(long AssignmentId, string Title, Instant? Deadline);

public sealed record MoodleLinkView(bool Enabled, string MoodleBaseUrl, long MoodleCourseId, bool HasToken);

public sealed record AssignedAssignment(
    long Id,
    long CourseId,
    string CourseTitle,
    string Title,
    Instant? Deadline,
    DeadlineKind DeadlineKind,
    bool Accepted,
    SubmissionStatus? Status);
