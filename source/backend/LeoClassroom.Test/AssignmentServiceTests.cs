using LeoClassroom.Services.Audit;
using LeoClassroom.Services.Auth;
using LeoClassroom.Services.Forgejo;
using LeoClassroom.Services.Moodle;
using LeoClassroom.Services.Notifications;
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

public sealed class AssignmentServiceTests
{
    private const long CourseId = 7L;
    private const long AssignmentId = 11L;
    private const long OwnerId = 20L;
    private const string OwnerStudentId = "IF000020";
    private const string OtherStudentId = "IF000099";

    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly ICourseRepository _courseRepo = Substitute.For<ICourseRepository>();
    private readonly IAssignmentRepository _assignmentRepo = Substitute.For<IAssignmentRepository>();
    private readonly IUserRepository _userRepo = Substitute.For<IUserRepository>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IReconciliationService _reconciliation = Substitute.For<IReconciliationService>();
    private readonly IDeletionCascade _cascade = Substitute.For<IDeletionCascade>();
    private readonly IForgejoClient _forgejo = Substitute.For<IForgejoClient>();
    private readonly IAuditLog _audit = Substitute.For<IAuditLog>();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly IMoodleSyncService _moodle = Substitute.For<IMoodleSyncService>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly AssignmentService _sut;

    public AssignmentServiceTests()
    {
        _uow.CourseRepository.Returns(_courseRepo);
        _uow.AssignmentRepository.Returns(_assignmentRepo);
        _uow.UserRepository.Returns(_userRepo);
        _currentUser.StudentId.Returns(OwnerStudentId);
        _currentUser.Roles.Returns(new HashSet<Role> { Role.Teacher });
        _userRepo.GetIdByStudentIdAsync(OwnerStudentId).Returns(new ValueTask<long?>(OwnerId));
        _clock.GetCurrentInstant().Returns(Instant.FromUtc(2026, 6, 13, 12, 0));
        _cascade.DeleteAssignmentsAsync(Arg.Any<IReadOnlyCollection<long>>())
                .Returns(new ValueTask<CascadeResult>(CascadeResult.Empty));
        _reconciliation.ReconcileAsync(Arg.Any<long>())
                       .Returns(new ValueTask<OneOf<Success, NotFound, ForgejoError>>(new Success()));

        _sut = new AssignmentService(_uow, _currentUser, _reconciliation, _cascade, _audit, _notifications, _moodle,
                                     _clock, Substitute.For<ILogger<AssignmentService>>());
    }

    private static AssignmentSettings Settings(DeadlineKind kind = DeadlineKind.None, Instant? deadline = null) =>
        new("Algorithms", null, null, deadline, kind, false, StarterSourceKind.DescriptionOnly, null, null,
            false, null, DownloadSnapshotMode.Deadline);

    private Course CourseOwnedByCurrent() => new()
    {
        Id = CourseId, Title = "Course", RosterId = 1, OwnerId = OwnerId,
        Owner = new User { Id = OwnerId, StudentId = OwnerStudentId, FirstName = "O", LastName = "W", Role = Role.Teacher },
        CoTeachers = []
    };

    private Assignment AssignmentOwnedByCurrent() => new()
    {
        Id = AssignmentId, CourseId = CourseId, Title = "Algorithms", Slug = "algorithms", OwnerId = OwnerId,
        DeadlineKind = DeadlineKind.None,
        Owner = new User { Id = OwnerId, StudentId = OwnerStudentId, FirstName = "O", LastName = "W", Role = Role.Teacher },
        CoTeachers = []
    };

    [Fact]
    public async Task Create_ByCourseTeacher_SetsOwnerAndSlug()
    {
        _courseRepo.GetWithTeachersAsync(CourseId).Returns(new ValueTask<Course?>(CourseOwnedByCurrent()));
        _assignmentRepo.ExistsWithSlugInCourseAsync(Arg.Any<string>(), CourseId).Returns(new ValueTask<bool>(false));

        var result = await _sut.CreateAsync(CourseId, Settings());

        Assignment created = result.ShouldBe<Success<Assignment>>().Value;
        created.OwnerId.Should().Be(OwnerId);
        created.Slug.Should().Be("algorithms");
        _assignmentRepo.Received(1).Add(Arg.Is<Assignment>(a => a.OwnerId == OwnerId));
        await _audit.Received(1).RecordAsync(AuditAction.AssignmentCreated, "Assignment", Arg.Any<string>(),
                                             Arg.Any<IReadOnlyDictionary<string, string>?>());
    }

    [Fact]
    public async Task Create_ByNonCourseTeacher_ReturnsForbidden()
    {
        Course course = CourseOwnedByCurrent();
        course.Owner.StudentId = OtherStudentId;
        _courseRepo.GetWithTeachersAsync(CourseId).Returns(new ValueTask<Course?>(course));

        var result = await _sut.CreateAsync(CourseId, Settings());

        result.ShouldBe<Forbidden>();
        _assignmentRepo.DidNotReceive().Add(Arg.Any<Assignment>());
    }

    [Fact]
    public async Task Create_MissingCourse_ReturnsNotFound()
    {
        _courseRepo.GetWithTeachersAsync(CourseId).Returns(new ValueTask<Course?>((Course?)null));

        var result = await _sut.CreateAsync(CourseId, Settings());

        result.ShouldBe<NotFound>();
    }

