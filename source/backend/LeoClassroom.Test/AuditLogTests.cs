using LeoClassroom.Services.Audit;
using LeoClassroom.Services.Auth;
using LeoClassroom.Services.Util;
using LeoClassroom.Persistence.Model;
using LeoClassroom.Persistence.Repositories;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace LeoClassroom.Test;

public sealed class AuditLogTests
{
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IAuditEventRepository _repo = Substitute.For<IAuditEventRepository>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly AuditLog _sut;

    public AuditLogTests()
    {
        _uow.AuditEventRepository.Returns(_repo);
        _clock.GetCurrentInstant().Returns(Instant.FromUtc(2026, 6, 13, 10, 0));
        _sut = new AuditLog(_uow, _currentUser, _clock, Substitute.For<ILogger<AuditLog>>());
    }

    [Fact]
    public async Task Record_AuthenticatedUser_StoresActorRolesAndMetadata()
    {
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.StudentId.Returns("IF000020");
        _currentUser.Roles.Returns(new HashSet<Role> { Role.Teacher });
        AuditEvent? captured = null;
        _repo.When(r => r.Add(Arg.Any<AuditEvent>())).Do(ci => captured = ci.Arg<AuditEvent>());

        await _sut.RecordAsync(AuditAction.AssignmentDeleted, "Assignment", "11",
                               new Dictionary<string, string> { ["acceptances"] = "2" });

        captured.Should().NotBeNull();
        captured!.ActorStudentId.Should().Be("IF000020");
        captured.ActorRoles.Should().Contain("Teacher");
        captured.Action.Should().Be(AuditAction.AssignmentDeleted);
        captured.TargetId.Should().Be("11");
        captured.Metadata.Should().Contain("acceptances");
        await _uow.Received(1).SaveChangesAsync();
    }

    [Fact]
    public async Task Record_Unauthenticated_UsesSystemActor()
    {
        _currentUser.IsAuthenticated.Returns(false);
        AuditEvent? captured = null;
        _repo.When(r => r.Add(Arg.Any<AuditEvent>())).Do(ci => captured = ci.Arg<AuditEvent>());

        await _sut.RecordAsync(AuditAction.AssignmentAutoDeleted, "Assignment", "5");

        captured!.ActorStudentId.Should().Be(AuditActor.System);
        captured.ActorRoles.Should().Be(AuditActor.System);
    }
}

public sealed class AuditRetentionServiceTests
{
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IAuditEventRepository _repo = Substitute.For<IAuditEventRepository>();
    private readonly IAuditLog _audit = Substitute.For<IAuditLog>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly AuditRetentionService _sut;

    public AuditRetentionServiceTests()
    {
        _uow.AuditEventRepository.Returns(_repo);
        _clock.GetCurrentInstant().Returns(Instant.FromUtc(2026, 6, 13, 3, 0));
        var options = Options.Create(new AuditSettings { RetentionYears = 5 });
        _sut = new AuditRetentionService(_uow, _audit, _clock, options,
                                         Substitute.For<ILogger<AuditRetentionService>>());
    }

    [Fact]
    public async Task Purge_RemovesAgedEventsAndAuditsThePurge()
    {
        _repo.PurgeOlderThanAsync(Arg.Any<Instant>()).Returns(new ValueTask<int>(7));

        int purged = await _sut.PurgeAsync();

        purged.Should().Be(7);
        await _audit.Received(1).RecordAsync(AuditAction.RetentionPurge, "AuditEvent", null,
                                             Arg.Any<IReadOnlyDictionary<string, string>?>());
    }

    [Fact]
    public async Task Purge_NothingAged_DoesNotAudit()
    {
        _repo.PurgeOlderThanAsync(Arg.Any<Instant>()).Returns(new ValueTask<int>(0));

        int purged = await _sut.PurgeAsync();

        purged.Should().Be(0);
        await _audit.DidNotReceive().RecordAsync(Arg.Any<AuditAction>(), Arg.Any<string>(), Arg.Any<string?>(),
                                                 Arg.Any<IReadOnlyDictionary<string, string>?>());
    }
}
