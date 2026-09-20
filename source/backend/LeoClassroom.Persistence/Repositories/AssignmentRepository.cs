using LeoClassroom.Persistence.Model;
using LeoClassroom.Shared;
using Microsoft.EntityFrameworkCore;

namespace LeoClassroom.Persistence.Repositories;

public interface IAssignmentRepository
{
    public void Add(Assignment assignment);
    public ValueTask<Assignment?> GetByIdAsync(long id);
    public ValueTask<Assignment?> GetTrackedByIdAsync(long id);
    public ValueTask<Assignment?> GetTrackedWithTeachersAsync(long id);
    public ValueTask<Assignment?> GetWithTeachersAsync(long id);
    public ValueTask<Assignment?> GetWithAcceptancesAsync(long id);
    public ValueTask<Assignment?> GetReconciliationDataAsync(long id);
    public ValueTask<bool> ExistsWithSlugInCourseAsync(string slug, long courseId);
    public ValueTask<bool> DeleteByIdAsync(long id);
    public ValueTask<IReadOnlyCollection<long>> GetIdsWithHardDeadlineAsync();
    public ValueTask<IReadOnlyCollection<StudentSubmission>?> GetStudentSubmissionsAsync(long id);
    public ValueTask<IReadOnlyCollection<long>> GetDueForAutoDeleteAsync(Instant now);
    public ValueTask<IReadOnlyCollection<AssignedAssignment>> GetAssignedToStudentAsync(long studentId);
    public ValueTask<bool> IsStudentAssignedAsync(long assignmentId, long studentId);
    public ValueTask<IReadOnlyCollection<long>> GetIdsOwnedByAsync(long userId);
    public ValueTask<IReadOnlyCollection<long>> GetIdsByCoursesAsync(IReadOnlyCollection<long> courseIds);
    public ValueTask<int> DeleteByIdsAsync(IReadOnlyCollection<long> ids);
}

internal sealed class AssignmentRepository(DbSet<Assignment> assignments) : IAssignmentRepository
{
    public void Add(Assignment assignment) => assignments.Add(assignment);

    public async ValueTask<Assignment?> GetByIdAsync(long id) =>
        await assignments.AsNoTracking()
                         .Include(a => a.Course)
                         .FirstOrDefaultAsync(a => a.Id == id);

    public async ValueTask<Assignment?> GetTrackedByIdAsync(long id) =>
        await assignments.FirstOrDefaultAsync(a => a.Id == id);

    public async ValueTask<Assignment?> GetTrackedWithTeachersAsync(long id) =>
        await assignments.Include(a => a.Owner)
                         .Include(a => a.CoTeachers)
                         .FirstOrDefaultAsync(a => a.Id == id);

    public async ValueTask<Assignment?> GetWithTeachersAsync(long id) =>
        await assignments.AsNoTracking()
                         .Include(a => a.Owner)
                         .Include(a => a.CoTeachers)
                         .FirstOrDefaultAsync(a => a.Id == id);

    public async ValueTask<Assignment?> GetWithAcceptancesAsync(long id) =>
        await assignments.AsNoTracking()
                         .Include(a => a.Acceptances)
                         .FirstOrDefaultAsync(a => a.Id == id);

    public async ValueTask<Assignment?> GetReconciliationDataAsync(long id) =>
        await assignments.AsNoTracking()
                         .AsSplitQuery()
                         .Include(a => a.Owner)
                         .Include(a => a.CoTeachers)
                         .Include(a => a.Course).ThenInclude(c => c.Owner)
                         .Include(a => a.Course).ThenInclude(c => c.CoTeachers)
                         .Include(a => a.Acceptances).ThenInclude(ac => ac.Student)
                         .FirstOrDefaultAsync(a => a.Id == id);

    public async ValueTask<bool> ExistsWithSlugInCourseAsync(string slug, long courseId) =>
        await assignments.AsNoTracking().AnyAsync(a => a.CourseId == courseId && a.Slug == slug);

