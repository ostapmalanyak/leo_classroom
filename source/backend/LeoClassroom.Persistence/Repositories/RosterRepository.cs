using LeoClassroom.Persistence.Model;
using LeoClassroom.Shared;
using Microsoft.EntityFrameworkCore;

namespace LeoClassroom.Persistence.Repositories;

public interface IRosterRepository
{
    public ValueTask<bool> ExistsAsync(long id);
    public ValueTask<Roster?> GetByIdAsync(long id);
    public void Add(Roster roster);
    public ValueTask<IReadOnlyCollection<Roster>> GetAllTrackedAutoRostersAsync();
    public ValueTask<Roster?> GetTrackedAutoRosterByClassAsync(string classKey);
    public ValueTask<Roster?> GetTrackedWithMembersAsync(long id);
    public ValueTask<IReadOnlyCollection<RosterOverview>> GetOverviewsAsync();
    public ValueTask<IReadOnlyCollection<UserSummary>?> GetMembersAsync(long id);
    public ValueTask<bool> DeleteByIdAsync(long id);
    public ValueTask<int> CountCoursesUsingAsync(long rosterId);
    public ValueTask<IReadOnlyCollection<long>> GetIdsOwnedByAsync(long userId);
    public ValueTask<int> DeleteByIdsAsync(IReadOnlyCollection<long> ids);
}

internal sealed class RosterRepository(DbSet<Roster> rosters, DbSet<Course> courses) : IRosterRepository
{
    public async ValueTask<bool> ExistsAsync(long id) =>
        await rosters.AsNoTracking().AnyAsync(r => r.Id == id);

    public async ValueTask<Roster?> GetByIdAsync(long id) =>
        await rosters.AsNoTracking()
                     .Include(r => r.Members)
                     .FirstOrDefaultAsync(r => r.Id == id);

    public void Add(Roster roster) => rosters.Add(roster);

    public async ValueTask<IReadOnlyCollection<Roster>> GetAllTrackedAutoRostersAsync()
    {
        List<Roster> all = await rosters.Include(r => r.Members)
                                        .Where(r => r.Kind == RosterKind.Auto)
                                        .ToListAsync();

        return all.AsReadOnly();
    }

    public async ValueTask<Roster?> GetTrackedAutoRosterByClassAsync(string classKey) =>
        await rosters.Include(r => r.Members)
                     .FirstOrDefaultAsync(r => r.Kind == RosterKind.Auto && r.ClassKey == classKey);

    public async ValueTask<Roster?> GetTrackedWithMembersAsync(long id) =>
        await rosters.Include(r => r.Members).FirstOrDefaultAsync(r => r.Id == id);

    public async ValueTask<IReadOnlyCollection<RosterOverview>> GetOverviewsAsync()
    {
        List<RosterOverview> overviews = await rosters.AsNoTracking()
                                                      .OrderBy(r => r.Name)
                                                      .Select(r => new RosterOverview(r.Id, r.Name, r.Kind,
                                                                  r.Members.Count, r.OwnerId))
                                                      .ToListAsync();

        return overviews.AsReadOnly();
    }

    public async ValueTask<IReadOnlyCollection<UserSummary>?> GetMembersAsync(long id)
    {
        if (!await rosters.AsNoTracking().AnyAsync(r => r.Id == id))
        {
            return null;
        }

        List<UserSummary> members = await rosters.AsNoTracking()
                                                 .Where(r => r.Id == id)
                                                 .SelectMany(r => r.Members)
                                                 .OrderBy(u => u.LastName).ThenBy(u => u.FirstName)
                                                 .Select(u => new UserSummary(u.Id, u.StudentId, u.FirstName,
                                                             u.LastName, u.Class, u.Role))
                                                 .ToListAsync();

        return members.AsReadOnly();
    }

    public async ValueTask<bool> DeleteByIdAsync(long id)
    {
        int affected = await rosters.Where(r => r.Id == id).ExecuteDeleteAsync();

        return affected == 1;
    }

    public async ValueTask<IReadOnlyCollection<long>> GetIdsOwnedByAsync(long userId)
    {
        List<long> ids = await rosters.AsNoTracking().Where(r => r.OwnerId == userId).Select(r => r.Id).ToListAsync();

        return ids.AsReadOnly();
    }

    public async ValueTask<int> DeleteByIdsAsync(IReadOnlyCollection<long> ids) =>
        ids.Count == 0 ? 0 : await rosters.Where(r => ids.Contains(r.Id)).ExecuteDeleteAsync();

    public async ValueTask<int> CountCoursesUsingAsync(long rosterId) =>
        await courses.AsNoTracking().CountAsync(c => c.RosterId == rosterId);
}
