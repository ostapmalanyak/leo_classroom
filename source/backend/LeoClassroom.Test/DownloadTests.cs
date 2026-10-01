using LeoClassroom.Services.Auth;
using LeoClassroom.Services.Download;
using LeoClassroom.Persistence.Model;
using LeoClassroom.Persistence.Repositories;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Shared;
using NSubstitute;
using OneOf;
using OneOf.Types;

namespace LeoClassroom.Test;

public sealed class DownloadFolderNamerTests
{
    [Fact]
    public void Base_UsesLastnameFirstname_NeverStudentId()
    {
        DownloadFolderNamer.Base("Mustermann", "Max").Should().Be("mustermann_max");
    }

    [Fact]
    public void Base_SanitisesUnsafeCharacters()
    {
        DownloadFolderNamer.Base("O'Brien", "Anna-Lena").Should().Be("o-brien_anna-lena");
    }

    [Fact]
    public void Base_FallsBackToPlaceholder_WhenNoName()
    {
        DownloadFolderNamer.Base("", "  ", "IF000001").Should().Be("student_if000001");
    }

    [Fact]
    public void Unique_DisambiguatesCollisionsDeterministically()
    {
        var used = new HashSet<string>(StringComparer.Ordinal);

        DownloadFolderNamer.Unique("smith_john", used).Should().Be("smith_john");
        DownloadFolderNamer.Unique("smith_john", used).Should().Be("smith_john_2");
        DownloadFolderNamer.Unique("smith_john", used).Should().Be("smith_john_3");
    }
}

public sealed class SubmissionSnapshotResolverTests
{
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IWebhookEventRepository _webhookRepo = Substitute.For<IWebhookEventRepository>();
    private readonly SubmissionSnapshotResolver _sut;

    private static readonly Instant Deadline = Instant.FromUtc(2026, 6, 13, 21, 59);

    private static readonly Acceptance Acceptance = new()
    {
        Id = 5, AssignmentId = 1, StudentId = 50, RepoOwner = "course-7", RepoName = "course-a-IF1"
    };

    public SubmissionSnapshotResolverTests()
    {
        _uow.WebhookEventRepository.Returns(_webhookRepo);
        _sut = new SubmissionSnapshotResolver(_uow);
    }

    [Fact]
    public async Task Deadline_UsesHeadShaOfLastPushBeforeDeadline()
    {
        var lastPush = new WebhookEvent
        {
            EventType = "push", RepoOwner = "course-7", RepoName = "course-a-IF1",
            ReceivedAt = Deadline.Minus(Duration.FromMinutes(5)),
            Payload = """{"after":"abc123","commits":[{}]}"""
        };
        _webhookRepo.GetLastPushReceivedByAsync("course-7", "course-a-IF1", Deadline)
                    .Returns(new ValueTask<WebhookEvent?>(lastPush));

        SnapshotResolution result = await _sut.ResolveAsync(Acceptance, DownloadSnapshotMode.Deadline, Deadline);

        result.Sha.Should().Be("abc123");
        result.SeededFallback.Should().BeFalse();
    }

    [Fact]
    public async Task Deadline_QueriesByReceiveTime_NotCommitterDate()
    {
        await _sut.ResolveAsync(Acceptance, DownloadSnapshotMode.Deadline, Deadline);

        // The cutoff passed to the repository is the deadline; the query filters on server-receive time.
        await _webhookRepo.Received(1).GetLastPushReceivedByAsync("course-7", "course-a-IF1", Deadline);
    }

