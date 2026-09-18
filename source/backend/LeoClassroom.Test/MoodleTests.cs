using LeoClassroom.Services.Moodle;
using LeoClassroom.Services.Security;
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

public sealed class SecretProtectorTests
{
    private const string Key = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";

    private static SecretProtector Sut(string key = Key) =>
        new(Options.Create(new DataProtectionSettings { MasterKey = key }));

    [Fact]
    public void ProtectUnprotect_RoundTrips()
    {
        SecretProtector sut = Sut();
        string cipher = sut.Protect("super-secret-token");

        cipher.Should().NotBe("super-secret-token");
        sut.TryUnprotect(cipher, out string plaintext).Should().BeTrue();
        plaintext.Should().Be("super-secret-token");
    }

    [Fact]
    public void TryUnprotect_TamperedCiphertext_Fails()
    {
        SecretProtector sut = Sut();
        string cipher = sut.Protect("token");
        char[] chars = cipher.ToCharArray();
        chars[^2] = chars[^2] == 'A' ? 'B' : 'A';

        sut.TryUnprotect(new string(chars), out _).Should().BeFalse();
    }

    [Fact]
    public void TryUnprotect_Garbage_Fails()
    {
        Sut().TryUnprotect("not-base64!!", out _).Should().BeFalse();
    }

    [Fact]
    public void Ciphertext_DiffersAcrossCalls_ForSamePlaintext()
    {
        SecretProtector sut = Sut();

        sut.Protect("x").Should().NotBe(sut.Protect("x"));
    }
}

public sealed class MoodleSyncServiceTests
{
    private const long CourseId = 7L;
    private const long AssignmentId = 11L;

    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IMoodleLinkRepository _linkRepo = Substitute.For<IMoodleLinkRepository>();
    private readonly IMoodleSyncRepository _syncRepo = Substitute.For<IMoodleSyncRepository>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly MoodleSyncService _sut;

    public MoodleSyncServiceTests()
    {
        _uow.MoodleLinkRepository.Returns(_linkRepo);
        _uow.MoodleSyncRepository.Returns(_syncRepo);
        _clock.GetCurrentInstant().Returns(Instant.FromUtc(2026, 6, 14, 9, 0));
        _sut = new MoodleSyncService(_uow, _clock, Substitute.For<ILogger<MoodleSyncService>>());
    }

    private void LinkReturns(MoodleLink? link) =>
        _linkRepo.GetByCourseIdAsync(CourseId).Returns(new ValueTask<MoodleLink?>(link));

    private static Assignment Assignment() => new()
    {
        Id = AssignmentId, CourseId = CourseId, Title = "Algorithms", Slug = "algorithms",
        Deadline = Instant.FromUtc(2026, 6, 20, 21, 59)
    };

    [Fact]
    public async Task EnqueueCreate_WithEnabledLink_AddsOpAndSaves()
    {
        LinkReturns(new MoodleLink { CourseId = CourseId, MoodleBaseUrl = "https://m", Enabled = true, TokenCipher = "c" });

        await _sut.EnqueueCreateAsync(Assignment());

        _syncRepo.Received(1).Add(Arg.Is<MoodleSyncOp>(o => o.OpType == MoodleSyncOpType.Create
                                                            && o.AssignmentId == AssignmentId
                                                            && o.Status == MoodleSyncStatus.Pending));
        await _uow.Received(1).SaveChangesAsync();
    }

    [Fact]
    public async Task EnqueueCreate_NoLink_DoesNothing()
    {
        LinkReturns(null);

        await _sut.EnqueueCreateAsync(Assignment());

        _syncRepo.DidNotReceive().Add(Arg.Any<MoodleSyncOp>());
        await _uow.DidNotReceive().SaveChangesAsync();
    }

    [Fact]
    public async Task EnqueueUpdate_DisabledLink_DoesNothing()
    {
        LinkReturns(new MoodleLink { CourseId = CourseId, MoodleBaseUrl = "https://m", Enabled = false, TokenCipher = "c" });

        await _sut.EnqueueUpdateAsync(Assignment());

        _syncRepo.DidNotReceive().Add(Arg.Any<MoodleSyncOp>());
    }
}

public sealed class MoodleSyncDispatcherTests
{
    private const string Key = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";

    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IMoodleLinkRepository _linkRepo = Substitute.For<IMoodleLinkRepository>();
    private readonly IMoodleSyncRepository _syncRepo = Substitute.For<IMoodleSyncRepository>();
    private readonly IMoodleClient _client = Substitute.For<IMoodleClient>();
    private readonly ISecretProtector _protector = new SecretProtector(
        Options.Create(new DataProtectionSettings { MasterKey = Key }));
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly MoodleSettings _settings = new() { MaxAttempts = 3, BaseBackoffSeconds = 60, AppBaseUrl = "https://app" };

    public MoodleSyncDispatcherTests()
    {
        _uow.MoodleLinkRepository.Returns(_linkRepo);
        _uow.MoodleSyncRepository.Returns(_syncRepo);
        _clock.GetCurrentInstant().Returns(Instant.FromUtc(2026, 6, 14, 9, 0));
        _linkRepo.GetByCourseIdAsync(7L).Returns(new ValueTask<MoodleLink?>(new MoodleLink
        {
            CourseId = 7L, MoodleBaseUrl = "https://m", MoodleCourseId = 99, Enabled = true,
            TokenCipher = _protector.Protect("ws-token")
        }));
    }

