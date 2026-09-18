using LeoClassroom.Services;
using LeoClassroom.Services.Audit;
using LeoClassroom.Services.Auth;
using LeoClassroom.Services.Forgejo;
using LeoClassroom.Services.Util;
using LeoClassroom.Persistence.Repositories;
using LeoClassroom.Persistence.Model;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using OneOf;
using OneOf.Types;

namespace LeoClassroom.Test;

public sealed class UserDeletionServiceTests
{
    private const long UserId = 7;
    private const string StudentId = "IF000007";
    private static readonly Instant Now = Instant.FromUtc(2026, 3, 1, 2, 0, 0);

    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly ICourseRepository _courses = Substitute.For<ICourseRepository>();
    private readonly IAssignmentRepository _assignments = Substitute.For<IAssignmentRepository>();
    private readonly IRosterRepository _rosters = Substitute.For<IRosterRepository>();
    private readonly IAcceptanceRepository _acceptances = Substitute.For<IAcceptanceRepository>();
    private readonly IForgejoClient _forgejo = Substitute.For<IForgejoClient>();
    private readonly IDeletionCascade _cascade = Substitute.For<IDeletionCascade>();
    private readonly INotificationRepository _notifications = Substitute.For<INotificationRepository>();
    private readonly INotificationPreferenceRepository _preferences =
        Substitute.For<INotificationPreferenceRepository>();
    private readonly IUserProvisioningCache _cache = Substitute.For<IUserProvisioningCache>();
    private readonly IAuditLog _audit = Substitute.For<IAuditLog>();
    private readonly UserDeletionService _sut;

    public UserDeletionServiceTests()
    {
        _uow.UserRepository.Returns(_users);
        _uow.CourseRepository.Returns(_courses);
        _uow.AssignmentRepository.Returns(_assignments);
        _uow.RosterRepository.Returns(_rosters);
        _uow.AcceptanceRepository.Returns(_acceptances);

        _forgejo.DeleteRepoAsync(Arg.Any<string>(), Arg.Any<string>())
                .Returns(new ValueTask<OneOf<Success, ForgejoError>>(new Success()));
        _forgejo.SetUserActiveAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<long>())
                .Returns(new ValueTask<OneOf<Success, ForgejoError>>(new Success()));

        _cascade.DeleteAcceptancesAsync(Arg.Any<IReadOnlyCollection<AcceptanceRepoRef>>())
                .Returns(ci => new ValueTask<CascadeResult>(
                    new CascadeResult(0, 0, ci.Arg<IReadOnlyCollection<AcceptanceRepoRef>>().Count,
                                      ci.Arg<IReadOnlyCollection<AcceptanceRepoRef>>().Count, 0, 0)));
        _cascade.DeleteAssignmentsAsync(Arg.Any<IReadOnlyCollection<long>>())
                .Returns(ci => new ValueTask<CascadeResult>(
                    new CascadeResult(0, ci.Arg<IReadOnlyCollection<long>>().Count, 0, 0, 0, 0)));
        _cascade.DeleteCoursesAsync(Arg.Any<IReadOnlyCollection<long>>())
                .Returns(ci => new ValueTask<CascadeResult>(
                    new CascadeResult(ci.Arg<IReadOnlyCollection<long>>().Count, 0, 0, 0, 0, 0)));
        _courses.GetCoTaughtForgejoOrgsAsync(Arg.Any<long>())
                .Returns(new ValueTask<IReadOnlyCollection<string>>([]));
        _uow.NotificationRepository.Returns(_notifications);
        _uow.NotificationPreferenceRepository.Returns(_preferences);

        ArrangeClosure();

        var clock = Substitute.For<IClock>();
        clock.GetCurrentInstant().Returns(Now);

