using LeoClassroom.Services.Forgejo;
using LeoClassroom.Persistence.Model;
using LeoClassroom.Persistence.Repositories;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Shared;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace LeoClassroom.Test;

public sealed class LateFlaggingConsumerTests
{
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IAcceptanceRepository _acceptanceRepo = Substitute.For<IAcceptanceRepository>();
    private readonly IAssignmentRepository _assignmentRepo = Substitute.For<IAssignmentRepository>();
    private readonly LateFlaggingConsumer _sut;

    private static readonly Instant Deadline = Instant.FromUtc(2026, 6, 13, 21, 59);

    public LateFlaggingConsumerTests()
    {
        _uow.AcceptanceRepository.Returns(_acceptanceRepo);
        _uow.AssignmentRepository.Returns(_assignmentRepo);
        _sut = new LateFlaggingConsumer(_uow, Substitute.For<ILogger<LateFlaggingConsumer>>());
    }

    private static WebhookEnvelope Push(Instant receivedAt, Instant? committerDate = null) => new(
        "push", "student", "course-7", "course-a-IF1", receivedAt, committerDate, "{}");

    private Acceptance Setup(DeadlineKind kind, bool alreadyLate = false)
    {
        var acceptance = new Acceptance
        {
            Id = 5, AssignmentId = 1, StudentId = 50, RepoOwner = "course-7", RepoName = "course-a-IF1",
            Late = alreadyLate
        };
        _acceptanceRepo.GetTrackedByRepoAsync("course-7", "course-a-IF1")
                       .Returns(new ValueTask<Acceptance?>(acceptance));
        _assignmentRepo.GetByIdAsync(1).Returns(new ValueTask<Assignment?>(new Assignment
        {
            Id = 1, CourseId = 7, Title = "A", Slug = "a", DeadlineKind = kind, Deadline = Deadline
        }));

        return acceptance;
    }

    [Fact]
    public async Task Push_AtOrAfterSoftDeadline_FlagsLate()
    {
        Acceptance acceptance = Setup(DeadlineKind.Soft);
        Instant after = Deadline.Plus(Duration.FromMinutes(1));

        await _sut.ConsumeAsync(Push(after));

        acceptance.Late.Should().BeTrue();
        acceptance.LateSince.Should().Be(after);
        await _uow.Received(1).SaveChangesAsync();
    }

    [Fact]
    public async Task Push_BeforeSoftDeadline_DoesNotFlag()
    {
        Acceptance acceptance = Setup(DeadlineKind.Soft);

        await _sut.ConsumeAsync(Push(Deadline.Minus(Duration.FromMinutes(1))));

        acceptance.Late.Should().BeFalse();
        await _uow.DidNotReceive().SaveChangesAsync();
    }

    [Fact]
    public async Task Push_AfterDeadline_WithEarlierCommitterDate_StillFlagsLate()
    {
        Acceptance acceptance = Setup(DeadlineKind.Soft);
        Instant after = Deadline.Plus(Duration.FromHours(2));

        await _sut.ConsumeAsync(Push(after, committerDate: Deadline.Minus(Duration.FromHours(5))));

        acceptance.Late.Should().BeTrue();
        acceptance.LateSince.Should().Be(after);
    }

    [Fact]
    public async Task Push_OnHardDeadline_IsNotFlagged()
    {
        Acceptance acceptance = Setup(DeadlineKind.Hard);

        await _sut.ConsumeAsync(Push(Deadline.Plus(Duration.FromHours(1))));

        acceptance.Late.Should().BeFalse();
        await _uow.DidNotReceive().SaveChangesAsync();
    }

    [Fact]
    public async Task Push_WhenAlreadyLate_DoesNotResave()
    {
        Setup(DeadlineKind.Soft, alreadyLate: true);

        await _sut.ConsumeAsync(Push(Deadline.Plus(Duration.FromHours(1))));

        await _uow.DidNotReceive().SaveChangesAsync();
    }
}

public sealed class CommitAnalyticsConsumerTests
{
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IAcceptanceRepository _acceptanceRepo = Substitute.For<IAcceptanceRepository>();
    private readonly CommitAnalyticsConsumer _sut;

    public CommitAnalyticsConsumerTests()
    {
        _uow.AcceptanceRepository.Returns(_acceptanceRepo);
        _sut = new CommitAnalyticsConsumer(_uow, Substitute.For<ILogger<CommitAnalyticsConsumer>>());
    }

    private Acceptance Setup()
    {
        var acceptance = new Acceptance
        {
            Id = 5, AssignmentId = 1, StudentId = 50, RepoOwner = "course-7", RepoName = "course-a-IF1"
        };
        _acceptanceRepo.GetTrackedByRepoAsync("course-7", "course-a-IF1")
                       .Returns(new ValueTask<Acceptance?>(acceptance));

        return acceptance;
    }

