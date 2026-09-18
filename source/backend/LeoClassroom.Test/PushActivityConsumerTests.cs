using LeoClassroom.Services.Forgejo;
using LeoClassroom.Persistence.Model;
using LeoClassroom.Persistence.Repositories;
using LeoClassroom.Persistence.Util;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace LeoClassroom.Test;

public sealed class PushActivityConsumerTests
{
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IAcceptanceRepository _acceptanceRepo = Substitute.For<IAcceptanceRepository>();
    private readonly PushActivityConsumer _sut;

    public PushActivityConsumerTests()
    {
        _uow.AcceptanceRepository.Returns(_acceptanceRepo);
        _sut = new PushActivityConsumer(_uow, Substitute.For<ILogger<PushActivityConsumer>>());
    }

    private static WebhookEnvelope Push(string owner, string repo) => new(
        "push", "student", owner, repo, Instant.FromUtc(2026, 6, 13, 9, 0), Instant.FromUtc(2026, 6, 13, 8, 55), "{}");

    [Fact]
    public async Task Push_MatchingRepo_UpdatesActivity()
    {
        var acceptance = new Acceptance
        {
            Id = 5, AssignmentId = 1, StudentId = 50, RepoOwner = "course-7", RepoName = "course-a-IF1"
        };
        _acceptanceRepo.GetTrackedByRepoAsync("course-7", "course-a-IF1")
                       .Returns(new ValueTask<Acceptance?>(acceptance));

        await _sut.ConsumeAsync(Push("course-7", "course-a-IF1"));

        acceptance.LastPushAt.Should().Be(Instant.FromUtc(2026, 6, 13, 9, 0));
        acceptance.LastCommitAt.Should().Be(Instant.FromUtc(2026, 6, 13, 8, 55));
        await _uow.Received(1).SaveChangesAsync();
    }

    [Fact]
    public async Task Push_UnmatchedRepo_IsIgnored()
    {
        _acceptanceRepo.GetTrackedByRepoAsync(Arg.Any<string>(), Arg.Any<string>())
                       .Returns(new ValueTask<Acceptance?>((Acceptance?)null));

        await _sut.ConsumeAsync(Push("other", "repo"));

        await _uow.DidNotReceive().SaveChangesAsync();
    }

    [Fact]
    public async Task NonPushEvent_IsIgnored()
    {
        await _sut.ConsumeAsync(new WebhookEnvelope("create", "x", "o", "r",
            Instant.FromUtc(2026, 6, 13, 9, 0), null, "{}"));

        await _acceptanceRepo.DidNotReceive().GetTrackedByRepoAsync(Arg.Any<string>(), Arg.Any<string>());
    }
}
