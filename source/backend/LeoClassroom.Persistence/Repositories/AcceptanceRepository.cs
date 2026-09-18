using LeoClassroom.Persistence.Model;
using LeoClassroom.Shared;
using Microsoft.EntityFrameworkCore;

namespace LeoClassroom.Persistence.Repositories;

public interface IAcceptanceRepository
{
    public void Add(Acceptance acceptance);
    public ValueTask<Acceptance?> GetByIdAsync(long id);
    public ValueTask<Acceptance?> GetTrackedByIdAsync(long id);
    public ValueTask<Acceptance?> GetTrackedByRepoAsync(string repoOwner, string repoName);
    public ValueTask<Acceptance?> GetTrackedWithAssignmentTeachersAsync(long id);
    public ValueTask<bool> ExistsAsync(long assignmentId, long studentId);
    public ValueTask<Acceptance?> GetByAssignmentAndStudentAsync(long assignmentId, long studentId);
    public ValueTask<Acceptance?> GetTrackedByAssignmentAndStudentAsync(long assignmentId, long studentId);
    public ValueTask<IReadOnlyCollection<long>> GetIdsByStatusAsync(SubmissionStatus status);
    public ValueTask<IReadOnlyCollection<Acceptance>> GetByAssignmentWithStudentAsync(long assignmentId);
    public ValueTask<bool> DeleteByIdAsync(long id);
    public ValueTask<IReadOnlyCollection<AcceptanceRepoRef>> GetForDeletionAsync(
        long studentId, IReadOnlyCollection<long> assignmentIds);
    public ValueTask<IReadOnlyCollection<AcceptanceRepoRef>> GetByAssignmentsAsync(
        IReadOnlyCollection<long> assignmentIds);
    public ValueTask<int> DeleteByIdsAsync(IReadOnlyCollection<long> ids);
}

/// <summary>
///     The identity of an acceptance and of the Forgejo repository behind it, which has to be read before the row
///     is deleted because the repository lives outside the database
/// </summary>
public sealed record AcceptanceRepoRef(long Id, long StudentId, long AssignmentId, string RepoOwner, string RepoName);

internal sealed class AcceptanceRepository(DbSet<Acceptance> acceptances) : IAcceptanceRepository
{
    public void Add(Acceptance acceptance) => acceptances.Add(acceptance);

    public async ValueTask<Acceptance?> GetByIdAsync(long id) =>
        await acceptances.AsNoTracking()
                         .Include(a => a.Student)
                         .FirstOrDefaultAsync(a => a.Id == id);

    public async ValueTask<Acceptance?> GetTrackedByIdAsync(long id) =>
        await acceptances.FirstOrDefaultAsync(a => a.Id == id);

    public async ValueTask<Acceptance?> GetTrackedByRepoAsync(string repoOwner, string repoName) =>
        await acceptances.FirstOrDefaultAsync(a => a.RepoOwner == repoOwner && a.RepoName == repoName);

    public async ValueTask<Acceptance?> GetTrackedWithAssignmentTeachersAsync(long id) =>
        await acceptances.Include(a => a.Student)
                         .Include(a => a.Assignment).ThenInclude(a => a.Owner)
                         .Include(a => a.Assignment).ThenInclude(a => a.CoTeachers)
                         .FirstOrDefaultAsync(a => a.Id == id);

    public async ValueTask<bool> ExistsAsync(long assignmentId, long studentId) =>
        await acceptances.AsNoTracking()
                         .AnyAsync(a => a.AssignmentId == assignmentId && a.StudentId == studentId);

    public async ValueTask<Acceptance?> GetByAssignmentAndStudentAsync(long assignmentId, long studentId) =>
        await acceptances.AsNoTracking()
                         .FirstOrDefaultAsync(a => a.AssignmentId == assignmentId && a.StudentId == studentId);

    public async ValueTask<Acceptance?> GetTrackedByAssignmentAndStudentAsync(long assignmentId, long studentId) =>
        await acceptances.FirstOrDefaultAsync(a => a.AssignmentId == assignmentId && a.StudentId == studentId);

    public async ValueTask<IReadOnlyCollection<long>> GetIdsByStatusAsync(SubmissionStatus status)
    {
        List<long> ids = await acceptances.AsNoTracking()
                                          .Where(a => a.Status == status)
                                          .Select(a => a.Id)
                                          .ToListAsync();

        return ids.AsReadOnly();
    }

    public async ValueTask<IReadOnlyCollection<Acceptance>> GetByAssignmentWithStudentAsync(long assignmentId)
    {
        List<Acceptance> rows = await acceptances.AsNoTracking()
                                                 .Include(a => a.Student)
                                                 .Where(a => a.AssignmentId == assignmentId)
                                                 .ToListAsync();

        return rows.AsReadOnly();
    }

    public async ValueTask<IReadOnlyCollection<AcceptanceRepoRef>> GetForDeletionAsync(
        long studentId, IReadOnlyCollection<long> assignmentIds)
    {
        List<AcceptanceRepoRef> refs =
            await acceptances.AsNoTracking()
                             .Where(a => a.StudentId == studentId || assignmentIds.Contains(a.AssignmentId))
                             .Select(a => new AcceptanceRepoRef(a.Id, a.StudentId, a.AssignmentId, a.RepoOwner,
                                                                a.RepoName))
                             .ToListAsync();

        return refs.AsReadOnly();
    }

    public async ValueTask<IReadOnlyCollection<AcceptanceRepoRef>> GetByAssignmentsAsync(
        IReadOnlyCollection<long> assignmentIds)
    {
        List<AcceptanceRepoRef> refs =
            await acceptances.AsNoTracking()
                             .Where(a => assignmentIds.Contains(a.AssignmentId))
                             .Select(a => new AcceptanceRepoRef(a.Id, a.StudentId, a.AssignmentId, a.RepoOwner,
                                                                a.RepoName))
                             .ToListAsync();

        return refs.AsReadOnly();
    }

    public async ValueTask<int> DeleteByIdsAsync(IReadOnlyCollection<long> ids) =>
        ids.Count == 0 ? 0 : await acceptances.Where(a => ids.Contains(a.Id)).ExecuteDeleteAsync();

    public async ValueTask<bool> DeleteByIdAsync(long id)
    {
        int affected = await acceptances.Where(a => a.Id == id).ExecuteDeleteAsync();

        return affected == 1;
    }
}
