namespace LeoClassroom.Persistence.Model;

public class WebhookEvent
{
    public long Id { get; set; }
    public required string EventType { get; set; }
    public string? Actor { get; set; }
    public string? RepoOwner { get; set; }
    public string? RepoName { get; set; }
    public Instant ReceivedAt { get; set; }
    public Instant? CommitterDate { get; set; }
    public required string Payload { get; set; }
}
