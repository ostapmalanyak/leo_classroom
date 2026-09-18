using LeoClassroom.Shared;

namespace LeoClassroom.Persistence.Model;

public class AuditEvent
{
    public long Id { get; set; }
    public Instant At { get; set; }
    public required string ActorStudentId { get; set; }
    public required string ActorRoles { get; set; }
    public AuditAction Action { get; set; }
    public required string TargetType { get; set; }
    public string? TargetId { get; set; }
    public required string Metadata { get; set; }
}