    [Fact]
    public async Task Head_ResolvesToTip_WithoutQuery()
    {
        SnapshotResolution result = await _sut.ResolveAsync(Acceptance, DownloadSnapshotMode.Head, Deadline);

        result.Sha.Should().BeNull();
        result.SeededFallback.Should().BeFalse();
        await _webhookRepo.DidNotReceive()
                          .GetLastPushReceivedByAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Instant>());
    }

    [Fact]
    public async Task Deadline_NoDeadline_ResolvesToTip()
    {
        SnapshotResolution result = await _sut.ResolveAsync(Acceptance, DownloadSnapshotMode.Deadline, null);

        result.Sha.Should().BeNull();
        result.SeededFallback.Should().BeFalse();
    }

    [Fact]
    public async Task Deadline_NoQualifyingPush_FallsBackToSeededState()
    {
        _webhookRepo.GetLastPushReceivedByAsync("course-7", "course-a-IF1", Deadline)
                    .Returns(new ValueTask<WebhookEvent?>((WebhookEvent?)null));

        SnapshotResolution result = await _sut.ResolveAsync(Acceptance, DownloadSnapshotMode.Deadline, Deadline);

        result.Sha.Should().BeNull();
        result.SeededFallback.Should().BeTrue();
    }
}

public sealed class DownloadServiceTests
{
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IAssignmentRepository _assignmentRepo = Substitute.For<IAssignmentRepository>();
    private readonly IDownloadJobRepository _jobRepo = Substitute.For<IDownloadJobRepository>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly DownloadService _sut;

    private static readonly Instant Now = Instant.FromUtc(2026, 6, 14, 12, 0);

    public DownloadServiceTests()
    {
        _uow.AssignmentRepository.Returns(_assignmentRepo);
        _uow.DownloadJobRepository.Returns(_jobRepo);
        _clock.GetCurrentInstant().Returns(Now);
        _currentUser.StudentId.Returns("T1");
        _currentUser.Roles.Returns(new HashSet<Role>());
        _sut = new DownloadService(_uow, _currentUser, _clock);
    }

    private Assignment OwnedAssignment(DownloadSnapshotMode mode = DownloadSnapshotMode.Deadline) => new()
    {
        Id = 1, CourseId = 7, Title = "A", Slug = "a", OwnerId = 9,
        Owner = new User { Id = 9, StudentId = "T1", FirstName = "Te", LastName = "Acher" },
        DownloadSnapshotMode = mode
    };

    [Fact]
    public async Task Trigger_NonTeacher_ReturnsForbidden()
    {
        _currentUser.StudentId.Returns("OTHER");
        _assignmentRepo.GetWithTeachersAsync(1).Returns(new ValueTask<Assignment?>(OwnedAssignment()));

        OneOf<Success<long>, NotFound, Forbidden> result = await _sut.TriggerAsync(1, null);

        result.ShouldBe<Forbidden>();
        _jobRepo.DidNotReceive().Add(Arg.Any<DownloadJob>());
    }

    [Fact]
    public async Task Trigger_MissingAssignment_ReturnsNotFound()
    {
        _assignmentRepo.GetWithTeachersAsync(1).Returns(new ValueTask<Assignment?>((Assignment?)null));

        OneOf<Success<long>, NotFound, Forbidden> result = await _sut.TriggerAsync(1, null);

        result.ShouldBe<NotFound>();
    }

    [Fact]
    public async Task Trigger_Owner_CreatesPendingJobWithAssignmentDefaultMode()
    {
        _assignmentRepo.GetWithTeachersAsync(1)
                       .Returns(new ValueTask<Assignment?>(OwnedAssignment(DownloadSnapshotMode.Head)));
        DownloadJob? captured = null;
        _jobRepo.When(r => r.Add(Arg.Any<DownloadJob>())).Do(ci => captured = ci.Arg<DownloadJob>());

        OneOf<Success<long>, NotFound, Forbidden> result = await _sut.TriggerAsync(1, null);

        result.ShouldBe<Success<long>>();
        captured.Should().NotBeNull();
        captured!.Status.Should().Be(DownloadJobStatus.Pending);
        captured.Mode.Should().Be(DownloadSnapshotMode.Head);
        captured.RequestedByStudentId.Should().Be("T1");
        captured.CreatedAt.Should().Be(Now);
        await _uow.Received(1).SaveChangesAsync();
    }

