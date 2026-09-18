using LeoClassroom.Shared;

namespace LeoClassroom.Persistence.Model;

public class MoodleSyncOp
{
    public long Id { get; set; }
    public long CourseId { get; set; }
    public long AssignmentId { get; set; }
    public MoodleSyncOpType OpType { get; set; }
    public string? Title { get; set; }
    public Instant? Deadline { get; set; }
    public MoodleSyncStatus Status { get; set; }
    public int Attempts { get; set; }
    public Instant CreatedAt { get; set; }
    public Instant NextAttemptAt { get; set; }
    public string? LastError { get; set; }
}

public class MoodleItemMapping
{
    public long Id { get; set; }
    public long AssignmentId { get; set; }
    public long CourseId { get; set; }
    public long MoodleItemId { get; set; }
}
