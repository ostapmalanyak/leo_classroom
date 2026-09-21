using LeoClassroom.Services.Audit;
using LeoClassroom.Services.Auth;
using LeoClassroom.Services.Forgejo;
using LeoClassroom.Services.Moodle;
using LeoClassroom.Services.Notifications;
using LeoClassroom.Services.Util;
using LeoClassroom.Persistence.Model;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Shared;
using OneOf;
using OneOf.Types;

namespace LeoClassroom.Services;

public sealed record AssignmentSettings(
    string Title,
    string? Description,
    string? HintsInstructions,
    Instant? Deadline,
    DeadlineKind DeadlineKind,
    bool HardDeadlineRevokesRead,
    StarterSourceKind StarterSourceKind,
    string? StarterRepoUrl,
    string? ReadmeMarkdown,
    bool AutoDeleteEnabled,
    Instant? AutoDeleteOn,
    DownloadSnapshotMode DownloadSnapshotMode);

public interface IAssignmentService
{
    public ValueTask<OneOf<IReadOnlyCollection<Assignment>, NotFound, Forbidden>> GetForCourseAsync(long courseId);
    public ValueTask<OneOf<Success<Assignment>, NotFound, Forbidden>> CreateAsync(long courseId, AssignmentSettings settings);
    public ValueTask<OneOf<Success<Assignment>, NotFound, Forbidden>> EditAsync(long id, AssignmentSettings settings);
    public ValueTask<OneOf<Assignment, NotFound, Forbidden>> GetForEditAsync(long id);
    public ValueTask<OneOf<Success, NotFound, Forbidden>> DeleteAsync(long id);
    public ValueTask<OneOf<Success, NotFound, Forbidden>> AddCoTeacherAsync(long id, long userId);
    public ValueTask<OneOf<Success, NotFound, Forbidden>> RemoveCoTeacherAsync(long id, long userId);
    public ValueTask<OneOf<IReadOnlyCollection<StudentSubmission>, NotFound, Forbidden>> GetStudentListAsync(long id);
}

