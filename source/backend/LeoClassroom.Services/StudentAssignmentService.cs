using LeoClassroom.Services.Auth;
using LeoClassroom.Services.Forgejo;
using LeoClassroom.Persistence.Model;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Shared;
using OneOf;
using OneOf.Types;

namespace LeoClassroom.Services;

public sealed record StudentAssignmentList(
    IReadOnlyCollection<AssignedAssignment> Accepted,
    IReadOnlyCollection<AssignedAssignment> Unaccepted);

public sealed record StudentAssignmentView(Assignment Assignment, Acceptance? Acceptance);

public sealed record AcceptanceRetry(Acceptance Acceptance, bool NeedsProvisioning);

public interface IStudentAssignmentService
{
    public ValueTask<StudentAssignmentList> ListAsync();
    public ValueTask<OneOf<StudentAssignmentView, NotFound, Forbidden>> GetForStudentAsync(long id);
    public ValueTask<OneOf<Success<Acceptance>, NotFound, Forbidden, AlreadyExists>> AcceptAsync(long assignmentId);
    public ValueTask<OneOf<AcceptanceRetry, NotFound>> RetryAsync(long assignmentId);
    public ValueTask<OneOf<Success, NotFound>> ConfirmFeedbackReadAsync(long assignmentId);
}

internal sealed class StudentAssignmentService(
    IUnitOfWork uow,
    ICurrentUser currentUser,
    IClock clock,
    ILogger<StudentAssignmentService> logger) : IStudentAssignmentService
{
    public async ValueTask<StudentAssignmentList> ListAsync()
    {
        long? studentId = await uow.UserRepository.GetIdByStudentIdAsync(currentUser.StudentId);
        if (studentId is null)
        {
            return new StudentAssignmentList([], []);
        }

        IReadOnlyCollection<AssignedAssignment> all =
            await uow.AssignmentRepository.GetAssignedToStudentAsync(studentId.Value);

        return new StudentAssignmentList(
            all.Where(a => a.Accepted).ToList().AsReadOnly(),
            all.Where(a => !a.Accepted).ToList().AsReadOnly());
    }

    public async ValueTask<OneOf<StudentAssignmentView, NotFound, Forbidden>> GetForStudentAsync(long id)
    {
        long? studentId = await uow.UserRepository.GetIdByStudentIdAsync(currentUser.StudentId);
        if (studentId is null)
        {
            return new NotFound();
        }

        Assignment? assignment = await uow.AssignmentRepository.GetByIdAsync(id);
        if (assignment is null)
        {
            return new NotFound();
        }
        if (!await uow.AssignmentRepository.IsStudentAssignedAsync(id, studentId.Value))
        {
            return new Forbidden();
        }

        Acceptance? acceptance = await uow.AcceptanceRepository.GetByAssignmentAndStudentAsync(id, studentId.Value);

        return new StudentAssignmentView(assignment, acceptance);
    }

    public async ValueTask<OneOf<Success<Acceptance>, NotFound, Forbidden, AlreadyExists>> AcceptAsync(long assignmentId)
    {
        long? studentId = await uow.UserRepository.GetIdByStudentIdAsync(currentUser.StudentId);
        if (studentId is null)
        {
            return new NotFound();
        }

        Assignment? assignment = await uow.AssignmentRepository.GetByIdAsync(assignmentId);
        if (assignment is null)
        {
            return new NotFound();
        }
        if (!await uow.AssignmentRepository.IsStudentAssignedAsync(assignmentId, studentId.Value))
        {
            return new Forbidden();
        }
        if (await uow.AcceptanceRepository.ExistsAsync(assignmentId, studentId.Value))
        {
            return new AlreadyExists();
        }

        string org = assignment.Course.ForgejoOrg ?? ForgejoNaming.OrgName(assignment.Course.Title, assignment.Course.Id);
        var acceptance = new Acceptance
        {
            AssignmentId = assignmentId,
            StudentId = studentId.Value,
            RepoOwner = org,
            RepoName = ForgejoNaming.RepoName(assignment.Course.Title, assignment.Slug, currentUser.StudentId),
            AcceptedAt = clock.GetCurrentInstant(),
            Status = SubmissionStatus.Provisioning,
            FeedbackState = FeedbackState.None
        };
        uow.AcceptanceRepository.Add(acceptance);
        await uow.SaveChangesAsync();
        // The endpoint queues this acceptance after committing, so the worker can see the new row.
        logger.LogInformation("Student {StudentId} accepted assignment {AssignmentId}; acceptance {AcceptanceId} created",
                              currentUser.StudentId, assignmentId, acceptance.Id);

        return new Success<Acceptance>(acceptance);
    }

    public async ValueTask<OneOf<AcceptanceRetry, NotFound>> RetryAsync(long assignmentId)
    {
        long? studentId = await uow.UserRepository.GetIdByStudentIdAsync(currentUser.StudentId);
        if (studentId is null)
        {
            return new NotFound();
        }

        Acceptance? acceptance =
            await uow.AcceptanceRepository.GetTrackedByAssignmentAndStudentAsync(assignmentId, studentId.Value);
        if (acceptance is null)
        {
            return new NotFound();
        }

        bool needsProvisioning = acceptance.Status == SubmissionStatus.Failed;
        if (needsProvisioning)
        {
            acceptance.Status = SubmissionStatus.Provisioning;
            await uow.SaveChangesAsync();
        }

        return new AcceptanceRetry(acceptance, needsProvisioning);
    }

    public async ValueTask<OneOf<Success, NotFound>> ConfirmFeedbackReadAsync(long assignmentId)
    {
        long? studentId = await uow.UserRepository.GetIdByStudentIdAsync(currentUser.StudentId);
        if (studentId is null)
        {
            return new NotFound();
        }

        Acceptance? acceptance =
            await uow.AcceptanceRepository.GetTrackedByAssignmentAndStudentAsync(assignmentId, studentId.Value);
        if (acceptance is null)
        {
            return new NotFound();
        }

        if (acceptance.FeedbackState == FeedbackState.Unread)
        {
            acceptance.FeedbackState = FeedbackState.Read;
            acceptance.FeedbackReadAt = clock.GetCurrentInstant();
            await uow.SaveChangesAsync();
            logger.LogInformation("Student {StudentId} confirmed reading feedback for assignment {AssignmentId}",
                                  currentUser.StudentId, assignmentId);
        }

        return new Success();
    }
}
