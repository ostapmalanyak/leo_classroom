using LeoClassroom.Services.Auth;
using LeoClassroom.Services.Audit;
using LeoClassroom.Services.Forgejo;
using LeoClassroom.Persistence.Repositories;
using LeoClassroom.Persistence.Model;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Shared;
using OneOf;
using OneOf.Types;

namespace LeoClassroom.Services;

public interface ICourseService
{
    public ValueTask<OneOf<Success<Course>, NotFound, AlreadyExists>>
        AddCourseAsync(string title, long rosterId, string? iconSeed = null);
    public ValueTask<OneOf<Course, NotFound>> GetCourseByIdAsync(long id);
    public ValueTask<OneOf<CourseParticipants, NotFound, Forbidden>> GetParticipantsAsync(long id);
    public ValueTask<OneOf<IReadOnlyCollection<CourseParticipant>, NotFound, Forbidden>>
        SearchAvailableStudentsAsync(long id, string term);
    public ValueTask<OneOf<Success, NotFound, Forbidden>> AddParticipantAsync(long id, long userId);
    public ValueTask<OneOf<Success, NotFound, Forbidden>> RemoveParticipantAsync(long id, long userId);
    public ValueTask<OneOf<Success<Course>, NotFound, AlreadyExists>> AddCourseAsync(string title, long rosterId);
    public ValueTask<OneOf<Course, NotFound, Forbidden>> GetCourseByIdAsync(long id);
    public ValueTask<IReadOnlyCollection<CourseOverview>> GetCourseOverviewsAsync();

    public ValueTask<OneOf<Success<Course>, NotFound, AlreadyExists, Forbidden>>
        UpdateCourseAsync(long id, string title, bool isReadOnly, bool studentsRetainAccess);

    public ValueTask<OneOf<Success, NotFound, Forbidden>> DeleteCourseAsync(long id);

    public readonly record struct AlreadyExists;
}

public sealed record CourseParticipant(long Id, string FirstName, string LastName, string? Class, Role Role);

public sealed record CourseTeacherParticipant(long Id, string FirstName, string LastName, bool IsOwner);

public sealed record CourseParticipants(string Title, IReadOnlyCollection<CourseParticipant> Participants,
                                        IReadOnlyCollection<CourseTeacherParticipant> Teachers);