    private MoodleSyncDispatcher Sut() =>
        new(_uow, _client, _protector, _clock, Options.Create(_settings),
            Substitute.For<ILogger<MoodleSyncDispatcher>>());

    private static MoodleSyncOp Op(MoodleSyncOpType type, int attempts = 0) => new()
    {
        Id = 1, CourseId = 7L, AssignmentId = 11L, OpType = type, Title = "Algorithms",
        Status = MoodleSyncStatus.Pending, Attempts = attempts
    };

    private void Batch(MoodleSyncOp op) =>
        _syncRepo.GetSendableAsync(Arg.Any<Instant>(), Arg.Any<int>())
                 .Returns(new ValueTask<IReadOnlyCollection<MoodleSyncOp>>(new List<MoodleSyncOp> { op }.AsReadOnly()));

    private void NoMapping() =>
        _syncRepo.GetMappingByAssignmentAsync(11L).Returns(new ValueTask<MoodleItemMapping?>((MoodleItemMapping?)null));

    private void Mapping(long itemId) =>
        _syncRepo.GetMappingByAssignmentAsync(11L)
                 .Returns(new ValueTask<MoodleItemMapping?>(new MoodleItemMapping
                 {
                     AssignmentId = 11L, CourseId = 7L, MoodleItemId = itemId
                 }));

    [Fact]
    public async Task Create_WithoutMapping_CreatesItemAndStoresMapping()
    {
        MoodleSyncOp op = Op(MoodleSyncOpType.Create);
        Batch(op);
        NoMapping();
        OneOf<Success<long>, MoodleError> created = new Success<long>(555);
        _client.CreateItemAsync(Arg.Any<MoodleTarget>(), Arg.Any<MoodleItemPayload>())
               .Returns(new ValueTask<OneOf<Success<long>, MoodleError>>(created));

        await Sut().DispatchPendingAsync(CancellationToken.None);

        op.Status.Should().Be(MoodleSyncStatus.Sent);
        _syncRepo.Received(1).AddMapping(Arg.Is<MoodleItemMapping>(m => m.MoodleItemId == 555));
    }

    [Fact]
    public async Task Update_WithMapping_CallsUpdate()
    {
        MoodleSyncOp op = Op(MoodleSyncOpType.Update);
        Batch(op);
        Mapping(555);
        OneOf<Success, MoodleError> ok = new Success();
        _client.UpdateItemAsync(Arg.Any<MoodleTarget>(), 555, Arg.Any<MoodleItemPayload>())
               .Returns(new ValueTask<OneOf<Success, MoodleError>>(ok));

        await Sut().DispatchPendingAsync(CancellationToken.None);

        op.Status.Should().Be(MoodleSyncStatus.Sent);
        await _client.DidNotReceive().CreateItemAsync(Arg.Any<MoodleTarget>(), Arg.Any<MoodleItemPayload>());
    }

    [Fact]
    public async Task Delete_WithMapping_DeletesItemAndRemovesMapping()
    {
        MoodleSyncOp op = Op(MoodleSyncOpType.Delete);
        Batch(op);
        var mapping = new MoodleItemMapping { AssignmentId = 11L, CourseId = 7L, MoodleItemId = 555 };
        _syncRepo.GetMappingByAssignmentAsync(11L).Returns(new ValueTask<MoodleItemMapping?>(mapping));
        OneOf<Success, MoodleError> ok = new Success();
        _client.DeleteItemAsync(Arg.Any<MoodleTarget>(), 555)
               .Returns(new ValueTask<OneOf<Success, MoodleError>>(ok));

        await Sut().DispatchPendingAsync(CancellationToken.None);

        op.Status.Should().Be(MoodleSyncStatus.Sent);
        _syncRepo.Received(1).RemoveMapping(mapping);
    }

    [Fact]
    public async Task Failure_StaysPendingWithBackoff_ThenDeadLetters()
    {
        MoodleSyncOp op = Op(MoodleSyncOpType.Update, attempts: 2);
        Batch(op);
        Mapping(555);
        OneOf<Success, MoodleError> fail = new MoodleError("moodle down");
        _client.UpdateItemAsync(Arg.Any<MoodleTarget>(), 555, Arg.Any<MoodleItemPayload>())
               .Returns(new ValueTask<OneOf<Success, MoodleError>>(fail));

        await Sut().DispatchPendingAsync(CancellationToken.None);

        op.Attempts.Should().Be(3);
        op.Status.Should().Be(MoodleSyncStatus.Failed);
        op.LastError.Should().Be("moodle down");
    }

    [Fact]
    public async Task DisabledLinkAfterQueue_SkipsWithoutCallingClient()
    {
        _linkRepo.GetByCourseIdAsync(7L).Returns(new ValueTask<MoodleLink?>(new MoodleLink
        {
            CourseId = 7L, MoodleBaseUrl = "https://m", Enabled = false, TokenCipher = "c"
        }));
        MoodleSyncOp op = Op(MoodleSyncOpType.Update);
        Batch(op);

        await Sut().DispatchPendingAsync(CancellationToken.None);

        op.Status.Should().Be(MoodleSyncStatus.Sent);
        await _client.DidNotReceive().UpdateItemAsync(Arg.Any<MoodleTarget>(), Arg.Any<long>(), Arg.Any<MoodleItemPayload>());
    }
}
