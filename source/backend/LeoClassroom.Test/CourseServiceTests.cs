using LeoClassroom.Services.Audit;
using LeoClassroom.Services.Auth;
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

public sealed class CourseServiceTests
{
    private const long RosterId = 10L;
    private const long OwnerId = 20L;
    private const string OwnerStudentId = "IF000001";
    private const string OtherStudentId = "IF000999";

    private readonly IUnitOfWork _uow;
    private readonly ICourseRepository _courseRepo;
    private readonly IRosterRepository _rosterRepo;
    private readonly IUserRepository _userRepo;
    private readonly IAssignmentRepository _assignmentRepo;
    private readonly IAcceptanceRepository _acceptanceRepo;
    private readonly IForgejoClient _forgejo;
    private readonly IDeletionCascade _cascade = Substitute.For<IDeletionCascade>();
    private readonly IAuditLog _audit;
    private readonly ICurrentUser _currentUser;
    private readonly CourseService _sut;

    public CourseServiceTests()
    {
        _uow = Substitute.For<IUnitOfWork>();
        _courseRepo = Substitute.For<ICourseRepository>();
        _rosterRepo = Substitute.For<IRosterRepository>();
        _userRepo = Substitute.For<IUserRepository>();
        _uow.CourseRepository.Returns(_courseRepo);
        _uow.RosterRepository.Returns(_rosterRepo);
        _uow.UserRepository.Returns(_userRepo);

        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.StudentId.Returns(OwnerStudentId);
        _currentUser.Roles.Returns(new HashSet<Role> { Role.Teacher });

        var clock = Substitute.For<IClock>();
        clock.GetCurrentInstant().Returns(Instant.FromUtc(2026, 3, 1, 12, 0, 0));

        _assignmentRepo = Substitute.For<IAssignmentRepository>();
        _acceptanceRepo = Substitute.For<IAcceptanceRepository>();
        _uow.AssignmentRepository.Returns(_assignmentRepo);
        _uow.AcceptanceRepository.Returns(_acceptanceRepo);
        _assignmentRepo.GetIdsByCoursesAsync(Arg.Any<IReadOnlyCollection<long>>())
                       .Returns(new ValueTask<IReadOnlyCollection<long>>([]));
        _acceptanceRepo.GetByAssignmentsAsync(Arg.Any<IReadOnlyCollection<long>>())
                       .Returns(new ValueTask<IReadOnlyCollection<AcceptanceRepoRef>>([]));

        _forgejo = Substitute.For<IForgejoClient>();
        _forgejo.DeleteRepoAsync(Arg.Any<string>(), Arg.Any<string>())
                .Returns(new ValueTask<OneOf<Success, ForgejoError>>(new Success()));

        _cascade.DeleteCoursesAsync(Arg.Any<IReadOnlyCollection<long>>())
                .Returns(new ValueTask<CascadeResult>(CascadeResult.Empty));
        _audit = Substitute.For<IAuditLog>();

        _sut = new CourseService(_uow, _currentUser, _cascade, _audit,
                                 Substitute.For<ILogger<CourseService>>(), clock);
    }

    private static Course OwnedCourse(string ownerStudentId) => new()
    {
        Id = 5,
        Title = "Algorithms",
        RosterId = RosterId,
        OwnerId = OwnerId,
        Owner = new User { StudentId = ownerStudentId, FirstName = "T", LastName = "T", Role = Role.Teacher }
    };

    private void ArrangeValidCreate()
    {
        _rosterRepo.ExistsAsync(RosterId).Returns(new ValueTask<bool>(true));
        _userRepo.GetIdByStudentIdAsync(OwnerStudentId).Returns(new ValueTask<long?>(OwnerId));
        _courseRepo.ExistsWithTitleInRosterAsync("Algorithms", RosterId).Returns(new ValueTask<bool>(false));
    }

    [Fact]
    public async Task AddCourseAsync_Valid_ReturnsSuccessWithActingOwner()
    {
        ArrangeValidCreate();

        var result = await _sut.AddCourseAsync("Algorithms", RosterId);

        result.Switch(
            success => success.Value.OwnerId.Should().Be(OwnerId),
            notFound => Assert.Fail("Expected Success but got NotFound"),
            alreadyExists => Assert.Fail("Expected Success but got AlreadyExists"));
        _courseRepo.Received(1).Add(Arg.Any<Course>());
        await _uow.Received(1).SaveChangesAsync();
    }

