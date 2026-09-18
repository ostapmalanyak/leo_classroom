using LeoClassroom.Services.Audit;
using LeoClassroom.Services.Auth;
using LeoClassroom.Services;
using LeoClassroom.Persistence.Model;
using LeoClassroom.Persistence.Repositories;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Shared;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OneOf.Types;

namespace LeoClassroom.Test;

public sealed class UserProvisioningServiceTests
{
    private const string StudentId = "IF123456";

    private readonly IUnitOfWork _uow;
    private readonly IUserRepository _userRepo;
    private readonly IRosterRepository _rosterRepo;
    private readonly IUserProvisioningCache _cache;
    private readonly IAuditLog _audit;
    private readonly UserProvisioningService _sut;

    public UserProvisioningServiceTests()
    {
        _uow = Substitute.For<IUnitOfWork>();
        _userRepo = Substitute.For<IUserRepository>();
        _uow.UserRepository.Returns(_userRepo);
        _rosterRepo = Substitute.For<IRosterRepository>();
        _uow.RosterRepository.Returns(_rosterRepo);
        _cache = Substitute.For<IUserProvisioningCache>();
        _audit = Substitute.For<IAuditLog>();
        _sut = new UserProvisioningService(_uow, _cache, _audit,
                                           Substitute.For<ILogger<UserProvisioningService>>());
    }

    private static ClaimUserData Claims(Role? role = Role.Student) =>
        new(StudentId, "Sam", "Student", "sam@school.at", role, "5AHIF");

    [Fact]
    public async Task EnsureUserAsync_UnknownUser_CreatesActiveAndSaves()
    {
        _userRepo.GetTrackedByStudentIdAsync(StudentId).Returns(new ValueTask<User?>(default(User?)));

        var result = await _sut.EnsureUserAsync(Claims());

        result.ShouldBe<Success>();
        _userRepo.Received(1).Add(Arg.Is<User>(u => u.StudentId == StudentId && u.State == UserState.Active));
        await _uow.Received(1).SaveChangesAsync();
    }

    [Fact]
    public async Task EnsureUserAsync_UnknownUser_StoresTheClassFromTheToken()
    {
        // without the LDAP sync the token is the only source of a class, and dropping it left every user
        // class-less: nothing on their account, and nothing to group them by
        _userRepo.GetTrackedByStudentIdAsync(StudentId).Returns(new ValueTask<User?>(default(User?)));

        await _sut.EnsureUserAsync(Claims());

        _userRepo.Received(1).Add(Arg.Is<User>(u => u.Class == "5AHIF"));
    }

    [Fact]
    public async Task EnsureUserAsync_NoRosterForTheClassYet_CreatesOneAndJoinsIt()
    {
        _userRepo.GetTrackedByStudentIdAsync(StudentId).Returns(new ValueTask<User?>(default(User?)));
        _rosterRepo.GetTrackedAutoRosterByClassAsync("5AHIF").Returns(new ValueTask<Roster?>(default(Roster?)));

        await _sut.EnsureUserAsync(Claims());

        _rosterRepo.Received(1).Add(Arg.Is<Roster>(r =>
            r.Name == "5AHIF" && r.ClassKey == "5AHIF" && r.Kind == RosterKind.Auto && r.Members.Count == 1));
    }

    [Fact]
    public async Task EnsureUserAsync_RosterForTheClassExists_JoinsItWithoutCreatingAnother()
    {
        var existing = new User
        {
            StudentId = StudentId, FirstName = "Sam", LastName = "Student", Role = Role.Student,
            State = UserState.Active
        };
        var roster = new Roster { Name = "5AHIF", Kind = RosterKind.Auto, ClassKey = "5AHIF" };
        _userRepo.GetTrackedByStudentIdAsync(StudentId).Returns(new ValueTask<User?>(existing));
        _rosterRepo.GetTrackedAutoRosterByClassAsync("5AHIF").Returns(new ValueTask<Roster?>(roster));

        await _sut.EnsureUserAsync(Claims());

        roster.Members.Should().ContainSingle().Which.StudentId.Should().Be(StudentId);
        _rosterRepo.DidNotReceive().Add(Arg.Any<Roster>());
    }

