namespace LeoClassroom.Shared;

public enum Role
{
    Student = 1,
    Teacher = 2,
    Admin = 3
}

public enum UserState
{
    Active = 1,
    SoftDeleted = 2
}

public enum RosterKind
{
    Auto = 1,
    Custom = 2
}

public enum DeadlineKind
{
    None = 1,
    Soft = 2,
    Hard = 3
}

public enum StarterSourceKind
{
    DescriptionOnly = 1,
    Archive = 2,
    ForkOwnRepo = 3,
    CopyRepo = 4
}

public enum CollaboratorPermission
{
    None = 0,
    Read = 1,
    Write = 2,
    Admin = 3
}

public enum SubmissionStatus
{
    Provisioning = 1,
    Ready = 2,
    Failed = 3
}

public enum DownloadSnapshotMode
{
    Deadline = 1,
    Head = 2
}

public enum FeedbackState
{
    None = 1,
    Unread = 2,
    Read = 3
}

public enum NotificationTrigger
{
    NewAssignment = 1,
    DeadlineChanged = 2
}

public enum NotificationStatus
{
    Pending = 1,
    Sent = 2,
    Failed = 3
}

public enum MoodleSyncOpType
{
    Create = 1,
    Update = 2,
    Delete = 3
}

public enum MoodleSyncStatus
{
    Pending = 1,
    Sent = 2,
    Failed = 3
}

public enum DownloadJobStatus
{
    Pending = 1,
    Running = 2,
    Ready = 3,
    Failed = 4
}

public enum AuditAction
{
    AssignmentCreated = 1,
    AssignmentDeleted = 2,
    AssignmentAutoDeleted = 3,
    RepositoryDeleted = 4,
    AcceptanceDeleted = 5,
    CourseTeacherAdded = 6,
    CourseTeacherRemoved = 7,
    AssignmentTeacherAdded = 8,
    AssignmentTeacherRemoved = 9,
    CourseReadOnlyToggled = 10,
    UserSoftDeleted = 11,
    UserReactivated = 12,
    RejectedSoftDeletedLogin = 13,
    RetentionPurge = 14,
    UserHardDeleted = 15,
    CourseDeleted = 16
}