internal sealed class AssignmentService(
    IUnitOfWork uow,
    ICurrentUser currentUser,
    IReconciliationService reconciliation,
    IDeletionCascade deletion,
    IAuditLog audit,
    INotificationService notifications,
    IMoodleSyncService moodle,
    IClock clock,
    ILogger<AssignmentService> logger) : IAssignmentService
{
    private const int AutoDeleteDefaultYears = 2;

    public async ValueTask<OneOf<IReadOnlyCollection<Assignment>, NotFound, Forbidden>> GetForCourseAsync(long courseId)
    {
        Course? course = await uow.CourseRepository.GetWithTeachersAsync(courseId);
        if (course is null)
        {
            return new NotFound();
        }
        if (!IsCourseTeacher(course))
        {
            return new Forbidden();
        }

        IReadOnlyCollection<Assignment> assignments = await uow.AssignmentRepository.GetByCourseIdAsync(courseId);

        return OneOf<IReadOnlyCollection<Assignment>, NotFound, Forbidden>.FromT0(assignments);
    }

    public async ValueTask<OneOf<Success<Assignment>, NotFound, Forbidden>> CreateAsync(
        long courseId, AssignmentSettings settings)
    {
        Course? course = await uow.CourseRepository.GetWithTeachersAsync(courseId);
        if (course is null)
        {
            return new NotFound();
        }
        if (!IsCourseTeacher(course))
        {
            return new Forbidden();
        }

        long? ownerId = await uow.UserRepository.GetIdByStudentIdAsync(currentUser.StudentId);
        if (ownerId is null)
        {
            return new NotFound();
        }

        string slug = await UniqueSlugAsync(settings.Title, courseId);
        var assignment = new Assignment { CourseId = courseId, Title = settings.Title, Slug = slug, OwnerId = ownerId.Value };
        Apply(assignment, settings);
        uow.AssignmentRepository.Add(assignment);
        await uow.SaveChangesAsync();
        await audit.RecordAsync(AuditAction.AssignmentCreated, "Assignment", assignment.Id.ToString(),
                                new Dictionary<string, string> { ["courseId"] = courseId.ToString() });
        await notifications.EnqueueAssignmentCreatedAsync(assignment);
        await moodle.EnqueueCreateAsync(assignment);

        return new Success<Assignment>(assignment);
    }

    public async ValueTask<OneOf<Success<Assignment>, NotFound, Forbidden>> EditAsync(long id, AssignmentSettings settings)
    {
        Assignment? assignment = await uow.AssignmentRepository.GetTrackedWithTeachersAsync(id);
        if (assignment is null)
        {
            return new NotFound();
        }
        if (!IsOwnerOrCoTeacherOrAdmin(assignment))
        {
            return new Forbidden();
        }

        bool deadlineChanged = assignment.Deadline != settings.Deadline
                               || assignment.DeadlineKind != settings.DeadlineKind;
        bool titleChanged = !string.Equals(assignment.Title, settings.Title, StringComparison.Ordinal);
        Apply(assignment, settings);
        await uow.SaveChangesAsync();

        if (deadlineChanged)
        {
            OneOf<Success, NotFound, ForgejoError> reconcile = await reconciliation.ReconcileAsync(id);
            reconcile.Switch(
                success => { },
                notFound => { },
                error => logger.LogWarning("Deadline edit for assignment {Id} reconcile failed: {Reason}",
                                           id, error.Reason));

            await notifications.EnqueueDeadlineChangedAsync(assignment);
        }

        if (titleChanged || deadlineChanged)
        {
            await moodle.EnqueueUpdateAsync(assignment);
        }

        return new Success<Assignment>(assignment);
    }

    public async ValueTask<OneOf<Assignment, NotFound, Forbidden>> GetForEditAsync(long id)
    {
        Assignment? assignment = await uow.AssignmentRepository.GetWithTeachersAsync(id);
        if (assignment is null)
        {
            return new NotFound();
        }

        return IsOwnerOrCoTeacherOrAdmin(assignment) ? assignment : new Forbidden();
    }

    public async ValueTask<OneOf<Success, NotFound, Forbidden>> DeleteAsync(long id)
    {
        Assignment? assignment = await uow.AssignmentRepository.GetWithAcceptancesAsync(id);
        if (assignment is null)
        {
            return new NotFound();
        }

        Assignment? teachers = await uow.AssignmentRepository.GetWithTeachersAsync(id);
        if (teachers is null || !IsOwnerOrAdmin(teachers))
        {
            return new Forbidden();
        }

        CascadeResult cascade = await deletion.DeleteAssignmentsAsync([id]);
        await uow.SaveChangesAsync();
        await audit.RecordAsync(AuditAction.AssignmentDeleted, "Assignment", id.ToString(), cascade.ToMetadata());
        await moodle.EnqueueDeleteAsync(id, assignment.CourseId);

        return new Success();
    }

    public async ValueTask<OneOf<Success, NotFound, Forbidden>> AddCoTeacherAsync(long id, long userId)
    {
        Assignment? assignment = await uow.AssignmentRepository.GetTrackedWithTeachersAsync(id);
        if (assignment is null)
        {
            return new NotFound();
        }
        if (!IsOwnerOrAdmin(assignment))
        {
            return new Forbidden();
        }

        User? user = await uow.UserRepository.GetTrackedByIdAsync(userId);
        if (user is null || user.State == UserState.SoftDeleted || user.Role != Role.Teacher)
        {
            return new NotFound();
        }

        if (assignment.CoTeachers.All(t => t.Id != userId) && assignment.OwnerId != userId)
        {
            assignment.CoTeachers.Add(user);
            await uow.SaveChangesAsync();
            await audit.RecordAsync(AuditAction.AssignmentTeacherAdded, "Assignment", id.ToString(),
                                    new Dictionary<string, string> { ["userId"] = userId.ToString() });
        }

        return new Success();
    }

    public async ValueTask<OneOf<Success, NotFound, Forbidden>> RemoveCoTeacherAsync(long id, long userId)
    {
        Assignment? assignment = await uow.AssignmentRepository.GetTrackedWithTeachersAsync(id);
        if (assignment is null)
        {
            return new NotFound();
        }
        if (!IsOwnerOrAdmin(assignment))
        {
            return new Forbidden();
        }
        if (userId == assignment.OwnerId)
        {
            return new Forbidden();
        }

        User? member = assignment.CoTeachers.FirstOrDefault(t => t.Id == userId);
        if (member is not null)
        {
            assignment.CoTeachers.Remove(member);
            await uow.SaveChangesAsync();
            await audit.RecordAsync(AuditAction.AssignmentTeacherRemoved, "Assignment", id.ToString(),
                                    new Dictionary<string, string> { ["userId"] = userId.ToString() });
        }

        return new Success();
    }

    public async ValueTask<OneOf<IReadOnlyCollection<StudentSubmission>, NotFound, Forbidden>> GetStudentListAsync(long id)
    {
        Assignment? assignment = await uow.AssignmentRepository.GetWithTeachersAsync(id);
        if (assignment is null)
        {
            return new NotFound();
        }
        if (!IsOwnerOrCoTeacherOrAdmin(assignment))
        {
            return new Forbidden();
        }

        IReadOnlyCollection<StudentSubmission>? rows = await uow.AssignmentRepository.GetStudentSubmissionsAsync(id);

        return rows is null
            ? new NotFound()
            : OneOf<IReadOnlyCollection<StudentSubmission>, NotFound, Forbidden>.FromT0(rows);
    }

    private void Apply(Assignment assignment, AssignmentSettings settings)
    {
        assignment.Title = settings.Title;
        assignment.Description = settings.Description;
        assignment.HintsInstructions = settings.HintsInstructions;
        assignment.Deadline = settings.Deadline;
        assignment.DeadlineKind = settings.DeadlineKind;
        assignment.HardDeadlineRevokesRead = settings.HardDeadlineRevokesRead;
        assignment.StarterSourceKind = settings.StarterSourceKind;
        assignment.StarterRepoUrl = settings.StarterRepoUrl;
        assignment.ReadmeMarkdown = settings.ReadmeMarkdown;
        assignment.DownloadSnapshotMode = settings.DownloadSnapshotMode;
        assignment.AutoDeleteEnabled = settings.AutoDeleteEnabled;
        assignment.AutoDeleteOn = settings.AutoDeleteEnabled
            ? settings.AutoDeleteOn ?? DefaultAutoDeleteInstant()
            : null;
    }

    private Instant DefaultAutoDeleteInstant() =>
        clock.GetCurrentInstant().InZone(Const.TimeZone).LocalDateTime
             .PlusYears(AutoDeleteDefaultYears).InZoneLeniently(Const.TimeZone).ToInstant();

    private async ValueTask<string> UniqueSlugAsync(string title, long courseId)
    {
        string baseSlug = Slugifier.Slugify(title);
        string slug = baseSlug;
        int suffix = 2;
        while (await uow.AssignmentRepository.ExistsWithSlugInCourseAsync(slug, courseId))
        {
            slug = $"{baseSlug}-{suffix}";
            suffix++;
        }

        return slug;
    }

    private bool IsCourseTeacher(Course course) =>
        currentUser.Roles.Contains(Role.Admin)
        || string.Equals(course.Owner.StudentId, currentUser.StudentId, StringComparison.Ordinal)
        || course.CoTeachers.Any(t => string.Equals(t.StudentId, currentUser.StudentId, StringComparison.Ordinal));

    private bool IsOwnerOrCoTeacherOrAdmin(Assignment assignment) =>
        currentUser.Roles.Contains(Role.Admin)
        || string.Equals(assignment.Owner.StudentId, currentUser.StudentId, StringComparison.Ordinal)
        || assignment.CoTeachers.Any(t => string.Equals(t.StudentId, currentUser.StudentId, StringComparison.Ordinal));

    private bool IsOwnerOrAdmin(Assignment assignment) =>
        currentUser.Roles.Contains(Role.Admin)
        || string.Equals(assignment.Owner.StudentId, currentUser.StudentId, StringComparison.Ordinal);
}
