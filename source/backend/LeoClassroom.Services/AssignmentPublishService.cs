using LeoClassroom.Services.Moodle;
using LeoClassroom.Services.Notifications;
using LeoClassroom.Persistence.Model;
using LeoClassroom.Persistence.Util;

namespace LeoClassroom.Services;

public interface IAssignmentPublishService
{
    public ValueTask<int> RunAsync();
}

/// <summary>Publishes assignments whose <c>PublishAt</c> has passed; the scheduled twin of the publish endpoint</summary>
internal sealed class AssignmentPublishService(
    IUnitOfWork uow,
    INotificationService notifications,
    IMoodleSyncService moodle,
    IClock clock,
    ILogger<AssignmentPublishService> logger) : IAssignmentPublishService
{
    public async ValueTask<int> RunAsync()
    {
        IReadOnlyCollection<long> due = await uow.AssignmentRepository.GetDueForPublishAsync(clock.GetCurrentInstant());

        int published = 0;
        foreach (long id in due)
        {
            try
            {
                Assignment? assignment = await uow.AssignmentRepository.GetTrackedWithTeachersAsync(id);
                if (assignment is null || assignment.IsPublished)
                {
                    continue;
                }

                assignment.IsPublished = true;
                assignment.PublishAt = null;
                await uow.SaveChangesAsync();
                await notifications.EnqueueAssignmentCreatedAsync(assignment);
                await moodle.EnqueueCreateAsync(assignment);
                published++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Scheduled publish of assignment {Id} failed", id);
            }
        }

        return published;
    }
}