    public async ValueTask<IReadOnlyCollection<long>> GetIdsOwnedByAsync(long userId)
    {
        List<long> ids = await assignments.AsNoTracking()
                                          .Where(a => a.OwnerId == userId)
                                          .Select(a => a.Id)
                                          .ToListAsync();

        return ids.AsReadOnly();
    }

    public async ValueTask<IReadOnlyCollection<long>> GetIdsByCoursesAsync(IReadOnlyCollection<long> courseIds)
    {
        List<long> ids = await assignments.AsNoTracking()
                                          .Where(a => courseIds.Contains(a.CourseId))
                                          .Select(a => a.Id)
                                          .ToListAsync();

        return ids.AsReadOnly();
    }

    public async ValueTask<int> DeleteByIdsAsync(IReadOnlyCollection<long> ids) =>
        ids.Count == 0 ? 0 : await assignments.Where(a => ids.Contains(a.Id)).ExecuteDeleteAsync();

    public async ValueTask<bool> DeleteByIdAsync(long id)
    {
        int affected = await assignments.Where(a => a.Id == id).ExecuteDeleteAsync();

        return affected == 1;
    }

    public async ValueTask<IReadOnlyCollection<long>> GetIdsWithHardDeadlineAsync()
    {
        List<long> ids = await assignments.AsNoTracking()
                                          .Where(a => a.DeadlineKind == DeadlineKind.Hard && a.Deadline != null)
                                          .Select(a => a.Id)
                                          .ToListAsync();

        return ids.AsReadOnly();
    }

    public async ValueTask<IReadOnlyCollection<StudentSubmission>?> GetStudentSubmissionsAsync(long id)
    {
        if (!await assignments.AsNoTracking().AnyAsync(a => a.Id == id))
        {
            return null;
        }

        List<StudentSubmission> rows = await assignments.AsNoTracking()
            .Where(a => a.Id == id)
            .SelectMany(a => a.Acceptances)
            .OrderBy(ac => ac.Student.LastName).ThenBy(ac => ac.Student.FirstName)
            .Select(ac => new StudentSubmission(ac.Id, ac.StudentId, ac.Student.StudentId, ac.Student.FirstName,
                        ac.Student.LastName, ac.Status, ac.LastCommitAt, ac.LastPushAt, ac.RepoUrl,
                        ac.Late, ac.LateSince, ac.FeedbackState, ac.FeedbackReadAt, ac.FeedbackPrNumber))
            .ToListAsync();

        return rows.AsReadOnly();
    }

    public async ValueTask<IReadOnlyCollection<long>> GetDueForAutoDeleteAsync(Instant now)
    {
        List<long> ids = await assignments.AsNoTracking()
                                          .Where(a => a.AutoDeleteEnabled && a.AutoDeleteOn != null
                                                      && a.AutoDeleteOn <= now)
                                          .Select(a => a.Id)
                                          .ToListAsync();

        return ids.AsReadOnly();
    }

    public async ValueTask<IReadOnlyCollection<AssignedAssignment>> GetAssignedToStudentAsync(long studentId)
    {
        List<AssignedAssignment> rows = await assignments.AsNoTracking()
            .Where(a => a.Course.Roster.Members.Any(m => m.Id == studentId))
            .OrderBy(a => a.Course.Title).ThenBy(a => a.Title)
            .Select(a => new AssignedAssignment(a.Id, a.CourseId, a.Course.Title, a.Title, a.Deadline, a.DeadlineKind,
                        a.Acceptances.Any(ac => ac.StudentId == studentId),
                        a.Acceptances.Where(ac => ac.StudentId == studentId)
                                     .Select(ac => (SubmissionStatus?)ac.Status).FirstOrDefault()))
            .ToListAsync();

        return rows.AsReadOnly();
    }

    public async ValueTask<bool> IsStudentAssignedAsync(long assignmentId, long studentId) =>
        await assignments.AsNoTracking()
                         .AnyAsync(a => a.Id == assignmentId
                                        && a.Course.Roster.Members.Any(m => m.Id == studentId));
}