    [Fact]
    public async Task AddCourseAsync_MissingRoster_ReturnsNotFound()
    {
        _rosterRepo.ExistsAsync(RosterId).Returns(new ValueTask<bool>(false));

        var result = await _sut.AddCourseAsync("Algorithms", RosterId);

        result.Switch(
            success => Assert.Fail("Expected NotFound but got Success"),
            notFound => { },
            alreadyExists => Assert.Fail("Expected NotFound but got AlreadyExists"));
    }

    [Fact]
    public async Task AddCourseAsync_DuplicateTitleInRoster_ReturnsAlreadyExists()
    {
        ArrangeValidCreate();
        _courseRepo.ExistsWithTitleInRosterAsync("Algorithms", RosterId).Returns(new ValueTask<bool>(true));

        var result = await _sut.AddCourseAsync("Algorithms", RosterId);

        result.Switch(
            success => Assert.Fail("Expected AlreadyExists but got Success"),
            notFound => Assert.Fail("Expected AlreadyExists but got NotFound"),
            alreadyExists => { });
    }

    [Fact]
    public async Task GetCourseByIdAsync_Existing_ReturnsCourse()
    {
        _courseRepo.GetByIdAsync(5).Returns(new ValueTask<Course?>(OwnedCourse(OwnerStudentId)));

        var result = await _sut.GetCourseByIdAsync(5);

        result.Switch(
            c => c.Id.Should().Be(5),
            notFound => Assert.Fail("Expected Course but got NotFound"));
    }

    [Fact]
    public async Task GetCourseByIdAsync_Missing_ReturnsNotFound()
    {
        _courseRepo.GetByIdAsync(99).Returns(new ValueTask<Course?>(default(Course?)));

        var result = await _sut.GetCourseByIdAsync(99);

        result.Switch(
            c => Assert.Fail("Expected NotFound but got Course"),
            notFound => { });
    }

    [Fact]
    public async Task UpdateCourseAsync_OwnerEditsOwnCourse_ReturnsSuccess()
    {
        _courseRepo.GetTrackedByIdAsync(5).Returns(new ValueTask<Course?>(OwnedCourse(OwnerStudentId)));

        var result = await _sut.UpdateCourseAsync(5, "Algorithms II", isReadOnly: true, studentsRetainAccess: false);

        result.Switch(
            success => success.Value.Title.Should().Be("Algorithms II"),
            notFound => Assert.Fail("Expected Success but got NotFound"),
            alreadyExists => Assert.Fail("Expected Success but got AlreadyExists"),
            forbidden => Assert.Fail("Expected Success but got Forbidden"));
        await _uow.Received(1).SaveChangesAsync();
    }

    [Fact]
    public async Task UpdateCourseAsync_NonOwnerTeacher_ReturnsForbidden()
    {
        _courseRepo.GetTrackedByIdAsync(5).Returns(new ValueTask<Course?>(OwnedCourse(OtherStudentId)));

        var result = await _sut.UpdateCourseAsync(5, "X", false, true);

        result.Switch(
            success => Assert.Fail("Expected Forbidden but got Success"),
            notFound => Assert.Fail("Expected Forbidden but got NotFound"),
            alreadyExists => Assert.Fail("Expected Forbidden but got AlreadyExists"),
            forbidden => { });
        await _uow.DidNotReceive().SaveChangesAsync();
    }

    [Fact]
    public async Task UpdateCourseAsync_AdminNonOwner_ReturnsSuccess()
    {
        _currentUser.Roles.Returns(new HashSet<Role> { Role.Admin });
        _courseRepo.GetTrackedByIdAsync(5).Returns(new ValueTask<Course?>(OwnedCourse(OtherStudentId)));

        var result = await _sut.UpdateCourseAsync(5, "Algorithms II", false, true);

        result.Switch(
            success => { },
            notFound => Assert.Fail("Expected Success but got NotFound"),
            alreadyExists => Assert.Fail("Expected Success but got AlreadyExists"),
            forbidden => Assert.Fail("Expected Success but got Forbidden"));
    }

