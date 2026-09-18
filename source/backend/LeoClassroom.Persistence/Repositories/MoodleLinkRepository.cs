using LeoClassroom.Persistence.Model;
using Microsoft.EntityFrameworkCore;

namespace LeoClassroom.Persistence.Repositories;

public interface IMoodleLinkRepository
{
    public void Add(MoodleLink link);
    public ValueTask<MoodleLink?> GetByCourseIdAsync(long courseId);
    public ValueTask<MoodleLink?> GetTrackedByCourseIdAsync(long courseId);
    public ValueTask<IReadOnlyCollection<long>> GetEnabledCourseIdsAsync();
}

internal sealed class MoodleLinkRepository(DbSet<MoodleLink> links) : IMoodleLinkRepository
{
    public void Add(MoodleLink link) => links.Add(link);

    public async ValueTask<MoodleLink?> GetByCourseIdAsync(long courseId) =>
        await links.AsNoTracking().FirstOrDefaultAsync(l => l.CourseId == courseId);

    public async ValueTask<MoodleLink?> GetTrackedByCourseIdAsync(long courseId) =>
        await links.FirstOrDefaultAsync(l => l.CourseId == courseId);

    public async ValueTask<IReadOnlyCollection<long>> GetEnabledCourseIdsAsync()
    {
        List<long> ids = await links.AsNoTracking()
                                    .Where(l => l.Enabled && l.TokenCipher != null)
                                    .Select(l => l.CourseId)
                                    .ToListAsync();

        return ids.AsReadOnly();
    }
}
