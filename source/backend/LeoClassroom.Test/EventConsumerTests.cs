using LeoClassroom.Services.Forgejo;
using LeoClassroom.Persistence.Model;
using LeoClassroom.Persistence.Repositories;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Shared;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace LeoClassroom.Test;

public sealed class AcceptanceLatenessTests
{
    private static readonly Instant Deadline = Instant.FromUtc(2026, 6, 13, 21, 59);

    [Theory]
    [InlineData(DeadlineKind.Soft, -1, false)]
    [InlineData(DeadlineKind.Soft, 0, true)]
    [InlineData(DeadlineKind.Soft, 1, true)]
    [InlineData(DeadlineKind.Soft, null, false)]
    [InlineData(DeadlineKind.Hard, 1, false)]
    [InlineData(DeadlineKind.None, 1, false)]
    public void IsLate_FollowsTheDeadline(DeadlineKind kind, int? secondsAfterDeadline, bool expected)
    {
        var acceptance = new Acceptance
        {
            RepoOwner = "course-7", RepoName = "course-a-IF1",
            LastPushAt = secondsAfterDeadline is { } s ? Deadline.Plus(Duration.FromSeconds(s)) : null
        };
        var assignment = new Assignment
        {
            Id = 1, CourseId = 7, Title = "A", Slug = "a", DeadlineKind = kind, Deadline = Deadline
        };

        acceptance.IsLate(assignment).Should().Be(expected);
    }
}

public sealed class CommitAnalyticsConsumerTests
{
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IAcceptanceRepository _acceptanceRepo = Substitute.For<IAcceptanceRepository>();
    private readonly CommitAnalyticsConsumer _sut;

    public CommitAnalyticsConsumerTests()
    {
        PushTestStudent.Register(_uow);
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

    [Theory]
    [InlineData("leo-classroom-bot")]
    [InlineData("IF000020")]
    public async Task PushBySomeoneOtherThanTheStudent_IsNotCounted(string pusher)
    {
        // the bot seeding the repository and a teacher pushing into it are not the student's activity
        Acceptance acceptance = Setup();

        await _sut.ConsumeAsync(Push(Instant.FromUtc(2026, 6, 13, 9, 0), 3, "aaa") with { Actor = pusher });

        acceptance.Analytics.PushCount.Should().Be(0);
        acceptance.Analytics.CommitCount.Should().Be(0);
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
