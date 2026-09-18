namespace LeoClassroom.Persistence.Model;

public class NotificationPreference
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public User User { get; set; } = null!;
    public bool NewAssignment { get; set; }
    public bool DeadlineChanged { get; set; }
}
