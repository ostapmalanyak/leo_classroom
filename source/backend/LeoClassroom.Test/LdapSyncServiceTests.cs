using LeoClassroom.Services.Audit;
using LeoClassroom.Services.Auth;
using LeoClassroom.Services.Forgejo;
using LeoClassroom.Services.Ldap;
using LeoClassroom.Services.Util;
using LeoClassroom.Persistence.Model;
using LeoClassroom.Persistence.Repositories;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using OneOf;
using OneOf.Types;

namespace LeoClassroom.Test;

public sealed class LdapSyncServiceTests
{
    private readonly ILdapDirectory _directory = Substitute.For<ILdapDirectory>();
    private readonly IUserProvisioningCache _provisioningCache = Substitute.For<IUserProvisioningCache>();
    private readonly IForgejoClient _forgejo = Substitute.For<IForgejoClient>();
    private readonly IUserRepository _userRepo = Substitute.For<IUserRepository>();
    private readonly IRosterRepository _rosterRepo = Substitute.For<IRosterRepository>();
    private readonly List<User> _users = [];
    private readonly List<Roster> _autoRosters = [];
    private readonly LdapSyncService _sut;

    public LdapSyncServiceTests()
    {
        var uow = Substitute.For<IUnitOfWork>();
        uow.UserRepository.Returns(_userRepo);
        uow.RosterRepository.Returns(_rosterRepo);
        _userRepo.GetAllTrackedAsync().Returns(_ => new ValueTask<IReadOnlyCollection<User>>(_users.AsReadOnly()));
        _userRepo.When(r => r.Add(Arg.Any<User>())).Do(ci => _users.Add(ci.Arg<User>()));
        _rosterRepo.GetAllTrackedAutoRostersAsync()
                   .Returns(_ => new ValueTask<IReadOnlyCollection<Roster>>(_autoRosters.AsReadOnly()));
        _rosterRepo.When(r => r.Add(Arg.Any<Roster>())).Do(ci => _autoRosters.Add(ci.Arg<Roster>()));

        _forgejo.EnsureUserAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<long>())
                .Returns(new ValueTask<OneOf<Success, ForgejoError>>(new Success()));
        _forgejo.SetUserActiveAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<long>())
                .Returns(new ValueTask<OneOf<Success, ForgejoError>>(new Success()));

        var clock = Substitute.For<IClock>();
        clock.GetCurrentInstant().Returns(Instant.FromUtc(2026, 3, 1, 2, 0, 0));

        _sut = new LdapSyncService(_directory, uow, _forgejo, clock,
            Options.Create(new LdapSettings
            {
                Host = "h", BindDn = "b", BindPassword = "p", StudentBaseDn = "s", TeacherBaseDn = "t",
                SoftDeleteThreshold = 2
            }),
            Options.Create(new ForgejoSettings
            {
                BaseUrl = "http://f", AdminToken = "x", WebhookSecret = "w", WebhookTargetUrl = "http://f/hook",
                AuthSourceId = 9
            }),
            _provisioningCache,
            Substitute.For<IAuditLog>(),
            Substitute.For<ILogger<LdapSyncService>>());
    }

    private void ArrangeRead(params LdapPerson[] people)
    {
        OneOf<IReadOnlyCollection<LdapPerson>, LdapError> result = people.ToList().AsReadOnly();
        _directory.SearchPeopleAsync()
                  .Returns(new ValueTask<OneOf<IReadOnlyCollection<LdapPerson>, LdapError>>(result));
    }

    private void ArrangeFailedRead() =>
        _directory.SearchPeopleAsync()
                  .Returns(new ValueTask<OneOf<IReadOnlyCollection<LdapPerson>, LdapError>>(new LdapError("down")));

    private static LdapPerson Student(string number, string @class) =>
        new(number, "First", "Last", $"{number}@s.local", @class, Role.Student);

    private static User ActiveUser(string number, Role role = Role.Student) =>
        new() { StudentId = number, FirstName = "Old", LastName = "Name", Role = role, State = UserState.Active };

    [Fact]
    public async Task NewPerson_IsCreatedActiveAndForgejoEnsured()
    {
        ArrangeRead(Student("IF123456", "1A"));

        SyncOutcome outcome = await _sut.RunAsync();

        outcome.Created.Should().Be(1);
        _users.Should().ContainSingle(u => u.StudentId == "IF123456" && u.State == UserState.Active);
        await _forgejo.Received(1).EnsureUserAsync("IF123456", "IF123456@s.local", 9);
    }

    [Fact]
    public async Task ExistingPerson_IsRefreshed()
    {
        _users.Add(ActiveUser("IT654321"));
        ArrangeRead(new LdapPerson("IT654321", "New", "Person", "new@s.local", "2B", Role.Student));

        SyncOutcome outcome = await _sut.RunAsync();

        outcome.Updated.Should().Be(1);
        _users.Single().FirstName.Should().Be("New");
        _users.Single().Class.Should().Be("2B");
    }

    [Fact]
    public async Task AutoRoster_MembershipIsReconciled()
    {
        User st1 = ActiveUser("EL111111");
        User st2 = ActiveUser("ME222222");
        _users.AddRange([st1, st2]);
        _autoRosters.Add(new Roster { Name = "1A", Kind = RosterKind.Auto, ClassKey = "1A", Members = [st2] });
        ArrangeRead(Student("EL111111", "1A"), Student("ME222222", "2B"));

        await _sut.RunAsync();

        Roster classA = _autoRosters.Single(r => r.ClassKey == "1A");
        classA.Members.Should().ContainSingle(m => m.StudentId == "EL111111");
        Roster classB = _autoRosters.Single(r => r.ClassKey == "2B");
        classB.Members.Should().ContainSingle(m => m.StudentId == "ME222222");
    }

    [Fact]
    public async Task HealthyDeparture_SoftDeletesAndDisablesForgejo()
    {
        _users.AddRange([ActiveUser("FE100000"), ActiveUser("FE200000")]);
        ArrangeRead(Student("FE100000", "1A"));

        SyncOutcome outcome = await _sut.RunAsync();

        outcome.SoftDeleted.Should().Be(1);
        _users.Single(u => u.StudentId == "FE200000").State.Should().Be(UserState.SoftDeleted);
        await _forgejo.Received(1).SetUserActiveAsync("FE200000", false, 9);
    }

    [Fact]
    public async Task FailedRead_SkipsEverything()
    {
        _users.Add(ActiveUser("IF999999"));
        ArrangeFailedRead();

        SyncOutcome outcome = await _sut.RunAsync();

        outcome.DestructivePassSkipped.Should().BeTrue();
        _users.Single().State.Should().Be(UserState.Active);
        await _forgejo.DidNotReceive().SetUserActiveAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<long>());
    }

    [Fact]
    public async Task OverThresholdDisappearance_SkipsSoftDeleteButStillUpserts()
    {
        _users.AddRange([ActiveUser("IF000001"), ActiveUser("IF000002"), ActiveUser("IF000003"), ActiveUser("IF000004")]);
        ArrangeRead(Student("IF000001", "1A"));

        SyncOutcome outcome = await _sut.RunAsync();

        outcome.DestructivePassSkipped.Should().BeTrue();
        outcome.SoftDeleted.Should().Be(0);
        _users.Where(u => u.State == UserState.SoftDeleted).Should().BeEmpty();
        _users.Single(u => u.StudentId == "IF000001").FirstName.Should().Be("First");
    }

    [Fact]
    public async Task ReturningUser_IsReactivatedAndForgejoReEnabled()
    {
        var softDeleted = new User
        {
            StudentId = "ME333333", FirstName = "Was", LastName = "Gone", Role = Role.Student,
            State = UserState.SoftDeleted
        };
        _users.Add(softDeleted);
        ArrangeRead(Student("ME333333", "3C"));

        SyncOutcome outcome = await _sut.RunAsync();

        outcome.Reactivated.Should().Be(1);
        softDeleted.State.Should().Be(UserState.Active);
        await _forgejo.Received(1).SetUserActiveAsync("ME333333", true, 9);
    }

    [Fact]
    public async Task SecondRun_IsIdempotent()
    {
        _users.Add(ActiveUser("IT123123"));
        ArrangeRead(Student("IT123123", "1A"));

        await _sut.RunAsync();
        SyncOutcome second = await _sut.RunAsync();

        second.Created.Should().Be(0);
        second.SoftDeleted.Should().Be(0);
        _users.Single(u => u.StudentId == "IT123123").State.Should().Be(UserState.Active);
    }

    [Fact]
    public async Task RunAsync_SuccessfulSync_RefreshesTheProvisioningCacheFromWhatItWrote()
    {
        // the sync changes User.State in bulk, so the cache has to be told what it now says
        ArrangeRead(Student("IF000001", "5AHIF"));

        await _sut.RunAsync();

        _provisioningCache.Received(1).Refresh(Arg.Is<IEnumerable<ProvisionedUser>>(
            users => users.Any(u => u.StudentId == "IF000001" && u.State == UserState.Active)));
    }

    [Fact]
    public async Task RunAsync_SoftDeletedPerson_IsRefreshedIntoTheCacheAsInactive()
    {
        _userRepo.GetAllTrackedAsync()
                 .Returns(new ValueTask<IReadOnlyCollection<User>>(
                     new List<User> { ActiveUser("IF000001"), ActiveUser("IF000002") }.AsReadOnly()));
        ArrangeRead(Student("IF000001", "5AHIF"));

        await _sut.RunAsync();

        _provisioningCache.Received(1).Refresh(Arg.Is<IEnumerable<ProvisionedUser>>(
            users => users.Any(u => u.StudentId == "IF000002" && u.State == UserState.SoftDeleted)));
    }

    [Fact]
    public async Task RunAsync_FailedRead_LeavesTheProvisioningCacheAlone()
    {
        ArrangeFailedRead();

        await _sut.RunAsync();

        _provisioningCache.DidNotReceive().Refresh(Arg.Any<IEnumerable<ProvisionedUser>>());
    }
}