    [Fact]
    public async Task EnsureUserAsync_AlreadyInTheClassRoster_DoesNotAddThemTwice()
    {
        var existing = new User
        {
            Id = 7, StudentId = StudentId, FirstName = "Sam", LastName = "Student", Role = Role.Student,
            State = UserState.Active
        };
        var roster = new Roster { Name = "5AHIF", Kind = RosterKind.Auto, ClassKey = "5AHIF" };
        roster.Members.Add(existing);
        _userRepo.GetTrackedByStudentIdAsync(StudentId).Returns(new ValueTask<User?>(existing));
        _rosterRepo.GetTrackedAutoRosterByClassAsync("5AHIF").Returns(new ValueTask<Roster?>(roster));

        await _sut.EnsureUserAsync(Claims());

        roster.Members.Should().ContainSingle();
    }

    [Fact]
    public async Task EnsureUserAsync_TokenWithoutAClass_TouchesNoRoster()
    {
        _userRepo.GetTrackedByStudentIdAsync(StudentId).Returns(new ValueTask<User?>(default(User?)));

        await _sut.EnsureUserAsync(new ClaimUserData(StudentId, "Sam", "Student", "sam@school.at",
                                                     Role.Student, Class: null));

        _rosterRepo.DidNotReceive().Add(Arg.Any<Roster>());
        await _rosterRepo.DidNotReceive().GetTrackedAutoRosterByClassAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task EnsureUserAsync_ExistingActive_RefreshesClaims()
    {
        var existing = new User
        {
            StudentId = StudentId, FirstName = "Old", LastName = "Name", Role = Role.Student, State = UserState.Active
        };
        _userRepo.GetTrackedByStudentIdAsync(StudentId).Returns(new ValueTask<User?>(existing));

        var result = await _sut.EnsureUserAsync(Claims());

        result.ShouldBe<Success>();
        existing.FirstName.Should().Be("Sam");
        existing.Email.Should().Be("sam@school.at");
        _userRepo.DidNotReceive().Add(Arg.Any<User>());
        await _uow.Received(1).SaveChangesAsync();
    }

    [Fact]
    public async Task EnsureUserAsync_SoftDeletedUser_ReturnsForbiddenAndDoesNotResurrect()
    {
        var existing = new User
        {
            StudentId = StudentId, FirstName = "Old", LastName = "Name", Role = Role.Student,
            State = UserState.SoftDeleted
        };
        _userRepo.GetTrackedByStudentIdAsync(StudentId).Returns(new ValueTask<User?>(existing));

        var result = await _sut.EnsureUserAsync(Claims());

        result.ShouldBe<Forbidden>();
        existing.State.Should().Be(UserState.SoftDeleted);
        await _uow.DidNotReceive().SaveChangesAsync();
    }

    [Fact]
    public async Task EnsureUserAsync_TokenWithoutRole_KeepsTheStoredRole()
    {
        // a token whose roles claim is missing must never be able to demote a teacher
        var existing = new User
        {
            StudentId = StudentId, FirstName = "Old", LastName = "Name", Role = Role.Teacher, State = UserState.Active
        };
        _userRepo.GetTrackedByStudentIdAsync(StudentId).Returns(new ValueTask<User?>(existing));

        var result = await _sut.EnsureUserAsync(Claims(role: null));

        result.ShouldBe<Success>();
        existing.Role.Should().Be(Role.Teacher);
        existing.FirstName.Should().Be("Sam");
    }

    [Fact]
    public async Task EnsureUserAsync_TokenWithRole_AppliesIt()
    {
        var existing = new User
        {
            StudentId = StudentId, FirstName = "Old", LastName = "Name", Role = Role.Student, State = UserState.Active
        };
        _userRepo.GetTrackedByStudentIdAsync(StudentId).Returns(new ValueTask<User?>(existing));

        var result = await _sut.EnsureUserAsync(Claims(role: Role.Admin));

        result.ShouldBe<Success>();
        existing.Role.Should().Be(Role.Admin);
    }

    [Fact]
    public async Task EnsureUserAsync_UnknownUserWithoutRole_GetsTheLeastPrivilegedRole()
    {
        _userRepo.GetTrackedByStudentIdAsync(StudentId).Returns(new ValueTask<User?>(default(User?)));

        var result = await _sut.EnsureUserAsync(Claims(role: null));

        result.ShouldBe<Success>();
        _userRepo.Received(1).Add(Arg.Is<User>(u => u.Role == Role.Student));
    }

    [Fact]
    public async Task EnsureUserAsync_KnownProvisionedCaller_DoesNotTouchTheDatabase()
    {
        _cache.Lookup(Arg.Any<ClaimUserData>()).Returns(ProvisioningState.Active);

        var result = await _sut.EnsureUserAsync(Claims());

        result.ShouldBe<Success>();
        await _userRepo.DidNotReceive().GetTrackedByStudentIdAsync(Arg.Any<string>());
        await _uow.DidNotReceive().SaveChangesAsync();
    }

    [Fact]
    public async Task EnsureUserAsync_ExistingActive_RemembersTheCaller()
    {
        var existing = new User
        {
            StudentId = StudentId, FirstName = "Old", LastName = "Name", Role = Role.Student, State = UserState.Active
        };
        _userRepo.GetTrackedByStudentIdAsync(StudentId).Returns(new ValueTask<User?>(existing));

        await _sut.EnsureUserAsync(Claims());

        _cache.Received(1).Remember(Arg.Any<ClaimUserData>(), ProvisioningState.Active);
    }

    [Fact]
    public async Task EnsureUserAsync_SoftDeletedUser_IsRefusedCachedAndAudited()
    {
        var existing = new User
        {
            StudentId = StudentId, FirstName = "Old", LastName = "Name", Role = Role.Student,
            State = UserState.SoftDeleted
        };
        _userRepo.GetTrackedByStudentIdAsync(StudentId).Returns(new ValueTask<User?>(existing));
        _cache.ShouldAuditRefusal(StudentId).Returns(true);

        var result = await _sut.EnsureUserAsync(Claims());

        result.ShouldBe<Forbidden>();
        await _audit.Received(1).RecordAsync(AuditAction.RejectedSoftDeletedLogin, "User", existing.Id.ToString(),
                                             Arg.Any<IReadOnlyDictionary<string, string>>());

        // remembered as disabled, so the next attempt is refused without reaching the database
        _cache.Received(1).Remember(Arg.Any<ClaimUserData>(), ProvisioningState.Inactive);
    }

    [Fact]
    public async Task EnsureUserAsync_CachedDisabledUser_IsRefusedWithoutTouchingTheDatabase()
    {
        _cache.Lookup(Arg.Any<ClaimUserData>()).Returns(ProvisioningState.Inactive);
        _cache.ShouldAuditRefusal(StudentId).Returns(true);

        var result = await _sut.EnsureUserAsync(Claims());

        result.ShouldBe<Forbidden>();
        await _userRepo.DidNotReceive().GetTrackedByStudentIdAsync(Arg.Any<string>());
        await _audit.Received(1).RecordAsync(AuditAction.RejectedSoftDeletedLogin, "User", null,
                                             Arg.Any<IReadOnlyDictionary<string, string>>());
    }

    [Fact]
    public async Task EnsureUserAsync_ThrottledRefusal_IsStillRefusedButNotAudited()
    {
        _cache.Lookup(Arg.Any<ClaimUserData>()).Returns(ProvisioningState.Inactive);
        _cache.ShouldAuditRefusal(StudentId).Returns(false);

        var result = await _sut.EnsureUserAsync(Claims());

        // repeated attempts by one disabled account must not decide how many audit rows the backend writes
        result.ShouldBe<Forbidden>();
        await _audit.DidNotReceive().RecordAsync(Arg.Any<AuditAction>(), Arg.Any<string>(), Arg.Any<string>(),
                                                 Arg.Any<IReadOnlyDictionary<string, string>>());
    }
}
