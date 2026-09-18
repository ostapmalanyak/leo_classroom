using System.Text.Json;
using LeoClassroom.Services.Auth;
using LeoClassroom.Persistence.Model;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Shared;

namespace LeoClassroom.Services.Audit;

public interface IAuditLog
{
    public ValueTask RecordAsync(AuditAction action, string targetType, string? targetId,
                                 IReadOnlyDictionary<string, string>? metadata = null);
}

public static class AuditActor
{
    public const string System = "system";
}

internal sealed class AuditLog(
    IUnitOfWork uow, ICurrentUser currentUser, IClock clock, ILogger<AuditLog> logger) : IAuditLog
{
    public async ValueTask RecordAsync(AuditAction action, string targetType, string? targetId,
                                       IReadOnlyDictionary<string, string>? metadata = null)
    {
        (string actor, string roles) = ResolveActor();
        string metadataJson = JsonSerializer.Serialize(metadata ?? new Dictionary<string, string>());

        uow.AuditEventRepository.Add(new AuditEvent
        {
            At = clock.GetCurrentInstant(),
            ActorStudentId = actor,
            ActorRoles = roles,
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            Metadata = metadataJson
        });
        await uow.SaveChangesAsync();

        logger.LogInformation("Audit: {Action} on {TargetType}/{TargetId} by {Actor} [{Roles}] {Metadata}",
                              action, targetType, targetId, actor, roles, metadataJson);
    }

    private (string Actor, string Roles) ResolveActor()
    {
        if (!currentUser.IsAuthenticated || string.IsNullOrEmpty(currentUser.StudentId))
        {
            return (AuditActor.System, AuditActor.System);
        }

        return (currentUser.StudentId, string.Join(",", currentUser.Roles.Select(r => r.ToString())));
    }
}
