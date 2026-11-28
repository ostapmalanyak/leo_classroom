using LeoClassroom.Services.Forgejo;
using OneOf;
using OneOf.Types;
using Quartz;

namespace LeoClassroom.Services.Scheduling;

/// <remarks>
///     No explicit transaction, for the reason given on <see cref="DeadlineReconciliationJob" />, which also
///     retries a run that fails here.
/// </remarks>
[DisallowConcurrentExecution]
internal sealed class DeadlineReachedJob(IReconciliationService reconciliation, ILogger<DeadlineReachedJob> logger)
    : IJob
{
    public const string AssignmentIdKey = "assignmentId";

    public async Task Execute(IJobExecutionContext context)
    {
        long assignmentId = context.MergedJobDataMap.GetLong(AssignmentIdKey);
        OneOf<Success, NotFound, ForgejoError> result = await reconciliation.ReconcileAsync(assignmentId);
        result.Switch(
            success => { },
            notFound => { },
            error => logger.LogWarning("Reconcile at the deadline of assignment {AssignmentId} failed: {Reason}",
                                       assignmentId, error.Reason));
    }
}