    [Fact]
    public async Task Edit_ChangingDeadline_EnqueuesReconcile()
    {
        _assignmentRepo.GetTrackedWithTeachersAsync(AssignmentId)
                       .Returns(new ValueTask<Assignment?>(AssignmentOwnedByCurrent()));

        var result = await _sut.EditAsync(AssignmentId,
            Settings(DeadlineKind.Hard, Instant.FromUtc(2026, 7, 1, 0, 0)));

        result.ShouldBe<Success<Assignment>>();
        await _reconciliation.Received(1).ReconcileAsync(AssignmentId);
    }

    [Fact]
    public async Task Edit_NoDeadlineChange_DoesNotReconcile()
    {
        _assignmentRepo.GetTrackedWithTeachersAsync(AssignmentId)
                       .Returns(new ValueTask<Assignment?>(AssignmentOwnedByCurrent()));

        var result = await _sut.EditAsync(AssignmentId, Settings());

        result.ShouldBe<Success<Assignment>>();
        await _reconciliation.DidNotReceive().ReconcileAsync(Arg.Any<long>());
    }

    [Fact]
    public async Task Edit_ByUnrelatedTeacher_ReturnsForbidden()
    {
        Assignment assignment = AssignmentOwnedByCurrent();
        assignment.Owner.StudentId = OtherStudentId;
        _assignmentRepo.GetTrackedWithTeachersAsync(AssignmentId).Returns(new ValueTask<Assignment?>(assignment));

        var result = await _sut.EditAsync(AssignmentId, Settings());

        result.ShouldBe<Forbidden>();
    }

    [Fact]
    public async Task Edit_AutoDeleteEnabledWithoutDate_DefaultsTwoYears()
    {
        Assignment assignment = AssignmentOwnedByCurrent();
        _assignmentRepo.GetTrackedWithTeachersAsync(AssignmentId).Returns(new ValueTask<Assignment?>(assignment));
        var settings = new AssignmentSettings("Algorithms", null, null, null, DeadlineKind.None, false,
            StarterSourceKind.DescriptionOnly, null, null, true, null, DownloadSnapshotMode.Deadline);

        var result = await _sut.EditAsync(AssignmentId, settings);

        result.ShouldBe<Success<Assignment>>();
        assignment.AutoDeleteOn.Should().NotBeNull();
        assignment.AutoDeleteOn!.Value.Should().BeGreaterThan(Instant.FromUtc(2028, 6, 12, 0, 0));
    }

    [Fact]
    public async Task Delete_ByCoTeacher_ReturnsForbidden()
    {
        Assignment assignment = AssignmentOwnedByCurrent();
        assignment.Owner.StudentId = OtherStudentId;
        assignment.CoTeachers.Add(new User
        {
            Id = 30L, StudentId = OwnerStudentId, FirstName = "C", LastName = "T", Role = Role.Teacher
        });
        _assignmentRepo.GetWithAcceptancesAsync(AssignmentId).Returns(new ValueTask<Assignment?>(assignment));
        _assignmentRepo.GetWithTeachersAsync(AssignmentId).Returns(new ValueTask<Assignment?>(assignment));

        var result = await _sut.DeleteAsync(AssignmentId);

        result.ShouldBe<Forbidden>();
        await _assignmentRepo.DidNotReceive().DeleteByIdAsync(Arg.Any<long>());
    }

    [Fact]
    public async Task Delete_ByOwner_CascadesAndAudits()
    {
        Assignment withAcceptances = AssignmentOwnedByCurrent();
        withAcceptances.Acceptances.Add(new Acceptance
        {
            Id = 1, AssignmentId = AssignmentId, StudentId = 50L, RepoOwner = "course-7", RepoName = "course-algorithms-IF1"
        });
        _assignmentRepo.GetWithAcceptancesAsync(AssignmentId).Returns(new ValueTask<Assignment?>(withAcceptances));
        _assignmentRepo.GetWithTeachersAsync(AssignmentId).Returns(new ValueTask<Assignment?>(AssignmentOwnedByCurrent()));
        var result = await _sut.DeleteAsync(AssignmentId);

        result.ShouldBe<Success>();

        // the cascade owns the ordering that keeps Forgejo in step, so the service delegates rather than deleting
        await _cascade.Received(1).DeleteAssignmentsAsync(
            Arg.Is<IReadOnlyCollection<long>>(ids => ids.Contains(AssignmentId)));
        await _assignmentRepo.DidNotReceive().DeleteByIdAsync(Arg.Any<long>());
        await _audit.Received(1).RecordAsync(AuditAction.AssignmentDeleted, "Assignment", "11",
                                             Arg.Any<IReadOnlyDictionary<string, string>?>());
    }

    [Fact]
    public async Task AddCoTeacher_NonTeacherUser_ReturnsNotFound()
    {
        _assignmentRepo.GetTrackedWithTeachersAsync(AssignmentId)
                       .Returns(new ValueTask<Assignment?>(AssignmentOwnedByCurrent()));
        _userRepo.GetTrackedByIdAsync(30L).Returns(new ValueTask<User?>(new User
        {
            Id = 30L, StudentId = "IF000030", FirstName = "S", LastName = "T", Role = Role.Student
        }));

        var result = await _sut.AddCoTeacherAsync(AssignmentId, 30L);

        result.ShouldBe<NotFound>();
    }

    [Fact]
    public async Task GetStudentList_ByUnrelatedTeacher_ReturnsForbidden()
    {
        Assignment assignment = AssignmentOwnedByCurrent();
        assignment.Owner.StudentId = OtherStudentId;
        _assignmentRepo.GetWithTeachersAsync(AssignmentId).Returns(new ValueTask<Assignment?>(assignment));

        var result = await _sut.GetStudentListAsync(AssignmentId);

        result.ShouldBe<Forbidden>();
    }
}
