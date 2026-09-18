using LeoClassroom.Persistence.Model;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Shared;

namespace LeoClassroom.Services.Moodle;

public interface IMoodleSyncService
{
    public ValueTask EnqueueCreateAsync(Assignment assignment);
    public ValueTask EnqueueUpdateAsync(Assignment assignment);
    public ValueTask EnqueueDeleteAsync(long assignmentId, long courseId);
}

internal sealed class MoodleSyncService(IUnitOfWork uow, IClock clock, ILogger<MoodleSyncService> logger)
    : IMoodleSyncService
{
    public ValueTask EnqueueCreateAsync(Assignment assignment) =>
        EnqueueAsync(assignment.CourseId, assignment.Id, MoodleSyncOpType.Create, assignment.Title, assignment.Deadline);

    public ValueTask EnqueueUpdateAsync(Assignment assignment) =>
        EnqueueAsync(assignment.CourseId, assignment.Id, MoodleSyncOpType.Update, assignment.Title, assignment.Deadline);

    public ValueTask EnqueueDeleteAsync(long assignmentId, long courseId) =>
        EnqueueAsync(courseId, assignmentId, MoodleSyncOpType.Delete, null, null);

    private async ValueTask EnqueueAsync(
        long courseId, long assignmentId, MoodleSyncOpType opType, string? title, Instant? deadline)
    {
        MoodleLink? link = await uow.MoodleLinkRepository.GetByCourseIdAsync(courseId);
        if (link is null || !link.Enabled || link.TokenCipher is null)
        {
            return;
        }

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
        await uow.SaveChangesAsync();
        logger.LogInformation("Queued Moodle {OpType} for assignment {AssignmentId}", opType, assignmentId);
    }
}
