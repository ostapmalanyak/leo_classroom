using LeoClassroom.Persistence.Model;
using LeoClassroom.Shared;
using Microsoft.EntityFrameworkCore;

namespace LeoClassroom.Persistence.Repositories;

public interface ICourseRepository
{
    public void Add(Course course);
    public ValueTask<Course?> GetByIdAsync(long id);
    public ValueTask<Course?> GetTrackedByIdAsync(long id);
    public ValueTask<IReadOnlyCollection<CourseOverview>> GetOverviewsAsync();
    public ValueTask<IReadOnlyCollection<CourseOverview>> GetOverviewsForTeacherAsync(long userId);
    public ValueTask<bool> ExistsWithTitleInRosterAsync(string title, long rosterId, long? excludingCourseId = null);
    public ValueTask<string?> GetOwnerStudentIdAsync(long id);
    public ValueTask<bool> DeleteByIdAsync(long id);
    public ValueTask<Course?> GetTrackedWithTeachersAsync(long id);
    public ValueTask<Course?> GetWithTeachersAsync(long id);
    public ValueTask<Course?> GetTrackedWithTeachersAndMembersAsync(long id);
    public ValueTask<Course?> GetWithTeachersAndMembersAsync(long id);
    public ValueTask<IReadOnlyCollection<long>> GetIdsOwnedByAsync(long userId);
    public ValueTask<IReadOnlyCollection<long>> GetIdsUsingRostersAsync(IReadOnlyCollection<long> rosterIds);
    public ValueTask<IReadOnlyCollection<string>> GetForgejoOrgsAsync(IReadOnlyCollection<long> ids);
    public ValueTask<int> DeleteByIdsAsync(IReadOnlyCollection<long> ids);
    public ValueTask<IReadOnlyCollection<string>> GetCoTaughtForgejoOrgsAsync(long userId);
}

internal sealed class CourseRepository(DbSet<Course> courses) : ICourseRepository
{
    public void Add(Course course) => courses.Add(course);

    public async ValueTask<Course?> GetByIdAsync(long id) =>
        await courses.AsNoTracking()
                     .Include(c => c.Roster)
                     .FirstOrDefaultAsync(c => c.Id == id);

    public async ValueTask<Course?> GetTrackedByIdAsync(long id) =>
        await courses.Include(c => c.Owner).FirstOrDefaultAsync(c => c.Id == id);

    public async ValueTask<Course?> GetTrackedWithTeachersAsync(long id) =>
        await courses.Include(c => c.Owner)
                     .Include(c => c.CoTeachers)
                     .FirstOrDefaultAsync(c => c.Id == id);

    public async ValueTask<Course?> GetWithTeachersAsync(long id) =>
        await courses.AsNoTracking()
                     .Include(c => c.Owner)
                     .Include(c => c.CoTeachers)
                     .FirstOrDefaultAsync(c => c.Id == id);

    public async ValueTask<Course?> GetTrackedWithTeachersAndMembersAsync(long id) =>
        await courses.AsSplitQuery()
                     .Include(c => c.Owner)
                     .Include(c => c.CoTeachers)
                     .Include(c => c.Members)
                     .FirstOrDefaultAsync(c => c.Id == id);

    public async ValueTask<Course?> GetWithTeachersAndMembersAsync(long id) =>
        await courses.AsNoTracking()
                     .AsSplitQuery()
                     .Include(c => c.Owner)
                     .Include(c => c.CoTeachers)
                     .Include(c => c.Members)
                     .FirstOrDefaultAsync(c => c.Id == id);

    public async ValueTask<IReadOnlyCollection<CourseOverview>> GetOverviewsAsync()
    {
        return await ProjectOverviews(courses.AsNoTracking());
    }

    public async ValueTask<IReadOnlyCollection<CourseOverview>> GetOverviewsForTeacherAsync(long userId)
    {
        IQueryable<Course> visibleCourses = courses.AsNoTracking()
                                                   .Where(c => c.OwnerId == userId
                                                               || c.CoTeachers.Any(t => t.Id == userId));

        return await ProjectOverviews(visibleCourses);
    }

    private static async ValueTask<IReadOnlyCollection<CourseOverview>> ProjectOverviews(IQueryable<Course> query)
    {
        List<CourseOverview> overviews = await query.OrderBy(c => c.Title)
                                                    .Select(c => new CourseOverview(c.Id, c.Title, c.IconSeed, c.RosterId,
                                                                c.Roster.Name, c.Members.Count, c.IsReadOnly,
                                                                c.StudentsRetainAccess))
                                                    .ToListAsync();

        return overviews.AsReadOnly();
    }

    public async ValueTask<bool> ExistsWithTitleInRosterAsync(string title, long rosterId, long? excludingCourseId = null)
    {
        IQueryable<Course> query = courses.AsNoTracking()
                                          .Where(c => c.RosterId == rosterId && c.Title == title);
        if (excludingCourseId is not null)
        {
            query = query.Where(c => c.Id != excludingCourseId.Value);
        }

        return await query.AnyAsync();
    }

    public async ValueTask<string?> GetOwnerStudentIdAsync(long id) =>
        await courses.AsNoTracking()
                     .Where(c => c.Id == id)
                     .Select(c => c.Owner.StudentId)
                     .FirstOrDefaultAsync();

    public async ValueTask<IReadOnlyCollection<long>> GetIdsOwnedByAsync(long userId)
    {
        List<long> ids = await courses.AsNoTracking().Where(c => c.OwnerId == userId).Select(c => c.Id).ToListAsync();

        return ids.AsReadOnly();
    }

    public async ValueTask<IReadOnlyCollection<long>> GetIdsUsingRostersAsync(IReadOnlyCollection<long> rosterIds)
    {
        List<long> ids = await courses.AsNoTracking()
                                      .Where(c => rosterIds.Contains(c.RosterId))
                                      .Select(c => c.Id)
                                      .ToListAsync();

        return ids.AsReadOnly();
    }

    public async ValueTask<IReadOnlyCollection<string>> GetForgejoOrgsAsync(IReadOnlyCollection<long> ids)
    {
        List<string> orgs = await courses.AsNoTracking()
                                         .Where(c => ids.Contains(c.Id) && c.ForgejoOrg != null)
                                         .Select(c => c.ForgejoOrg!)
                                         .ToListAsync();

        return orgs.AsReadOnly();
    }

    /// <summary>
    ///     The Forgejo organisations of the courses this user co-teaches without owning them
    /// </summary>
    /// <remarks>
    ///     Those courses survive the user, so their teachers team has to have the user removed from it rather than
    ///     being torn down.
    /// </remarks>
    public async ValueTask<IReadOnlyCollection<string>> GetCoTaughtForgejoOrgsAsync(long userId)
    {
        List<string> orgs = await courses.AsNoTracking()
                                         .Where(c => c.OwnerId != userId
                                                     && c.ForgejoOrg != null
                                                     && c.CoTeachers.Any(t => t.Id == userId))
                                         .Select(c => c.ForgejoOrg!)
                                         .ToListAsync();

        return orgs.AsReadOnly();
    }

    public async ValueTask<int> DeleteByIdsAsync(IReadOnlyCollection<long> ids) =>
        ids.Count == 0 ? 0 : await courses.Where(c => ids.Contains(c.Id)).ExecuteDeleteAsync();

    public async ValueTask<bool> DeleteByIdAsync(long id)
    {
        int affected = await courses.Where(c => c.Id == id).ExecuteDeleteAsync();

        return affected == 1;
    }
}
