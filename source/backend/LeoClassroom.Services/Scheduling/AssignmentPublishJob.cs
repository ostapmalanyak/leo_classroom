using Quartz;

namespace LeoClassroom.Services.Scheduling;

/// <remarks>
///     Each assignment is its own unit, like <see cref="AssignmentAutoDeleteJob" />: a failure leaves the others
///     published and the failed one for the next run, and re-running is harmless because the e-mail and Moodle
///     operation are idempotent per assignment.
/// </remarks>
[DisallowConcurrentExecution]
internal sealed class AssignmentPublishJob(IAssignmentPublishService service, ILogger<AssignmentPublishJob> logger) : IJob
{
    public static readonly JobKey Key = new("assignment-publish");

    public async Task Execute(IJobExecutionContext context)
    {
        int published = await service.RunAsync();
        if (published > 0)
        {
            logger.LogInformation("Published {Count} scheduled assignments", published);
        }
    }
}