internal sealed class CourseService(IUnitOfWork uow, ICurrentUser currentUser, IDeletionCascade deletion,
                                    IAuditLog audit, ILogger<CourseService> logger,
                                    IClock clock) : ICourseService
{
    private const int MaxStudentSearchResults = 20;

    public async ValueTask<OneOf<Success<Course>, NotFound, ICourseService.AlreadyExists>>
        AddCourseAsync(string title, long rosterId, string? iconSeed = null)
    {
        Roster? roster = await uow.RosterRepository.GetTrackedWithMembersAsync(rosterId);
        if (roster is null)
        {
            logger.LogWarning("Course creation referenced missing roster {RosterId}", rosterId);

            return new NotFound();
        }

        long? ownerId = await uow.UserRepository.GetIdByStudentIdAsync(currentUser.StudentId);
        if (ownerId is null)
        {
            logger.LogWarning("Acting user {StudentId} has no local account to own the course", currentUser.StudentId);

            return new NotFound();
        }

        if (await uow.CourseRepository.ExistsWithTitleInRosterAsync(title, rosterId))
        {
            logger.LogWarning("Course titled {Title} already exists for roster {RosterId}", title, rosterId);

            return new ICourseService.AlreadyExists();
        }

        var course = new Course
        {
            Title = title,
            IconSeed = iconSeed ?? Guid.NewGuid().ToString("N"),
            RosterId = rosterId,
            Members = roster.Members.ToList(),
            OwnerId = ownerId.Value,
            StudentsRetainAccess = true,
            CreatedAt = clock.GetCurrentInstant()
        };

        uow.CourseRepository.Add(course);
        await uow.SaveChangesAsync();
        logger.LogInformation("Course {CourseId} ({Title}) created for roster {RosterId}", course.Id, title, rosterId);

        return new Success<Course>(course);
    }

    public async ValueTask<OneOf<Course, NotFound, Forbidden>> GetCourseByIdAsync(long id)
    {
        Course? course = await uow.CourseRepository.GetWithTeachersAsync(id);
        if (course is null)
        {
            logger.LogWarning("Course {CourseId} was not found", id);

            return new NotFound();
        }
        if (!IsCourseTeacher(course))
        {
            return new Forbidden();
        }

        return course;
    }

    public async ValueTask<OneOf<CourseParticipants, NotFound, Forbidden>> GetParticipantsAsync(long id)
    {
        Course? course = await uow.CourseRepository.GetWithTeachersAndMembersAsync(id);
        if (course is null)
        {
            return new NotFound();
        }

        bool isCourseTeacher = currentUser.Roles.Contains(Role.Admin)
                               || string.Equals(course.Owner.StudentId, currentUser.StudentId,
                                                StringComparison.Ordinal)
                               || course.CoTeachers.Any(teacher =>
                                   string.Equals(teacher.StudentId, currentUser.StudentId, StringComparison.Ordinal));
        if (!isCourseTeacher && !course.Members.Any(member =>
                string.Equals(member.StudentId, currentUser.StudentId, StringComparison.Ordinal)))
        {
            return new Forbidden();
        }

        CourseParticipant[] participants = course.Members
            .OrderBy(member => member.LastName).ThenBy(member => member.FirstName)
            .Select(member => new CourseParticipant(member.Id, member.FirstName, member.LastName, member.Class,
                                                    member.Role))
            .ToArray();
        CourseTeacherParticipant[] teachers =
        [
            new(course.Owner.Id, course.Owner.FirstName, course.Owner.LastName, true),
            .. course.CoTeachers.OrderBy(teacher => teacher.LastName).ThenBy(teacher => teacher.FirstName)
                       .Select(teacher => new CourseTeacherParticipant(teacher.Id, teacher.FirstName,
                                                                      teacher.LastName, false))
        ];

        return new CourseParticipants(course.Title, participants, teachers);
    }

    public async ValueTask<OneOf<IReadOnlyCollection<CourseParticipant>, NotFound, Forbidden>>
        SearchAvailableStudentsAsync(long id, string term)
    {
        Course? course = await uow.CourseRepository.GetTrackedWithTeachersAndMembersAsync(id);
        if (course is null)
        {
            return new NotFound();
        }

        if (!IsCourseTeacher(course))
        {
            return new Forbidden();
        }

        IReadOnlyCollection<UserSummary> matches = await uow.UserRepository.SearchActiveRosterMembersAsync(
            course.RosterId, term, Role.Student, MaxStudentSearchResults);
        HashSet<long> currentMemberIds = course.Members.Select(member => member.Id).ToHashSet();

        return matches.Where(user => !currentMemberIds.Contains(user.Id))
                      .Select(user => new CourseParticipant(user.Id, user.FirstName, user.LastName, user.Class,
                                                            user.Role))
                      .ToArray();
    }

    public async ValueTask<OneOf<Success, NotFound, Forbidden>> AddParticipantAsync(long id, long userId)
    {
        Course? course = await uow.CourseRepository.GetTrackedWithTeachersAndMembersAsync(id);
        if (course is null)
        {
            return new NotFound();
        }

        if (!IsCourseTeacher(course))
        {
            logger.LogWarning("User {StudentId} forbidden to add participant to course {CourseId}",
                              currentUser.StudentId, id);

            return new Forbidden();
        }

        User? student = await uow.UserRepository.GetTrackedByIdAsync(userId);
        if (student is null || !await uow.UserRepository.IsActiveRosterMemberAsync(course.RosterId, userId,
                                                                                   Role.Student))
        {
            return new NotFound();
        }

        if (course.Members.All(member => member.Id != userId))
        {
            course.Members.Add(student);
            await uow.SaveChangesAsync();
            logger.LogInformation("User {StudentId} added participant {ParticipantId} to course {CourseId}",
                                  currentUser.StudentId, userId, id);
        }

        return new Success();
    }

    public async ValueTask<OneOf<Success, NotFound, Forbidden>> RemoveParticipantAsync(long id, long userId)
    {
        Course? course = await uow.CourseRepository.GetTrackedWithTeachersAndMembersAsync(id);
        if (course is null)
        {
            return new NotFound();
        }

        if (!IsCourseTeacher(course))
        {
            logger.LogWarning("User {StudentId} forbidden to remove participant from course {CourseId}",
                              currentUser.StudentId, id);

            return new Forbidden();
        }

        User? member = course.Members.FirstOrDefault(candidate => candidate.Id == userId);
        if (member is not null)
        {
            course.Members.Remove(member);
            await uow.SaveChangesAsync();
            logger.LogInformation("User {StudentId} removed participant {ParticipantId} from course {CourseId}",
                                  currentUser.StudentId, userId, id);
        }

        return new Success();
    }

    public ValueTask<IReadOnlyCollection<CourseOverview>> GetCourseOverviewsAsync() =>
        uow.CourseRepository.GetOverviewsAsync();
    public async ValueTask<IReadOnlyCollection<CourseOverview>> GetCourseOverviewsAsync()
    {
        if (currentUser.Roles.Contains(Role.Admin))
        {
            return await uow.CourseRepository.GetOverviewsAsync();
        }

        long? userId = await uow.UserRepository.GetIdByStudentIdAsync(currentUser.StudentId);

        return userId is null
            ? Array.Empty<CourseOverview>()
            : await uow.CourseRepository.GetOverviewsForTeacherAsync(userId.Value);
    }

    public async ValueTask<OneOf<Success<Course>, NotFound, ICourseService.AlreadyExists, Forbidden>>
        UpdateCourseAsync(long id, string title, bool isReadOnly, bool studentsRetainAccess)
    {
        var course = await uow.CourseRepository.GetTrackedByIdAsync(id);
        if (course is null)
        {
            logger.LogWarning("Course {CourseId} to update was not found", id);

            return new NotFound();
        }

        if (!IsOwnerOrAdmin(course.Owner.StudentId))
        {
            logger.LogWarning("User {StudentId} forbidden to update course {CourseId}", currentUser.StudentId, id);

            return new Forbidden();
        }

        if (!string.Equals(course.Title, title, StringComparison.Ordinal)
            && await uow.CourseRepository.ExistsWithTitleInRosterAsync(title, course.RosterId, id))
        {
            logger.LogWarning("Course rename to {Title} conflicts within roster {RosterId}", title, course.RosterId);

            return new ICourseService.AlreadyExists();
        }

        course.Title = title;
        course.IsReadOnly = isReadOnly;
        course.StudentsRetainAccess = studentsRetainAccess;
        await uow.SaveChangesAsync();
        logger.LogInformation("Course {CourseId} updated", id);

        return new Success<Course>(course);
    }

    public async ValueTask<OneOf<Success, NotFound, Forbidden>> DeleteCourseAsync(long id)
    {
        Course? course = await uow.CourseRepository.GetWithTeachersAsync(id);
        if (course is null)
        {
            logger.LogWarning("Course {CourseId} to delete was not found", id);

            return new NotFound();
        }

        bool isTeacher = string.Equals(course.Owner.StudentId, currentUser.StudentId, StringComparison.Ordinal)
                         || course.CoTeachers.Any(teacher =>
                             string.Equals(teacher.StudentId, currentUser.StudentId, StringComparison.Ordinal));
        if (!currentUser.Roles.Contains(Role.Admin) && !isTeacher)
        {
            logger.LogWarning("User {StudentId} forbidden to delete course {CourseId}", currentUser.StudentId, id);

            return new Forbidden();
        }

        CascadeResult cascade = await deletion.DeleteCoursesAsync([id]);

        logger.LogInformation("Course {CourseId} deleted with {Assignments} assignments and {Acceptances} submissions",
                              id, cascade.Assignments, cascade.Acceptances);
        await audit.RecordAsync(AuditAction.CourseDeleted, "Course", id.ToString(), cascade.ToMetadata());

        return new Success();
    }

    private bool IsOwnerOrAdmin(string ownerStudentId) =>
        currentUser.Roles.Contains(Role.Admin)
        || string.Equals(ownerStudentId, currentUser.StudentId, StringComparison.Ordinal);

    private bool IsCourseTeacher(Course course) =>
        currentUser.Roles.Contains(Role.Admin)
        || string.Equals(course.Owner.StudentId, currentUser.StudentId, StringComparison.Ordinal)
        || course.CoTeachers.Any(teacher =>
            string.Equals(teacher.StudentId, currentUser.StudentId, StringComparison.Ordinal));
        || course.CoTeachers.Any(t => string.Equals(t.StudentId, currentUser.StudentId, StringComparison.Ordinal));
}
