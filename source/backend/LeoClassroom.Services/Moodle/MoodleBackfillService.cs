using LeoClassroom.Persistence.Model;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Shared;

namespace LeoClassroom.Services.Moodle;

public interface IMoodleBackfillService
{
    public ValueTask BackfillAsync();
}

internal sealed class MoodleBackfillService(IUnitOfWork uow, IClock clock, ILogger<MoodleBackfillService> logger)
    : IMoodleBackfillService
{
    public async ValueTask BackfillAsync()
    {
        IReadOnlyCollection<long> courseIds = await uow.MoodleLinkRepository.GetEnabledCourseIdsAsync();
        int enqueued = 0;

        foreach (long courseId in courseIds)
        {
            IReadOnlyCollection<MoodleAssignmentTarget> targets =
                await uow.MoodleSyncRepository.GetAssignmentTargetsForCourseAsync(courseId);
            IReadOnlyCollection<long> mappedIds =
                await uow.MoodleSyncRepository.GetMappedAssignmentIdsForCourseAsync(courseId);
            HashSet<long> currentIds = targets.Select(t => t.AssignmentId).ToHashSet();

            foreach (MoodleAssignmentTarget target in targets)
            {
                if (await uow.MoodleSyncRepository.ExistsPendingForAssignmentAsync(target.AssignmentId))
                {
                    continue;
                }

                Add(courseId, target.AssignmentId, MoodleSyncOpType.Update, target.Title, target.Deadline);
                enqueued++;
            }

            foreach (long mappedId in mappedIds.Where(id => !currentIds.Contains(id)))
            {
                if (await uow.MoodleSyncRepository.ExistsPendingForAssignmentAsync(mappedId))
                {
                    continue;
                }

                Add(courseId, mappedId, MoodleSyncOpType.Delete, null, null);
                enqueued++;
            }
        }

        if (enqueued > 0)
        {
            await uow.SaveChangesAsync();
            logger.LogInformation("Moodle backfill enqueued {Count} reconcile ops", enqueued);
        }
    }

    private void Add(long courseId, long assignmentId, MoodleSyncOpType opType, string? title, Instant? deadline)
    {
        Instant now = clock.GetCurrentInstant();
        uow.MoodleSyncRepository.Add(new MoodleSyncOp
        {
            CourseId = courseId,
            AssignmentId = assignmentId,
            OpType = opType,
            Title = title,
            Deadline = deadline,
            Status = MoodleSyncStatus.Pending,
            Attempts = 0,
            CreatedAt = now,
            NextAttemptAt = now
        });
    }
}
