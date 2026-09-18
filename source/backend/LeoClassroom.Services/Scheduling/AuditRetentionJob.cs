using LeoClassroom.Services.Audit;
using LeoClassroom.Persistence.Util;
using Quartz;

namespace LeoClassroom.Services.Scheduling;

/// <remarks>
///     Purges rows and then records that it did so. One transaction, because an audit log that lost its own
///     purge record would misrepresent its completeness - the one thing an audit log must not do.
/// </remarks>
[DisallowConcurrentExecution]
internal sealed class AuditRetentionJob(IAuditRetentionService service, ITransactionProvider transaction) : IJob
{
    public static readonly JobKey Key = new("audit-retention");

    public async Task Execute(IJobExecutionContext context) =>
        await transaction.ExecuteAsync(async () => await service.PurgeAsync());
}
