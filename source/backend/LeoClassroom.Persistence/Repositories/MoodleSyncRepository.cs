using LeoClassroom.Persistence.Model;
using LeoClassroom.Shared;
using Microsoft.EntityFrameworkCore;

namespace LeoClassroom.Persistence.Repositories;

public interface IMoodleSyncRepository
{
    public void Add(MoodleSyncOp op);
    public ValueTask<IReadOnlyCollection<MoodleSyncOp>> GetSendableAsync(Instant now, int batchSize);
    public ValueTask<MoodleItemMapping?> GetMappingByAssignmentAsync(long assignmentId);
    public void AddMapping(MoodleItemMapping mapping);
    public void RemoveMapping(MoodleItemMapping mapping);
    public ValueTask<bool> ExistsPendingForAssignmentAsync(long assignmentId);
    public ValueTask<IReadOnlyCollection<MoodleAssignmentTarget>> GetAssignmentTargetsForCourseAsync(long courseId);
    public ValueTask<IReadOnlyCollection<long>> GetMappedAssignmentIdsForCourseAsync(long courseId);
}

internal sealed class MoodleSyncRepository(
    DbSet<MoodleSyncOp> ops,
    DbSet<MoodleItemMapping> mappings,
    DbSet<Assignment> assignments) : IMoodleSyncRepository
{
    public void Add(MoodleSyncOp op) => ops.Add(op);

    public async ValueTask<IReadOnlyCollection<MoodleSyncOp>> GetSendableAsync(Instant now, int batchSize)
    {
        List<MoodleSyncOp> rows = await ops
            .Where(o => o.Status == MoodleSyncStatus.Pending && o.NextAttemptAt <= now)
            .OrderBy(o => o.NextAttemptAt)
            .Take(batchSize)
            .ToListAsync();

        return rows.AsReadOnly();
    }

    public async ValueTask<MoodleItemMapping?> GetMappingByAssignmentAsync(long assignmentId) =>
        await mappings.FirstOrDefaultAsync(m => m.AssignmentId == assignmentId);

    public void AddMapping(MoodleItemMapping mapping) => mappings.Add(mapping);

    public void RemoveMapping(MoodleItemMapping mapping) => mappings.Remove(mapping);

    public async ValueTask<bool> ExistsPendingForAssignmentAsync(long assignmentId) =>
        await ops.AsNoTracking()
                 .AnyAsync(o => o.AssignmentId == assignmentId && o.Status == MoodleSyncStatus.Pending);

    public async ValueTask<IReadOnlyCollection<MoodleAssignmentTarget>> GetAssignmentTargetsForCourseAsync(long courseId)
    {
        List<MoodleAssignmentTarget> targets = await assignments.AsNoTracking()
            .Where(a => a.CourseId == courseId)
            .Select(a => new MoodleAssignmentTarget(a.Id, a.Title, a.Deadline))
            .ToListAsync();

        return targets.AsReadOnly();
    }

    public async ValueTask<IReadOnlyCollection<long>> GetMappedAssignmentIdsForCourseAsync(long courseId)
    {
        List<long> ids = await mappings.AsNoTracking()
                                       .Where(m => m.CourseId == courseId)
                                       .Select(m => m.AssignmentId)
                                       .ToListAsync();

        return ids.AsReadOnly();
    }
}
