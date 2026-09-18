using LeoClassroom.Services.Moodle;
using LeoClassroom.Persistence.Util;
using Quartz;

namespace LeoClassroom.Services.Scheduling;

/// <remarks>
///     Enqueues outbox rows and nothing else - no call leaves the process here, the worker makes those later.
///     A partially enqueued backfill would sync some assignments to Moodle and silently skip others.
/// </remarks>
[DisallowConcurrentExecution]
internal sealed class MoodleBackfillJob(IMoodleBackfillService service, ITransactionProvider transaction) : IJob
{
    public static readonly JobKey Key = new("moodle-backfill");

    public async Task Execute(IJobExecutionContext context) =>
        await transaction.ExecuteAsync(async () => await service.BackfillAsync());
}
