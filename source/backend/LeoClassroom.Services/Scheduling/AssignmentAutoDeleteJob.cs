using LeoClassroom.Services;
using Quartz;

namespace LeoClassroom.Services.Scheduling;

/// <remarks>
///     <para>
///         **Deliberately without an explicit transaction across the run.** Deleting an assignment deletes
///         its Forgejo repositories, so each one is a series of remote calls, and the job walks a list of
///         them. Wrapping the list would hold one transaction open across every repository deletion.
///     </para>
///     <para>
///         Each assignment is its own unit instead: <c>DeleteOneAsync</c> saves and audits one deletion, and
///         a failure part-way leaves the assignments already handled deleted and the rest for tomorrow, which
///         is the correct outcome. Rolling those back would mean re-deleting repositories that are already
///         gone.
///     </para>
/// </remarks>
[DisallowConcurrentExecution]
internal sealed class AssignmentAutoDeleteJob(IAssignmentAutoDeleteService service, ILogger<AssignmentAutoDeleteJob> logger)
    : IJob
{
    public static readonly JobKey Key = new("assignment-auto-delete");

    public async Task Execute(IJobExecutionContext context)
    {
        int deleted = await service.RunAsync();
        if (deleted > 0)
        {
            logger.LogInformation("Auto-deleted {Count} assignments", deleted);
        }
    }
}
