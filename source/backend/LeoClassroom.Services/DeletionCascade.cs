using LeoClassroom.Services.Forgejo;
using LeoClassroom.Services.Util;
using LeoClassroom.Persistence.Repositories;
using LeoClassroom.Persistence.Util;
using OneOf;
using OneOf.Types;

namespace LeoClassroom.Services;

/// <param name="Repositories">Forgejo repositories removed</param>
/// <param name="Organisations">Forgejo organisations removed</param>
/// <param name="ForgejoFailures">
///     Forgejo calls that did not succeed. The rows are deleted regardless - an orphaned repository can be cleaned
///     up later, a half-deleted course cannot - so this is what tells a caller that Forgejo needs attention.
/// </param>
public sealed record CascadeResult(
    int Courses, int Assignments, int Acceptances, int Repositories, int Organisations, int ForgejoFailures)
{
    public static CascadeResult Empty { get; } = new(0, 0, 0, 0, 0, 0);

    public CascadeResult Plus(CascadeResult other) =>
        new(Courses + other.Courses, Assignments + other.Assignments, Acceptances + other.Acceptances,
            Repositories + other.Repositories, Organisations + other.Organisations,
            ForgejoFailures + other.ForgejoFailures);

    /// <summary>
    ///     The cascade as audit-log metadata, so every deletion records the same shape
    /// </summary>
    public IReadOnlyDictionary<string, string> ToMetadata() =>
        new Dictionary<string, string>
        {
            ["courses"] = Courses.ToString(),
            ["assignments"] = Assignments.ToString(),
            ["acceptances"] = Acceptances.ToString(),
            ["repositories"] = Repositories.ToString(),
            ["organisations"] = Organisations.ToString(),
            ["forgejoFailures"] = ForgejoFailures.ToString()
        };
}

/// <summary>
///     The one place that deletes courses, assignments and acceptances, keeping the database and Forgejo in step
/// </summary>
/// <remarks>
///     <para>
///         Every level is deleted explicitly rather than left to the database's cascade. A database cascade only
///         removes rows, and the rows are the smaller half of the truth here: each acceptance has a Forgejo
///         repository behind it and each course has a Forgejo organisation, and those are only reachable through
///         the very rows that a cascade would silently take away.
///     </para>
///     <para>
///         Order is therefore fixed from the leaves upward: repositories before the acceptances that name them,
///         acceptances before their assignments, assignments before their course, and the organisation only once
///         it holds no repositories - which is also the order Forgejo itself requires.
///     </para>
/// </remarks>
public interface IDeletionCascade
{
    public ValueTask<CascadeResult> DeleteAcceptancesAsync(IReadOnlyCollection<AcceptanceRepoRef> acceptances);
    public ValueTask<CascadeResult> DeleteAssignmentsAsync(IReadOnlyCollection<long> assignmentIds);
    public ValueTask<CascadeResult> DeleteCoursesAsync(IReadOnlyCollection<long> courseIds);
}

internal sealed class DeletionCascade(
    IUnitOfWork uow, IForgejoClient forgejo, ILogger<DeletionCascade> logger) : IDeletionCascade
{
    public async ValueTask<CascadeResult> DeleteAcceptancesAsync(IReadOnlyCollection<AcceptanceRepoRef> acceptances)
    {
        if (acceptances.Count == 0)
        {
            return CascadeResult.Empty;
        }

        int failures = 0;
        foreach (AcceptanceRepoRef acceptance in acceptances)
        {
            // while the row still names it - after the row is gone, the repository is unreachable
            OneOf<Success, ForgejoError> deleted =
                await forgejo.DeleteRepoAsync(acceptance.RepoOwner, acceptance.RepoName);
            if (deleted.Failure is { } repoFailed)
            {
                failures++;
                logger.LogWarning("Deleting repo {Owner}/{Repo} failed: {Reason}",
                                  acceptance.RepoOwner, acceptance.RepoName, repoFailed.Reason);
            }
        }

        int deletedRows = await uow.AcceptanceRepository.DeleteByIdsAsync([.. acceptances.Select(a => a.Id)]);

        return new CascadeResult(0, 0, deletedRows, acceptances.Count - failures, 0, failures);
    }

    public async ValueTask<CascadeResult> DeleteAssignmentsAsync(IReadOnlyCollection<long> assignmentIds)
    {
        if (assignmentIds.Count == 0)
        {
            return CascadeResult.Empty;
        }

        CascadeResult result =
            await DeleteAcceptancesAsync(await uow.AcceptanceRepository.GetByAssignmentsAsync(assignmentIds));

        int deletedRows = await uow.AssignmentRepository.DeleteByIdsAsync(assignmentIds);

        return result.Plus(new CascadeResult(0, deletedRows, 0, 0, 0, 0));
    }

    public async ValueTask<CascadeResult> DeleteCoursesAsync(IReadOnlyCollection<long> courseIds)
    {
        if (courseIds.Count == 0)
        {
            return CascadeResult.Empty;
        }

        // read before deleting: the organisation name only exists on the course row
        IReadOnlyCollection<string> orgs = await uow.CourseRepository.GetForgejoOrgsAsync(courseIds);

        CascadeResult result =
            await DeleteAssignmentsAsync(await uow.AssignmentRepository.GetIdsByCoursesAsync(courseIds));

        int orgFailures = 0;
        foreach (string org in orgs)
        {
            // only now, with the organisation's repositories gone, will Forgejo let it go
            OneOf<Success, ForgejoError> deleted = await forgejo.DeleteOrgAsync(org);
            if (deleted.Failure is { } orgFailed)
            {
                orgFailures++;
                logger.LogWarning("Deleting Forgejo org {Org} failed: {Reason}", org, orgFailed.Reason);
            }
        }

        int deletedRows = await uow.CourseRepository.DeleteByIdsAsync(courseIds);

        return result.Plus(new CascadeResult(deletedRows, 0, 0, 0, orgs.Count - orgFailures, orgFailures));
    }
}
