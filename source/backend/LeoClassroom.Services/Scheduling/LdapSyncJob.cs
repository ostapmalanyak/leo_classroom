using LeoClassroom.Services.Ldap;
using LeoClassroom.Persistence.Util;
using Quartz;

namespace LeoClassroom.Services.Scheduling;

/// <remarks>
///     The directory is read first and the database written afterwards, so the whole reconcile is one
///     transaction: users created, users soft-deleted, users reactivated and every automatic roster's
///     membership either all land or none do. Half of a sync is worse than none - it would leave classes
///     holding the members of two different runs.
/// </remarks>
[DisallowConcurrentExecution]
internal sealed class LdapSyncJob(ILdapSyncService sync, ITransactionProvider transaction,
                                  ILogger<LdapSyncJob> logger) : IJob
{
    public static readonly JobKey Key = new("ldap-sync");

    public async Task Execute(IJobExecutionContext context)
    {
        SyncOutcome outcome = await transaction.ExecuteAsync(async () => await sync.RunAsync());
        logger.LogInformation(
            "LDAP sync finished: created={Created}, updated={Updated}, softDeleted={SoftDeleted}, " +
            "reactivated={Reactivated}, destructiveSkipped={Skipped}",
            outcome.Created, outcome.Updated, outcome.SoftDeleted, outcome.Reactivated, outcome.DestructivePassSkipped);
    }
}
