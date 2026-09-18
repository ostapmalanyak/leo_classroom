using LeoClassroom.Services.Audit;
using LeoClassroom.Services.Auth;
using LeoClassroom.Services.Forgejo;
using LeoClassroom.Persistence.Model;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Shared;
using OneOf;
using OneOf.Types;

namespace LeoClassroom.Services;

public sealed record CourseTeachers(UserSummary Owner, IReadOnlyList<UserSummary> CoTeachers);

public interface ICourseTeacherService
{
    public ValueTask<OneOf<CourseTeachers, NotFound>> ListAsync(long courseId);
    public ValueTask<OneOf<Success, NotFound, Forbidden>> AddAsync(long courseId, long userId);
    public ValueTask<OneOf<Success, NotFound, Forbidden>> RemoveAsync(long courseId, long userId);
}

internal sealed class CourseTeacherService(
    IUnitOfWork uow, ICurrentUser currentUser, IReconciliationService reconciliation, IAuditLog auditLog,
    ILogger<CourseTeacherService> logger) : ICourseTeacherService
{
    public async ValueTask<OneOf<CourseTeachers, NotFound>> ListAsync(long courseId)
    {
        Course? course = await uow.CourseRepository.GetTrackedWithTeachersAsync(courseId);
        if (course is null)
        {
            return new NotFound();
        }

        return new CourseTeachers(Summary(course.Owner),
                                  [.. course.CoTeachers.OrderBy(t => t.LastName).Select(Summary)]);
    }

    public async ValueTask<OneOf<Success, NotFound, Forbidden>> AddAsync(long courseId, long userId)
    {
        Course? course = await uow.CourseRepository.GetTrackedWithTeachersAsync(courseId);
        if (course is null)
        {
            return new NotFound();
        }
        if (!await IsOwnerOrAdminAsync(course))
        {
            return new Forbidden();
        }

        User? teacher = await uow.UserRepository.GetTrackedByIdAsync(userId);
        if (teacher is null || teacher.State == UserState.SoftDeleted || teacher.Role != Role.Teacher)
        {
            return new NotFound();
        }

        if (teacher.Id != course.OwnerId && course.CoTeachers.All(t => t.Id != userId))
        {
            course.CoTeachers.Add(teacher);
            await uow.SaveChangesAsync();
            await auditLog.RecordAsync(AuditAction.CourseTeacherAdded, "Course", courseId.ToString(),
                                       new Dictionary<string, string> { ["teacher"] = teacher.StudentId });
            await EnqueueReconcileAsync(courseId);
        }

        return new Success();
    }

    public async ValueTask<OneOf<Success, NotFound, Forbidden>> RemoveAsync(long courseId, long userId)
    {
        Course? course = await uow.CourseRepository.GetTrackedWithTeachersAsync(courseId);
        if (course is null)
        {
            return new NotFound();
        }
        if (!await IsOwnerOrAdminAsync(course))
        {
            return new Forbidden();
        }
        if (userId == course.OwnerId)
        {
            return new Forbidden();
        }

        User? member = course.CoTeachers.FirstOrDefault(t => t.Id == userId);
        if (member is not null)
        {
            course.CoTeachers.Remove(member);
            await uow.SaveChangesAsync();
            await auditLog.RecordAsync(AuditAction.CourseTeacherRemoved, "Course", courseId.ToString(),
                                       new Dictionary<string, string> { ["teacher"] = member.StudentId });
            await EnqueueReconcileAsync(courseId);
        }

        return new Success();
    }

    private async ValueTask EnqueueReconcileAsync(long courseId)
    {
        OneOf<Success, NotFound, ForgejoError> result = await reconciliation.ReconcileCourseAsync(courseId);
        result.Switch(
            success => { },
            notFound => { },
            error => logger.LogWarning("Teacher change on course {CourseId} reconciled with error: {Reason}",
                                       courseId, error.Reason));
    }

    private async ValueTask<bool> IsOwnerOrAdminAsync(Course course)
    {
        if (currentUser.Roles.Contains(Role.Admin))
        {
            return true;
        }

        long? actingId = await uow.UserRepository.GetIdByStudentIdAsync(currentUser.StudentId);

        return actingId is not null && course.OwnerId == actingId;
    }

    private static UserSummary Summary(User user) =>
        new(user.Id, user.StudentId, user.FirstName, user.LastName, user.Class, user.Role);
}
