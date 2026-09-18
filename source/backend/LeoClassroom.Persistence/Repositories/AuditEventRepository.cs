using LeoClassroom.Persistence.Model;
using LeoClassroom.Shared;
using Microsoft.EntityFrameworkCore;

namespace LeoClassroom.Persistence.Repositories;

public sealed record AuditQuery(
    Instant? From,
    Instant? To,
    string? Actor,
    AuditAction? Action,
    string? TargetType,
    string? TargetId,
    int Skip,
    int Take);

public interface IAuditEventRepository
{
    public void Add(AuditEvent auditEvent);
    public ValueTask<IReadOnlyCollection<AuditEventView>> QueryAsync(AuditQuery query);
    public ValueTask<int> PurgeOlderThanAsync(Instant cutoff);
}

internal sealed class AuditEventRepository(DbSet<AuditEvent> auditEvents) : IAuditEventRepository
{
    public void Add(AuditEvent auditEvent) => auditEvents.Add(auditEvent);

    public async ValueTask<IReadOnlyCollection<AuditEventView>> QueryAsync(AuditQuery query)
    {
        IQueryable<AuditEvent> q = auditEvents.AsNoTracking();
        if (query.From is not null)
        {
            q = q.Where(e => e.At >= query.From.Value);
        }
        if (query.To is not null)
        {
            q = q.Where(e => e.At <= query.To.Value);
        }
        if (!string.IsNullOrWhiteSpace(query.Actor))
        {
            q = q.Where(e => e.ActorStudentId == query.Actor);
        }
        if (query.Action is not null)
        {
            q = q.Where(e => e.Action == query.Action.Value);
        }
        if (!string.IsNullOrWhiteSpace(query.TargetType))
        {
            q = q.Where(e => e.TargetType == query.TargetType);
        }
        if (!string.IsNullOrWhiteSpace(query.TargetId))
        {
            q = q.Where(e => e.TargetId == query.TargetId);
        }

        List<AuditEventView> results = await q.OrderByDescending(e => e.At)
            .Skip(query.Skip)
            .Take(query.Take)
            .Select(e => new AuditEventView(e.Id, e.At, e.ActorStudentId, e.ActorRoles, e.Action,
                        e.TargetType, e.TargetId, e.Metadata))
            .ToListAsync();

        return results.AsReadOnly();
    }

    public async ValueTask<int> PurgeOlderThanAsync(Instant cutoff) =>
        await auditEvents.Where(e => e.At < cutoff).ExecuteDeleteAsync();
}