        _sut = new UserDeletionService(_uow, _cascade, _forgejo, _cache, _audit, clock,
                                       Options.Create(new ForgejoSettings
                                       {
                                           BaseUrl = "http://f", AdminToken = "x", WebhookSecret = "w",
                                           WebhookTargetUrl = "http://f/hook", AuthSourceId = 9
                                       }),
                                       Substitute.For<ILogger<UserDeletionService>>());
    }

    private static User Existing(UserState state = UserState.Active, Instant? lastSeen = null) =>
        new()
        {
            Id = UserId, StudentId = StudentId, FirstName = "Sam", LastName = "Student", Role = Role.Student,
            State = state, LdapLastSeen = lastSeen
        };

    private void ArrangeUser(User? user) => _users.GetByIdAsync(UserId).Returns(new ValueTask<User?>(user));

    private void ArrangeClosure(
        long[]? ownedRosters = null, long[]? ownedCourses = null, long[]? coursesUsingRosters = null,
        long[]? ownedAssignments = null, long[]? assignmentsInCourses = null,
        AcceptanceRepoRef[]? acceptances = null)
    {
        _rosters.GetIdsOwnedByAsync(UserId)
                .Returns(new ValueTask<IReadOnlyCollection<long>>(ownedRosters ?? []));
        _courses.GetIdsOwnedByAsync(UserId)
                .Returns(new ValueTask<IReadOnlyCollection<long>>(ownedCourses ?? []));
        _courses.GetIdsUsingRostersAsync(Arg.Any<IReadOnlyCollection<long>>())
                .Returns(new ValueTask<IReadOnlyCollection<long>>(coursesUsingRosters ?? []));
        _assignments.GetIdsOwnedByAsync(UserId)
                    .Returns(new ValueTask<IReadOnlyCollection<long>>(ownedAssignments ?? []));
        _assignments.GetIdsByCoursesAsync(Arg.Any<IReadOnlyCollection<long>>())
                    .Returns(new ValueTask<IReadOnlyCollection<long>>(assignmentsInCourses ?? []));
        _acceptances.GetForDeletionAsync(UserId, Arg.Any<IReadOnlyCollection<long>>())
                    .Returns(new ValueTask<IReadOnlyCollection<AcceptanceRepoRef>>(acceptances ?? []));
    }

    private static AcceptanceRepoRef Acceptance(long id, long studentId, long assignmentId = 1) =>
        new(id, studentId, assignmentId, "org", $"repo-{id}");

    [Fact]
    public async Task PreviewAsync_UnknownUser_IsNotFound()
    {
        ArrangeUser(null);

        (await _sut.PreviewAsync(UserId)).ShouldBe<NotFound>();
    }

    [Fact]
    public async Task PreviewAsync_ChangesNothing()
    {
        ArrangeUser(Existing());
        ArrangeClosure(ownedCourses: [10], assignmentsInCourses: [20],
                       acceptances: [Acceptance(1, UserId), Acceptance(2, 99)]);

        await _sut.PreviewAsync(UserId);

        await _forgejo.DidNotReceive().DeleteRepoAsync(Arg.Any<string>(), Arg.Any<string>());
        await _users.DidNotReceive().DeleteByIdAsync(Arg.Any<long>());
        await _courses.DidNotReceive().DeleteByIdsAsync(Arg.Any<IReadOnlyCollection<long>>());
        await _audit.DidNotReceive().RecordAsync(Arg.Any<AuditAction>(), Arg.Any<string>(), Arg.Any<string>(),
                                                 Arg.Any<IReadOnlyDictionary<string, string>>());
    }

    [Fact]
    public async Task PreviewAsync_ReportsCollateralDamageSeparatelyFromTheirOwn()
    {
        ArrangeUser(Existing());
        ArrangeClosure(
            ownedRosters: [5],
            ownedCourses: [10],
            coursesUsingRosters: [11],
            ownedAssignments: [20],
            assignmentsInCourses: [21, 22],
            acceptances:
            [
                Acceptance(1, UserId), Acceptance(2, 98), Acceptance(3, 99), Acceptance(4, 99)
            ]);

        var result = await _sut.PreviewAsync(UserId);

        UserDeletionImpact impact = result.ShouldBe<UserDeletionImpact>();
        impact.OwnedRosters.Should().Be(1);
        impact.OwnedCourses.Should().Be(2);
        impact.OwnedAssignments.Should().Be(3);
        impact.OwnAcceptances.Should().Be(1);
        impact.CollateralAcceptances.Should().Be(3);
        impact.AffectedStudents.Should().Be(2);
        impact.Repositories.Should().Be(4);
    }

    [Fact]
    public async Task PreviewAsync_PullsInTheCoursesThatUseARosterTheUserOwns()
    {
        ArrangeUser(Existing());
        // a roster cannot be deleted while a course still points at it, so that course is part of the cascade
        ArrangeClosure(ownedRosters: [5], coursesUsingRosters: [11]);

        var result = await _sut.PreviewAsync(UserId);

        result.ShouldBe<UserDeletionImpact>().OwnedCourses.Should().Be(1);
        await _assignments.Received().GetIdsByCoursesAsync(Arg.Is<IReadOnlyCollection<long>>(c => c.Contains(11L)));
    }

    [Fact]
    public async Task DeleteAsync_HandsTheUsersOwnSubmissionsToTheCascade()
    {
        ArrangeUser(Existing());
        ArrangeClosure(acceptances: [Acceptance(1, UserId), Acceptance(2, 99)]);

        await _sut.DeleteAsync(UserId);

        // only their own: the other student's submission goes with the assignment it belongs to
        await _cascade.Received(1).DeleteAcceptancesAsync(
            Arg.Is<IReadOnlyCollection<AcceptanceRepoRef>>(a => a.Count == 1 && a.Single().Id == 1));
    }

    [Fact]
    public async Task DeleteAsync_DoesNotDeleteAnOwnedAssignmentTwiceWhenItsCourseIsAlsoGoing()
    {
        ArrangeUser(Existing());
        // assignment 20 is owned by the user and lives in course 10, which is also being deleted
        ArrangeClosure(ownedCourses: [10], ownedAssignments: [20], assignmentsInCourses: [20]);

        await _sut.DeleteAsync(UserId);

        await _cascade.Received(1).DeleteAssignmentsAsync(Arg.Is<IReadOnlyCollection<long>>(a => a.Count == 0));
        await _cascade.Received(1).DeleteCoursesAsync(Arg.Is<IReadOnlyCollection<long>>(c => c.Contains(10L)));
    }

    [Fact]
    public async Task DeleteAsync_DeletesInDependencyOrder()
    {
        ArrangeUser(Existing());
        ArrangeClosure(ownedRosters: [5], ownedCourses: [10], ownedAssignments: [20],
                       acceptances: [Acceptance(1, UserId)]);

        await _sut.DeleteAsync(UserId);

        // leaves upward: submissions, then assignments, then the courses, then the rosters those pointed at
        Received.InOrder(() =>
        {
            _cascade.DeleteAcceptancesAsync(Arg.Any<IReadOnlyCollection<AcceptanceRepoRef>>());
            _cascade.DeleteAssignmentsAsync(Arg.Any<IReadOnlyCollection<long>>());
            _cascade.DeleteCoursesAsync(Arg.Any<IReadOnlyCollection<long>>());
            _rosters.DeleteByIdsAsync(Arg.Any<IReadOnlyCollection<long>>());
            _notifications.DeleteByRecipientAsync(UserId);
            _preferences.DeleteByUserAsync(UserId);
            _users.DeleteByIdAsync(UserId);
        });
    }

    [Fact]
    public async Task DeleteAsync_RemovesTheUserFromTheTeacherTeamsOfCoursesThatSurviveThem()
    {
        ArrangeUser(Existing());
        _courses.GetCoTaughtForgejoOrgsAsync(UserId)
                .Returns(new ValueTask<IReadOnlyCollection<string>>(new List<string> { "algorithms-10" }
                             .AsReadOnly()));
        _forgejo.GetTeamAsync("algorithms-10", ForgejoNaming.TeachersTeamName)
                .Returns(new ValueTask<OneOf<ForgejoTeam, NotFound, ForgejoError>>(
                    new ForgejoTeam(42, ForgejoNaming.TeachersTeamName)));
        _forgejo.RemoveTeamMemberAsync(42, StudentId)
                .Returns(new ValueTask<OneOf<Success, ForgejoError>>(new Success()));

        await _sut.DeleteAsync(UserId);

        await _forgejo.Received(1).RemoveTeamMemberAsync(42, StudentId);
    }

    [Fact]
    public async Task DeleteAsync_ForgetsTheUserInTheProvisioningCache()
    {
        ArrangeUser(Existing());

        await _sut.DeleteAsync(UserId);

        // otherwise the cache would keep vouching for an identity that no longer has a row
        _cache.Received(1).Forget(StudentId);
    }

    [Fact]
    public async Task DeleteAsync_DeactivatesTheForgejoAccountAndAuditsTheCascade()
    {
        ArrangeUser(Existing());
        ArrangeClosure(ownedCourses: [10], acceptances: [Acceptance(1, 99)]);

        await _sut.DeleteAsync(UserId);

        // Forgejo has no user-delete and its accounts own repository history, so the account is disabled
        await _forgejo.Received(1).SetUserActiveAsync(StudentId, false, 9);
        await _audit.Received(1).RecordAsync(AuditAction.UserHardDeleted, "User", UserId.ToString(),
                                             Arg.Is<IReadOnlyDictionary<string, string>>(
                                                 m => m["collateralAcceptances"] == "1"));
    }

    [Fact]
    public async Task DeleteAsync_UnknownUser_IsNotFound()
    {
        ArrangeUser(null);

        (await _sut.DeleteAsync(UserId)).ShouldBe<NotFound>();
        await _users.DidNotReceive().DeleteByIdAsync(Arg.Any<long>());
    }

    [Fact]
    public async Task PurgeExpiredAsync_AsksForUsersPastTheSchoolYearRetentionCutoff()
    {
        _users.GetPurgeCandidatesAsync(Arg.Any<Instant>())
              .Returns(new ValueTask<IReadOnlyCollection<User>>(new List<User>().AsReadOnly()));

        await _sut.PurgeExpiredAsync();

        // the window is the policy, not a caller-supplied number
        await _users.Received(1).GetPurgeCandidatesAsync(RetentionPolicy.CutoffFor(Now));
    }

    [Fact]
    public async Task PreviewAsync_ReportsWhenASoftDeletedUserBecomesPurgeable()
    {
        Instant lastSeen = new LocalDate(2026, 3, 15).ToInstantInZone();
        ArrangeUser(Existing(UserState.SoftDeleted, lastSeen));

        var result = await _sut.PreviewAsync(UserId);

        result.ShouldBe<UserDeletionImpact>().DeletableFrom.Should().Be(new LocalDate(2027, 8, 1));
    }

    [Fact]
    public async Task PreviewAsync_AnActiveUserHasNoPurgeDate()
    {
        ArrangeUser(Existing());

        var result = await _sut.PreviewAsync(UserId);

        // only accounts the LDAP sync has retired are ever purge candidates
        result.ShouldBe<UserDeletionImpact>().DeletableFrom.Should().BeNull();
    }

    [Fact]
    public async Task PurgeInactiveAsync_OneFailure_DoesNotStopTheRest()
    {
        var first = Existing(UserState.SoftDeleted);
        var second = new User
        {
            Id = 8, StudentId = "IF000008", FirstName = "Other", LastName = "Student", Role = Role.Student,
            State = UserState.SoftDeleted
        };
        _users.GetPurgeCandidatesAsync(Arg.Any<Instant>())
              .Returns(new ValueTask<IReadOnlyCollection<User>>(new List<User> { first, second }.AsReadOnly()));
        _rosters.GetIdsOwnedByAsync(UserId).Returns<ValueTask<IReadOnlyCollection<long>>>(
            _ => throw new InvalidOperationException("boom"));
        _rosters.GetIdsOwnedByAsync(8L).Returns(new ValueTask<IReadOnlyCollection<long>>([]));
        _courses.GetIdsOwnedByAsync(8L).Returns(new ValueTask<IReadOnlyCollection<long>>([]));
        _assignments.GetIdsOwnedByAsync(8L).Returns(new ValueTask<IReadOnlyCollection<long>>([]));
        _acceptances.GetForDeletionAsync(8L, Arg.Any<IReadOnlyCollection<long>>())
                    .Returns(new ValueTask<IReadOnlyCollection<AcceptanceRepoRef>>([]));

        PurgeOutcome outcome = await _sut.PurgeExpiredAsync();

        outcome.Purged.Should().Be(1);
        outcome.Failed.Should().Be(1);
        outcome.Deleted.Should().ContainSingle().Which.StudentId.Should().Be("IF000008");
        await _users.Received(1).DeleteByIdAsync(8L);
        await _users.DidNotReceive().DeleteByIdAsync(UserId);
    }

    [Fact]
    public async Task PurgeInactiveAsync_RecordsTheRunInTheAuditLog()
    {
        _users.GetPurgeCandidatesAsync(Arg.Any<Instant>())
              .Returns(new ValueTask<IReadOnlyCollection<User>>(new List<User>().AsReadOnly()));

        await _sut.PurgeExpiredAsync();

        await _audit.Received(1).RecordAsync(AuditAction.RetentionPurge, "User", null,
                                             Arg.Is<IReadOnlyDictionary<string, string>>(m => m["purged"] == "0"));
    }
}
