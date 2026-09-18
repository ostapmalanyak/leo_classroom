using LeoClassroom.Persistence.Repositories;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Shared;

namespace LeoClassroom.Services.Audit;

public sealed record AuditFilter(
    Instant? From = null,
    Instant? To = null,
    string? Actor = null,
    AuditAction? Action = null,
    string? TargetType = null,
    string? TargetId = null,
    int Skip = 0,
    int Take = 100);

public interface IAuditQueryService
{
    public ValueTask<IReadOnlyCollection<AuditEventView>> QueryAsync(AuditFilter filter);
}

internal sealed class AuditQueryService(IUnitOfWork uow) : IAuditQueryService
{
    private const int MaxTake = 200;

    public async ValueTask<IReadOnlyCollection<AuditEventView>> QueryAsync(AuditFilter filter)
    {
        int take = Math.Clamp(filter.Take, 1, MaxTake);
        int skip = Math.Max(filter.Skip, 0);

        return await uow.AuditEventRepository.QueryAsync(new AuditQuery(
            filter.From, filter.To, filter.Actor, filter.Action, filter.TargetType, filter.TargetId, skip, take));
    }
}
