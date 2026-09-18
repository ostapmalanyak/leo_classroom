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

public sealed class CustomRosterServiceTests
{
    private const long OwnerId = 20L;
    private const string OwnerStudentId = "IF000020";
    private const long RosterId = 5L;

    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IRosterRepository _rosterRepo = Substitute.For<IRosterRepository>();
    private readonly IUserRepository _userRepo = Substitute.For<IUserRepository>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly CustomRosterService _sut;

    public CustomRosterServiceTests()
    {
        _uow.RosterRepository.Returns(_rosterRepo);
        _uow.UserRepository.Returns(_userRepo);
        _currentUser.StudentId.Returns(OwnerStudentId);
        _currentUser.Roles.Returns(new HashSet<Role> { Role.Teacher });
        _userRepo.GetIdByStudentIdAsync(OwnerStudentId).Returns(new ValueTask<long?>(OwnerId));

        _sut = new CustomRosterService(_uow, _currentUser, Substitute.For<ILogger<CustomRosterService>>());
    }

    private static Roster CustomRoster(long ownerId, RosterKind kind = RosterKind.Custom) =>
        new() { Id = RosterId, Name = "1A", Kind = kind, OwnerId = ownerId };

    private void ArrangeRoster(Roster roster) =>
        _rosterRepo.GetTrackedWithMembersAsync(RosterId).Returns(new ValueTask<Roster?>(roster));

    [Fact]
    public async Task Create_SetsActingTeacherAsOwnerAndCustomKind()
    {
        var result = await _sut.CreateAsync("Project group");

        Roster created = result.ShouldBe<Success<Roster>>().Value;
        created.Kind.Should().Be(RosterKind.Custom);
        created.OwnerId.Should().Be(OwnerId);
        _rosterRepo.Received(1).Add(Arg.Is<Roster>(r => r.OwnerId == OwnerId));
    }

    [Fact]
    public async Task ListMembers_UnknownRoster_ReturnsNotFound()
    {
        _rosterRepo.GetMembersAsync(RosterId).Returns(new ValueTask<IReadOnlyCollection<UserSummary>?>((IReadOnlyCollection<UserSummary>?)null));

        var result = await _sut.ListMembersAsync(RosterId);

        result.ShouldBe<NotFound>();
    }

    [Fact]
    public async Task ListMembers_ExistingRoster_ReturnsMembers()
    {
        IReadOnlyCollection<UserSummary> members =
            new List<UserSummary> { new(30L, "IF000030", "S", "T", "1A", Role.Student) }.AsReadOnly();
        _rosterRepo.GetMembersAsync(RosterId).Returns(new ValueTask<IReadOnlyCollection<UserSummary>?>(members));

        var result = await _sut.ListMembersAsync(RosterId);

        result.ShouldBe<IReadOnlyCollection<UserSummary>>().Should().ContainSingle(m => m.Id == 30L);
    }

    [Fact]
    public async Task Rename_ByOwner_Succeeds()
    {
        ArrangeRoster(CustomRoster(OwnerId));

        var result = await _sut.RenameAsync(RosterId, "Renamed");

        result.ShouldBe<Success<Roster>>().Value.Name.Should().Be("Renamed");
    }

    [Fact]
    public async Task Rename_ByNonOwner_ReturnsForbidden()
    {
        ArrangeRoster(CustomRoster(ownerId: 99L));

        var result = await _sut.RenameAsync(RosterId, "Renamed");

        result.ShouldBe<Forbidden>();
    }

    [Fact]
    public async Task Rename_ByAdminNonOwner_Succeeds()
    {
        _currentUser.Roles.Returns(new HashSet<Role> { Role.Admin });
        ArrangeRoster(CustomRoster(ownerId: 99L));

        var result = await _sut.RenameAsync(RosterId, "Renamed");

        result.ShouldBe<Success<Roster>>();
    }

    [Fact]
    public async Task Delete_WhenInUse_ReturnsInUseWithCount()
    {
        ArrangeRoster(CustomRoster(OwnerId));
        _rosterRepo.CountCoursesUsingAsync(RosterId).Returns(new ValueTask<int>(3));

        var result = await _sut.DeleteAsync(RosterId);

        result.ShouldBe<ICustomRosterService.InUse>().CourseCount.Should().Be(3);
        await _rosterRepo.DidNotReceive().DeleteByIdAsync(Arg.Any<long>());
    }

    [Fact]
    public async Task Delete_WhenNotInUse_Deletes()
    {
        ArrangeRoster(CustomRoster(OwnerId));
        _rosterRepo.CountCoursesUsingAsync(RosterId).Returns(new ValueTask<int>(0));
        _rosterRepo.DeleteByIdAsync(RosterId).Returns(new ValueTask<bool>(true));

        var result = await _sut.DeleteAsync(RosterId);

        result.ShouldBe<Success>();
        await _rosterRepo.Received(1).DeleteByIdAsync(RosterId);
    }

    [Fact]
    public async Task AddMember_ToAutoRoster_ReturnsForbidden()
    {
        ArrangeRoster(CustomRoster(OwnerId, RosterKind.Auto));

        var result = await _sut.AddMemberAsync(RosterId, 30L);

        result.ShouldBe<Forbidden>();
        await _uow.DidNotReceive().SaveChangesAsync();
    }

    [Fact]
    public async Task AddMember_UnknownUser_ReturnsNotFound()
    {
        ArrangeRoster(CustomRoster(OwnerId));
        _userRepo.GetTrackedByIdAsync(30L).Returns(new ValueTask<User?>((User?)null));

        var result = await _sut.AddMemberAsync(RosterId, 30L);

        result.ShouldBe<NotFound>();
    }

    [Fact]
    public async Task AddMember_Valid_AddsMember()
    {
        Roster roster = CustomRoster(OwnerId);
        ArrangeRoster(roster);
        var student = new User { Id = 30L, StudentId = "IF000030", FirstName = "S", LastName = "T", Role = Role.Student };
        _userRepo.GetTrackedByIdAsync(30L).Returns(new ValueTask<User?>(student));

        var result = await _sut.AddMemberAsync(RosterId, 30L);

        result.ShouldBe<Success>();
        roster.Members.Should().ContainSingle(m => m.Id == 30L);
    }

    [Fact]
    public async Task AddMember_SoftDeletedUser_ReturnsNotFound()
    {
        ArrangeRoster(CustomRoster(OwnerId));
        var gone = new User
        {
            Id = 30L, StudentId = "IF000030", FirstName = "S", LastName = "T", Role = Role.Student,
            State = UserState.SoftDeleted
        };
        _userRepo.GetTrackedByIdAsync(30L).Returns(new ValueTask<User?>(gone));

        var result = await _sut.AddMemberAsync(RosterId, 30L);

        result.ShouldBe<NotFound>();
    }

    [Fact]
    public async Task RemoveMember_KeepsExistingAcceptances_OnlyEditsMembership()
    {
        Roster roster = CustomRoster(OwnerId);
        var student = new User { Id = 30L, StudentId = "IF000030", FirstName = "S", LastName = "T", Role = Role.Student };
        roster.Members.Add(student);
        ArrangeRoster(roster);

        var result = await _sut.RemoveMemberAsync(RosterId, 30L);

        result.ShouldBe<Success>();
        roster.Members.Should().BeEmpty();
    }
}
