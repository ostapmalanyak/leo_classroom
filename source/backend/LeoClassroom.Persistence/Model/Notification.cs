using LeoClassroom.Shared;

namespace LeoClassroom.Persistence.Model;

public class Notification
{
    public long Id { get; set; }
    public required string EventKey { get; set; }
    public long RecipientUserId { get; set; }
    public User Recipient { get; set; } = null!;
    public required string RecipientEmail { get; set; }
    public NotificationTrigger Trigger { get; set; }
    public long AssignmentId { get; set; }
    public required string AssignmentTitle { get; set; }
    public Instant? Deadline { get; set; }
    public required string Language { get; set; }
    public NotificationStatus Status { get; set; }
    public int Attempts { get; set; }
    public Instant CreatedAt { get; set; }
    public Instant NextAttemptAt { get; set; }
    public Instant? SentAt { get; set; }
    public string? LastError { get; set; }
}
