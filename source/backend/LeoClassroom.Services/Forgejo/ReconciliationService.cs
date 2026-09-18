using LeoClassroom.Persistence.Model;
using LeoClassroom.Services.Util;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Shared;
using OneOf;
using OneOf.Types;

namespace LeoClassroom.Services.Forgejo;

public interface IReconciliationService
{
    public ValueTask<OneOf<Success, NotFound, ForgejoError>> ReconcileAsync(long assignmentId);
    public ValueTask<OneOf<Success, NotFound, ForgejoError>> ReconcileCourseAsync(long courseId);
    public ValueTask ReconcileAllWithHardDeadlinesAsync();
}

internal sealed class ReconciliationService(
    IUnitOfWork uow, IForgejoClient forgejo, IClock clock, ILogger<ReconciliationService> logger)
    : IReconciliationService
{
    public async ValueTask<OneOf<Success, NotFound, ForgejoError>> ReconcileAsync(long assignmentId)
    {
        Assignment? assignment = await uow.AssignmentRepository.GetReconciliationDataAsync(assignmentId);
        if (assignment is null)
        {
            return new NotFound();
        }

        Course course = assignment.Course;
        string org = course.ForgejoOrg ?? ForgejoNaming.OrgName(course.Title, course.Id);
        var input = new ReconciliationInput(
            org,
            TeacherStudentIds(course),
            [.. assignment.Acceptances.Select(a => new AcceptanceInput(a.Student.StudentId, a.RepoOwner, a.RepoName))],
            assignment.Deadline,
            assignment.DeadlineKind,
            assignment.HardDeadlineRevokesRead);

        DesiredState desired = DesiredStateComputer.Compute(input, clock.GetCurrentInstant());

        return await ApplyAsync(desired);
    }

    public async ValueTask<OneOf<Success, NotFound, ForgejoError>> ReconcileCourseAsync(long courseId)
    {
        Course? course = await uow.CourseRepository.GetWithTeachersAsync(courseId);
        if (course is null)
        {
            return new NotFound();
        }

        if (course.ForgejoOrg is null)
        {
            return new Success();
        }

        var input = new ReconciliationInput(course.ForgejoOrg, TeacherStudentIds(course), [], null,
                                             DeadlineKind.None, HardDeadlineRevokesRead: false);

        return await ApplyAsync(DesiredStateComputer.Compute(input, clock.GetCurrentInstant()));
    }

    private static IReadOnlyList<string> TeacherStudentIds(Course course) =>
        [course.Owner.StudentId, .. course.CoTeachers.Select(t => t.StudentId)];

    public async ValueTask ReconcileAllWithHardDeadlinesAsync()
    {
        IReadOnlyCollection<long> ids = await uow.AssignmentRepository.GetIdsWithHardDeadlineAsync();
        foreach (long id in ids)
        {
            OneOf<Success, NotFound, ForgejoError> result = await ReconcileAsync(id);
            result.Switch(
                success => { },
                notFound => { },
                error => logger.LogWarning("Scheduled reconcile of assignment {AssignmentId} failed: {Reason}",
                                           id, error.Reason));
        }
    }

    private async ValueTask<OneOf<Success, NotFound, ForgejoError>> ApplyAsync(DesiredState desired)
    {
        OneOf<Success, ForgejoError> org = await forgejo.EnsureOrgAsync(desired.Org);
        if (org.Failure is { } orgFailed)
        {
            return orgFailed;
        }

        OneOf<Success<long>, ForgejoError> team =
            await forgejo.EnsureTeamAsync(desired.Org, desired.TeamName, CollaboratorPermission.Admin);

        return await team.Match<ValueTask<OneOf<Success, NotFound, ForgejoError>>>(
            teamId => ConvergeTeamAsync(teamId.Value, desired),
            error => ValueTask.FromResult<OneOf<Success, NotFound, ForgejoError>>(error));
    }

    private async ValueTask<OneOf<Success, NotFound, ForgejoError>> ConvergeTeamAsync(long teamId, DesiredState desired)
    {
        OneOf<Success, ForgejoError> teamMembers = await ConvergeTeamMembersAsync(teamId, desired.TeamMembers);
        if (teamMembers.Failure is { } membersFailed)
        {
            return membersFailed;
        }

        foreach (DesiredCollaborator collaborator in desired.Collaborators)
        {
            OneOf<Success, ForgejoError> step = await ApplyCollaboratorAsync(collaborator);
            if (step.Failure is { } stepFailed)
            {
                return stepFailed;
            }
        }

        return new Success();
    }

    private async ValueTask<OneOf<Success, ForgejoError>> ConvergeTeamMembersAsync(
        long teamId, IReadOnlyList<string> desiredMembers)
    {
        OneOf<IReadOnlyCollection<string>, ForgejoError> actual = await forgejo.ListTeamMembersAsync(teamId);

        return await actual.Match(
            members => ConvergeTeamMembersAsync(teamId, desiredMembers, members),
            error => ValueTask.FromResult<OneOf<Success, ForgejoError>>(error));
    }

    private async ValueTask<OneOf<Success, ForgejoError>> ConvergeTeamMembersAsync(
        long teamId, IReadOnlyList<string> desiredMembers, IReadOnlyCollection<string> actualMembers)
    {
        HashSet<string> desired = [.. desiredMembers];
        HashSet<string> current = [.. actualMembers];

        foreach (string extra in current.Where(m => !desired.Contains(m)))
        {
            OneOf<Success, ForgejoError> removed = await forgejo.RemoveTeamMemberAsync(teamId, extra);
            if (removed.Failure is { } removeFailed)
            {
                return removeFailed;
            }
        }

        foreach (string missing in desired.Where(m => !current.Contains(m)))
        {
            OneOf<Success, ForgejoError> added = await forgejo.AddTeamMemberAsync(teamId, missing);
            if (added.Failure is { } addFailed)
            {
                return addFailed;
            }
        }

        return new Success();
    }

    private async ValueTask<OneOf<Success, ForgejoError>> ApplyCollaboratorAsync(DesiredCollaborator collaborator)
    {
        OneOf<CollaboratorPermission, NotFound, ForgejoError> actual = await forgejo.GetCollaboratorPermissionAsync(
            collaborator.RepoOwner, collaborator.RepoName, collaborator.Username);

        return await actual.Match(
            granted => ApplyCollaboratorAsync(collaborator, granted),
            // no grant at all is the same starting point as an explicit "none"
            notFound => ApplyCollaboratorAsync(collaborator, CollaboratorPermission.None),
            error => ValueTask.FromResult<OneOf<Success, ForgejoError>>(error));
    }

    private async ValueTask<OneOf<Success, ForgejoError>> ApplyCollaboratorAsync(
        DesiredCollaborator collaborator, CollaboratorPermission current)
    {
        if (current == collaborator.Permission)
        {
            return new Success();
        }

        if (collaborator.Permission == CollaboratorPermission.None)
        {
            return await forgejo.RemoveCollaboratorAsync(
                collaborator.RepoOwner, collaborator.RepoName, collaborator.Username);
        }

        return await forgejo.SetCollaboratorAsync(
            collaborator.RepoOwner, collaborator.RepoName, collaborator.Username, collaborator.Permission);
    }
}
