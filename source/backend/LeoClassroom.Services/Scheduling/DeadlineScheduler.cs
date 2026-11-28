using Quartz;

namespace LeoClassroom.Services.Scheduling;

public interface IDeadlineScheduler
{
    /// <summary>Takes write access away at the deadline itself; replaces an earlier time for the same assignment</summary>
    public ValueTask ScheduleAsync(long assignmentId, Instant deadline);
}

/// <remarks>
///     The triggers live in memory. <see cref="DeadlineReconciliationJob" /> registers them again after a restart.
///     A trigger that outlives its deadline (moved, softened, deleted) is harmless, because reconciliation
///     computes the access from the stored assignment and the clock.
/// </remarks>
internal sealed class DeadlineScheduler(ISchedulerFactory schedulers) : IDeadlineScheduler
{
    // Quartz may fire a few milliseconds early, which would still find the deadline ahead
    private static readonly Duration FireDelay = Duration.FromSeconds(1);

    public async ValueTask ScheduleAsync(long assignmentId, Instant deadline)
    {
        IJobDetail job = JobBuilder.Create<DeadlineReachedJob>()
                                   .WithIdentity($"hard-deadline-{assignmentId}")
                                   .UsingJobData(DeadlineReachedJob.AssignmentIdKey, assignmentId)
                                   .Build();
        ITrigger trigger = TriggerBuilder.Create()
                                         .WithIdentity($"hard-deadline-{assignmentId}")
                                         .ForJob(job)
                                         .StartAt((deadline + FireDelay).ToDateTimeOffset())
                                         .WithSimpleSchedule(s => s.WithMisfireHandlingInstructionFireNow())
                                         .Build();

        IScheduler scheduler = await schedulers.GetScheduler();
        await scheduler.ScheduleJob(job, [trigger], replace: true);
    }
}
