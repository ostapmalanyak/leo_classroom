using LeoClassroom.Persistence.Model;
using LeoClassroom.Shared;
using Microsoft.EntityFrameworkCore;

namespace LeoClassroom.Persistence.Repositories;

public interface IUserRepository
{
    public ValueTask<User?> GetByIdAsync(long id);
    public ValueTask<bool> ExistsActiveTeacherAsync(long id);
    public void Add(User user);
    public ValueTask<User?> GetTrackedByStudentIdAsync(string studentId);
    public ValueTask<long?> GetIdByStudentIdAsync(string studentId);
    public ValueTask<IReadOnlyCollection<User>> GetAllTrackedAsync();
    public ValueTask<User?> GetTrackedByIdAsync(long id);
    public ValueTask<IReadOnlyCollection<UserSummary>> SearchActiveAsync(string term, Role? role, int take);
    public ValueTask<IReadOnlyCollection<ProvisionedUser>> GetAllProvisionedAsync();
    public ValueTask<IReadOnlyCollection<User>> GetPurgeCandidatesAsync(Instant lastSeenBefore);
    public ValueTask<bool> DeleteByIdAsync(long id);
}

/// <summary>
///     The columns the provisioning cache compares a caller's claims against
/// </summary>
public sealed record ProvisionedUser(
    string StudentId,
    string FirstName,
    string LastName,
    string? Email,
    Role Role,
    string? Class,
    UserState State);

internal sealed class UserRepository(DbSet<User> users) : IUserRepository
{
    public async ValueTask<User?> GetByIdAsync(long id) =>
        await users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);

    public async ValueTask<bool> ExistsActiveTeacherAsync(long id) =>
        await users.AsNoTracking()
                   .AnyAsync(u => u.Id == id && u.State == UserState.Active && u.Role == Role.Teacher);

    public void Add(User user) => users.Add(user);

    public async ValueTask<User?> GetTrackedByStudentIdAsync(string studentId) =>
        await users.FirstOrDefaultAsync(u => u.StudentId == studentId);

    public async ValueTask<long?> GetIdByStudentIdAsync(string studentId)
    {
        long id = await users.AsNoTracking()
                             .Where(u => u.StudentId == studentId)
                             .Select(u => u.Id)
                             .FirstOrDefaultAsync();

        return id == 0 ? null : id;
    }

    public async ValueTask<IReadOnlyCollection<ProvisionedUser>> GetAllProvisionedAsync()
    {
        List<ProvisionedUser> all = await users.AsNoTracking()
                                               .Select(u => new ProvisionedUser(u.StudentId, u.FirstName, u.LastName,
                                                           u.Email, u.Role, u.Class, u.State))
                                               .ToListAsync();

        return all.AsReadOnly();
    }

    /// <summary>
    ///     Users who left the directory long enough ago to be purged
    /// </summary>
    /// <remarks>
    ///     A null <c>LdapLastSeen</c> is deliberately not treated as "infinitely old": it means the LDAP sync has
    ///     never seen this account, which is also true of someone provisioned from their claims minutes ago.
    ///     Requiring the soft-deleted state as well means only accounts the sync has actually retired are
    ///     candidates.
    /// </remarks>
    public async ValueTask<IReadOnlyCollection<User>> GetPurgeCandidatesAsync(Instant lastSeenBefore)
    {
        List<User> candidates = await users.AsNoTracking()
                                           .Where(u => u.State == UserState.SoftDeleted
                                                       && u.LdapLastSeen != null
                                                       && u.LdapLastSeen < lastSeenBefore)
                                           .OrderBy(u => u.LdapLastSeen)
                                           .ToListAsync();

        return candidates.AsReadOnly();
    }

    public async ValueTask<bool> DeleteByIdAsync(long id)
    {
        int affected = await users.Where(u => u.Id == id).ExecuteDeleteAsync();

        return affected == 1;
    }

    public async ValueTask<IReadOnlyCollection<User>> GetAllTrackedAsync()
    {
        List<User> all = await users.ToListAsync();

        return all.AsReadOnly();
    }

    public async ValueTask<User?> GetTrackedByIdAsync(long id) =>
        await users.FirstOrDefaultAsync(u => u.Id == id);

    public async ValueTask<IReadOnlyCollection<UserSummary>> SearchActiveAsync(string term, Role? role, int take)
    {
        IQueryable<User> query = users.AsNoTracking().Where(u => u.State == UserState.Active);
        if (role is not null)
        {
            query = query.Where(u => u.Role == role.Value);
        }
        if (!string.IsNullOrWhiteSpace(term))
        {
            query = query.Where(u => u.StudentId.Contains(term)
                                     || u.FirstName.Contains(term)
                                     || u.LastName.Contains(term));
        }

        List<UserSummary> results = await query.OrderBy(u => u.LastName).ThenBy(u => u.FirstName)
                                               .Take(take)
                                               .Select(u => new UserSummary(u.Id, u.StudentId, u.FirstName,
                                                           u.LastName, u.Class, u.Role))
                                               .ToListAsync();

        return results.AsReadOnly();
    }
}