    private static WebhookEnvelope Push(Instant receivedAt, int commits, string sha) => new(
        "push", "student", "course-7", "course-a-IF1", receivedAt, null,
        $$"""{"after":"{{sha}}","commits":[{{string.Join(",", Enumerable.Repeat("{}", commits))}}]}""");

    [Fact]
    public async Task TwoPushesOnSameDay_IncrementCountsOneActiveDay()
    {
        Acceptance acceptance = Setup();

        await _sut.ConsumeAsync(Push(Instant.FromUtc(2026, 6, 13, 9, 0), 2, "aaa"));
        await _sut.ConsumeAsync(Push(Instant.FromUtc(2026, 6, 13, 15, 0), 3, "bbb"));

        acceptance.Analytics.PushCount.Should().Be(2);
        acceptance.Analytics.CommitCount.Should().Be(5);
        acceptance.Analytics.ActiveDayCount.Should().Be(1);
        acceptance.Analytics.FirstPushAt.Should().Be(Instant.FromUtc(2026, 6, 13, 9, 0));
        acceptance.Analytics.LastPushAt.Should().Be(Instant.FromUtc(2026, 6, 13, 15, 0));
    }

    [Fact]
    public async Task PushesOnDifferentDays_IncrementActiveDayCount()
    {
        Acceptance acceptance = Setup();

        await _sut.ConsumeAsync(Push(Instant.FromUtc(2026, 6, 13, 9, 0), 1, "aaa"));
        await _sut.ConsumeAsync(Push(Instant.FromUtc(2026, 6, 14, 9, 0), 1, "bbb"));

        acceptance.Analytics.ActiveDayCount.Should().Be(2);
    }

    [Fact]
    public async Task ReprocessingSamePush_IsIdempotent()
    {
        Acceptance acceptance = Setup();
        WebhookEnvelope push = Push(Instant.FromUtc(2026, 6, 13, 9, 0), 2, "aaa");

        await _sut.ConsumeAsync(push);
        await _sut.ConsumeAsync(push);

        acceptance.Analytics.PushCount.Should().Be(1);
        acceptance.Analytics.CommitCount.Should().Be(2);
        await _uow.Received(1).SaveChangesAsync();
    }

    [Fact]
    public async Task UnmatchedRepo_IsIgnored()
    {
        _acceptanceRepo.GetTrackedByRepoAsync(Arg.Any<string>(), Arg.Any<string>())
                       .Returns(new ValueTask<Acceptance?>((Acceptance?)null));

        await _sut.ConsumeAsync(Push(Instant.FromUtc(2026, 6, 13, 9, 0), 1, "aaa"));

        await _uow.DidNotReceive().SaveChangesAsync();
    }
}

public sealed class FeedbackReviewConsumerTests
{
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IAcceptanceRepository _acceptanceRepo = Substitute.For<IAcceptanceRepository>();
    private readonly FeedbackReviewConsumer _sut;

    public FeedbackReviewConsumerTests()
    {
        _uow.AcceptanceRepository.Returns(_acceptanceRepo);
        _sut = new FeedbackReviewConsumer(_uow, Substitute.For<ILogger<FeedbackReviewConsumer>>());
    }

    private Acceptance Setup(FeedbackState state)
    {
        var acceptance = new Acceptance
        {
            Id = 5, AssignmentId = 1, StudentId = 50, RepoOwner = "course-7", RepoName = "course-a-IF1",
            FeedbackState = state, FeedbackReadAt = Instant.FromUtc(2026, 6, 1, 0, 0)
        };
        _acceptanceRepo.GetTrackedByRepoAsync("course-7", "course-a-IF1")
                       .Returns(new ValueTask<Acceptance?>(acceptance));

        return acceptance;
    }

    private static WebhookEnvelope Review() => new(
        "pull_request_review", "teacher", "course-7", "course-a-IF1", Instant.FromUtc(2026, 6, 14, 9, 0), null, "{}");

    [Fact]
    public async Task Review_SetsUnreadAndClearsReadAt()
    {
        Acceptance acceptance = Setup(FeedbackState.Read);

        await _sut.ConsumeAsync(Review());

        acceptance.FeedbackState.Should().Be(FeedbackState.Unread);
        acceptance.FeedbackReadAt.Should().BeNull();
        await _uow.Received(1).SaveChangesAsync();
    }

    [Fact]
    public async Task Review_WhenAlreadyUnread_DoesNotResave()
    {
        Setup(FeedbackState.Unread);

        await _sut.ConsumeAsync(Review());

        await _uow.DidNotReceive().SaveChangesAsync();
    }

    [Fact]
    public async Task NonReviewEvent_IsIgnored()
    {
        await _sut.ConsumeAsync(new WebhookEnvelope(
            "push", "x", "course-7", "course-a-IF1", Instant.FromUtc(2026, 6, 14, 9, 0), null, "{}"));

        await _acceptanceRepo.DidNotReceive().GetTrackedByRepoAsync(Arg.Any<string>(), Arg.Any<string>());
    }
}
