using LeoClassroom.Services.Forgejo;
using Quartz;

namespace LeoClassroom.Services.Scheduling;

/// <remarks>
///     <para>
///         **Deliberately without an explicit transaction**, unlike most jobs here. Reconciliation is mostly
///         Forgejo calls - team membership, collaborator permissions, one round trip per assignment and per
///         student - with small database writes between them. A transaction would hold a connection and its
///         row locks open across every one of those calls, so a slow or unreachable Forgejo would block
///         unrelated requests for as long as it took to time out.
///     </para>
///     <para>
///         Safe because the work is idempotent: it computes the desired state and applies the difference, so
///         a run that dies halfway is corrected by the next one five minutes later.
///     </para>
/// </remarks>
[DisallowConcurrentExecution]
internal sealed class DeadlineReconciliationJob(IReconciliationService reconciliation) : IJob
{
    public static readonly JobKey Key = new("deadline-reconciliation");

    public async Task Execute(IJobExecutionContext context) =>
        await reconciliation.ReconcileAllWithHardDeadlinesAsync();
}