    [Fact]
    public async Task UpdateCourseAsync_Missing_ReturnsNotFound()
    {
        _courseRepo.GetTrackedByIdAsync(99).Returns(new ValueTask<Course?>(default(Course?)));

        var result = await _sut.UpdateCourseAsync(99, "X", false, true);

        result.Switch(
            success => Assert.Fail("Expected NotFound but got Success"),
            notFound => { },
            alreadyExists => Assert.Fail("Expected NotFound but got AlreadyExists"),
            forbidden => Assert.Fail("Expected NotFound but got Forbidden"));
    }

    [Fact]
    public async Task DeleteCourseAsync_Owner_ReturnsSuccess()
    {
        _courseRepo.GetOwnerStudentIdAsync(5).Returns(new ValueTask<string?>(OwnerStudentId));
        _courseRepo.DeleteByIdAsync(5).Returns(new ValueTask<bool>(true));

        var result = await _sut.DeleteCourseAsync(5);

        result.Switch(
            success => { },
            notFound => Assert.Fail("Expected Success but got NotFound"),
            forbidden => Assert.Fail("Expected Success but got Forbidden"));
    }

    [Fact]
    public async Task DeleteCourseAsync_NonOwnerTeacher_ReturnsForbidden()
    {
        _courseRepo.GetOwnerStudentIdAsync(5).Returns(new ValueTask<string?>(OtherStudentId));

        var result = await _sut.DeleteCourseAsync(5);

        result.Switch(
            success => Assert.Fail("Expected Forbidden but got Success"),
            notFound => Assert.Fail("Expected Forbidden but got NotFound"),
            forbidden => { });
        await _courseRepo.DidNotReceive().DeleteByIdAsync(Arg.Any<long>());
    }

    [Fact]
    public async Task DeleteCourseAsync_Missing_ReturnsNotFound()
    {
        _courseRepo.GetOwnerStudentIdAsync(99).Returns(new ValueTask<string?>(default(string?)));

        var result = await _sut.DeleteCourseAsync(99);

        result.Switch(
            success => Assert.Fail("Expected NotFound but got Success"),
            notFound => { },
            forbidden => Assert.Fail("Expected NotFound but got Forbidden"));
    }

    [Fact]
    public async Task GetCourseOverviewsAsync_ReturnsRepositoryResult()
    {
        IReadOnlyCollection<CourseOverview> overviews =
        [
            new CourseOverview(1, "Algorithms", RosterId, "5AHIF", 25, false, true)
        ];
        _courseRepo.GetOverviewsAsync()
                   .Returns(new ValueTask<IReadOnlyCollection<CourseOverview>>(overviews));

        var result = await _sut.GetCourseOverviewsAsync();

        result.Should().HaveCount(1);
    }

    [Fact]
    public async Task DeleteCourseAsync_GoesThroughTheCascadeSoForgejoStaysInStep()
    {
        _courseRepo.GetOwnerStudentIdAsync(5).Returns(new ValueTask<string?>(OwnerStudentId));

        var result = await _sut.DeleteCourseAsync(5);

        result.ShouldBe<Success>();

        // the service must not delete the row itself: the course's assignments, their submissions, the Forgejo
        // repositories behind them and the organisation all have to go together
        await _cascade.Received(1).DeleteCoursesAsync(Arg.Is<IReadOnlyCollection<long>>(ids => ids.Contains(5L)));
        await _courseRepo.DidNotReceive().DeleteByIdAsync(Arg.Any<long>());
    }

    [Fact]
    public async Task DeleteCourseAsync_RecordsWhatTheCascadeRemoved()
    {
        _courseRepo.GetOwnerStudentIdAsync(5).Returns(new ValueTask<string?>(OwnerStudentId));
        _cascade.DeleteCoursesAsync(Arg.Any<IReadOnlyCollection<long>>())
                .Returns(new ValueTask<CascadeResult>(new CascadeResult(1, 2, 3, 3, 1, 0)));

        await _sut.DeleteCourseAsync(5);

        await _audit.Received(1).RecordAsync(AuditAction.CourseDeleted, "Course", "5",
                                             Arg.Is<IReadOnlyDictionary<string, string>>(
                                                 m => m["assignments"] == "2" && m["acceptances"] == "3"
                                                      && m["organisations"] == "1"));
    }
}