    [Fact]
    public async Task Trigger_ExplicitMode_OverridesDefault()
    {
        _assignmentRepo.GetWithTeachersAsync(1)
                       .Returns(new ValueTask<Assignment?>(OwnedAssignment(DownloadSnapshotMode.Deadline)));
        DownloadJob? captured = null;
        _jobRepo.When(r => r.Add(Arg.Any<DownloadJob>())).Do(ci => captured = ci.Arg<DownloadJob>());

        await _sut.TriggerAsync(1, DownloadSnapshotMode.Head);

        captured!.Mode.Should().Be(DownloadSnapshotMode.Head);
    }

    [Fact]
    public async Task Trigger_AdminBypassesTeacherGate()
    {
        _currentUser.StudentId.Returns("ADMIN");
        _currentUser.Roles.Returns(new HashSet<Role> { Role.Admin });
        _assignmentRepo.GetWithTeachersAsync(1).Returns(new ValueTask<Assignment?>(OwnedAssignment()));

        OneOf<Success<long>, NotFound, Forbidden> result = await _sut.TriggerAsync(1, null);

        result.ShouldBe<Success<long>>();
    }

    [Fact]
    public async Task Artifact_NotReady_ReturnsNotFound()
    {
        _jobRepo.GetByIdAsync(3).Returns(new ValueTask<DownloadJob?>(new DownloadJob
        {
            Id = 3, AssignmentId = 1, RequestedByStudentId = "T1", Status = DownloadJobStatus.Running
        }));
        _assignmentRepo.GetWithTeachersAsync(1).Returns(new ValueTask<Assignment?>(OwnedAssignment()));

        OneOf<DownloadArtifact, NotFound, Forbidden> result = await _sut.GetArtifactAsync(1, 3);

        result.ShouldBe<NotFound>();
    }

    [Fact]
    public async Task Status_JobOfDifferentAssignment_ReturnsNotFound()
    {
        _jobRepo.GetByIdAsync(3).Returns(new ValueTask<DownloadJob?>(new DownloadJob
        {
            Id = 3, AssignmentId = 99, RequestedByStudentId = "T1", Status = DownloadJobStatus.Ready
        }));

        OneOf<DownloadJobView, NotFound, Forbidden> result = await _sut.GetStatusAsync(1, 3);

        result.ShouldBe<NotFound>();
    }
}

public sealed class DownloadCleanupServiceTests
{
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IDownloadJobRepository _jobRepo = Substitute.For<IDownloadJobRepository>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly DownloadCleanupService _sut;

    private static readonly Instant Now = Instant.FromUtc(2026, 6, 14, 12, 0);

    public DownloadCleanupServiceTests()
    {
        _uow.DownloadJobRepository.Returns(_jobRepo);
        _clock.GetCurrentInstant().Returns(Now);
        _sut = new DownloadCleanupService(_uow, _clock, Substitute.For<Microsoft.Extensions.Logging.ILogger<DownloadCleanupService>>());
    }

    [Fact]
    public async Task Cleanup_DeletesExpiredArtifactFileAndClearsPath()
    {
        string path = Path.Combine(Path.GetTempPath(), $"leo-cleanup-{Guid.NewGuid():N}.zip");
        await File.WriteAllTextAsync(path, "x", TestContext.Current.CancellationToken);
        var job = new DownloadJob
        {
            Id = 1, AssignmentId = 1, RequestedByStudentId = "T1", Status = DownloadJobStatus.Ready,
            ArtifactPath = path, ExpiresAt = Now.Minus(Duration.FromHours(1))
        };
        _jobRepo.GetExpiredAsync(Now).Returns(new ValueTask<IReadOnlyCollection<DownloadJob>>(new[] { job }));

        await _sut.CleanupAsync();

        File.Exists(path).Should().BeFalse();
        job.ArtifactPath.Should().BeNull();
        await _uow.Received(1).SaveChangesAsync();
    }

    [Fact]
    public async Task Cleanup_NoExpired_DoesNothing()
    {
        _jobRepo.GetExpiredAsync(Now)
                .Returns(new ValueTask<IReadOnlyCollection<DownloadJob>>(Array.Empty<DownloadJob>()));

        await _sut.CleanupAsync();

        await _uow.DidNotReceive().SaveChangesAsync();
    }
}
