using LeoClassroom.Services.Auth;
using LeoClassroom.Services.Audit;
using LeoClassroom.Services.Forgejo;
using LeoClassroom.Services;
using LeoClassroom.Persistence.Model;
using LeoClassroom.Persistence.Repositories;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Shared;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OneOf;
using OneOf.Types;

namespace LeoClassroom.Test;

public sealed class CourseTeacherServiceTests
{
    private const long CourseId = 5L;
    private const long OwnerId = 20L;
    private const string OwnerStudentId = "IF000020";
    private const long CoTeacherId = 30L;

    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly ICourseRepository _courseRepo = Substitute.For<ICourseRepository>();
    private readonly IUserRepository _userRepo = Substitute.For<IUserRepository>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IReconciliationService _reconciliation = Substitute.For<IReconciliationService>();
    private readonly IAuditLog _audit = Substitute.For<IAuditLog>();
    private readonly CourseTeacherService _sut;

    public CourseTeacherServiceTests()
    {
        _uow.CourseRepository.Returns(_courseRepo);
        _uow.UserRepository.Returns(_userRepo);
        _currentUser.StudentId.Returns(OwnerStudentId);
        _currentUser.Roles.Returns(new HashSet<Role> { Role.Teacher });
        _userRepo.GetIdByStudentIdAsync(OwnerStudentId).Returns(new ValueTask<long?>(OwnerId));
        _reconciliation.ReconcileCourseAsync(Arg.Any<long>())
                       .Returns(new ValueTask<OneOf<Success, NotFound, ForgejoError>>(new Success()));

        _sut = new CourseTeacherService(_uow, _currentUser, _reconciliation, _audit,
                                        Substitute.For<ILogger<CourseTeacherService>>());
    }

    private Course ArrangeCourse(params User[] coTeachers)
    {
        var course = new Course
        {
            Id = CourseId, Title = "Algo", RosterId = 1, OwnerId = OwnerId,
            Owner = new User { Id = OwnerId, StudentId = OwnerStudentId, FirstName = "O", LastName = "W", Role = Role.Teacher },
            CoTeachers = [.. coTeachers]
        };
        _courseRepo.GetTrackedWithTeachersAsync(CourseId).Returns(new ValueTask<Course?>(course));

        return course;
    }

    private static User Teacher(long id) =>
        new() { Id = id, StudentId = $"IF0000{id}", FirstName = "C", LastName = "T", Role = Role.Teacher };

    [Fact]
    public async Task Owner_AddsCoTeacher_PersistsAuditsAndReconciles()
    {
        Course course = ArrangeCourse();
        _userRepo.GetTrackedByIdAsync(CoTeacherId).Returns(new ValueTask<User?>(Teacher(CoTeacherId)));

        var result = await _sut.AddAsync(CourseId, CoTeacherId);

        result.ShouldBe<Success>();
        course.CoTeachers.Should().ContainSingle(t => t.Id == CoTeacherId);
        await _audit.Received(1).RecordAsync(AuditAction.CourseTeacherAdded, "Course", CourseId.ToString(),
                                             Arg.Any<IReadOnlyDictionary<string, string>?>());
        await _reconciliation.Received(1).ReconcileCourseAsync(CourseId);
    }

    [Fact]
    public async Task NonOwnerNonAdmin_Add_ReturnsForbidden()
    {
        ArrangeCourse();
        _userRepo.GetIdByStudentIdAsync(OwnerStudentId).Returns(new ValueTask<long?>(99L));

        var result = await _sut.AddAsync(CourseId, CoTeacherId);

        result.ShouldBe<Forbidden>();
        await _reconciliation.DidNotReceive().ReconcileCourseAsync(Arg.Any<long>());
    }

    [Fact]
    public async Task Add_NonTeacherUser_ReturnsNotFound()
    {
        ArrangeCourse();
        var student = new User { Id = CoTeacherId, StudentId = "IF000030", FirstName = "S", LastName = "T", Role = Role.Student };
        _userRepo.GetTrackedByIdAsync(CoTeacherId).Returns(new ValueTask<User?>(student));

        var result = await _sut.AddAsync(CourseId, CoTeacherId);

        result.ShouldBe<NotFound>();
    }

    [Fact]
    public async Task RemovingOwner_IsRefused()
    {
        ArrangeCourse();

        var result = await _sut.RemoveAsync(CourseId, OwnerId);

        result.ShouldBe<Forbidden>();
    }

    [Fact]
    public async Task Owner_RemovesCoTeacher_ReconcileEnqueued()
    {
        Course course = ArrangeCourse(Teacher(CoTeacherId));

        var result = await _sut.RemoveAsync(CourseId, CoTeacherId);

        result.ShouldBe<Success>();
        course.CoTeachers.Should().BeEmpty();
        await _reconciliation.Received(1).ReconcileCourseAsync(CourseId);
    }

    [Fact]
    public async Task MissingCourse_ReturnsNotFound()
    {
        _courseRepo.GetTrackedWithTeachersAsync(CourseId).Returns(new ValueTask<Course?>((Course?)null));

        var result = await _sut.AddAsync(CourseId, CoTeacherId);

        result.ShouldBe<NotFound>();
    }
}
