using LeoClassroom.Services.Auth;
using LeoClassroom.Services.Forgejo;
using LeoClassroom.Services;
using LeoClassroom.Persistence.Model;
using LeoClassroom.Persistence.Repositories;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Shared;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OneOf;
using OneOf.Types;

namespace LeoClassroom.Test;

public sealed class SubmissionReviewServiceTests
{
    private const long AcceptanceId = 5L;
    private const string OwnerStudentId = "IF000020";
    private const string OtherStudentId = "IF000099";

    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IAcceptanceRepository _acceptanceRepo = Substitute.For<IAcceptanceRepository>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IForgejoClient _forgejo = Substitute.For<IForgejoClient>();
    private readonly SubmissionReviewService _sut;

    public SubmissionReviewServiceTests()
    {
        _uow.AcceptanceRepository.Returns(_acceptanceRepo);
        _currentUser.StudentId.Returns(OwnerStudentId);
        _currentUser.Roles.Returns(new HashSet<Role> { Role.Teacher });
        _sut = new SubmissionReviewService(_uow, _currentUser, _forgejo,
                                           Substitute.For<ILogger<SubmissionReviewService>>());
    }

    private Acceptance Setup(long? feedbackPrNumber = null)
    {
        var acceptance = new Acceptance
        {
            Id = AcceptanceId, AssignmentId = 1, StudentId = 50, RepoOwner = "course-7", RepoName = "course-a-IF1",
            FeedbackPrNumber = feedbackPrNumber,
            Assignment = new Assignment
            {
                Id = 1, CourseId = 7, Title = "A", Slug = "a", OwnerId = 20,
                Owner = new User
                {
                    Id = 20, StudentId = OwnerStudentId, FirstName = "O", LastName = "W", Role = Role.Teacher
                },
                CoTeachers = []
            }
        };
        _acceptanceRepo.GetTrackedWithAssignmentTeachersAsync(AcceptanceId)
                       .Returns(new ValueTask<Acceptance?>(acceptance));

        return acceptance;
    }

    private void RepoReturns(string defaultBranch)
    {
        OneOf<ForgejoRepo, NotFound, ForgejoError> repo =
            new ForgejoRepo(1, "course-a-IF1", "course-7/course-a-IF1", "url", "html", defaultBranch);
        _forgejo.GetRepoAsync("course-7", "course-a-IF1")
                .Returns(new ValueTask<OneOf<ForgejoRepo, NotFound, ForgejoError>>(repo));
    }

    [Fact]
    public async Task OpenFeedbackPr_WhenNoneExists_CreatesBranchAndPr()
    {
        Acceptance acceptance = Setup();
        RepoReturns("main");
        OneOf<ForgejoPullRequest, NotFound, ForgejoError> none = new NotFound();
        _forgejo.FindOpenPullRequestAsync("course-7", "course-a-IF1", "main", "feedback")
                .Returns(new ValueTask<OneOf<ForgejoPullRequest, NotFound, ForgejoError>>(none));
        OneOf<Success, ForgejoError> branch = new Success();
        _forgejo.CreateBranchAsync("course-7", "course-a-IF1", "feedback", "main")
                .Returns(new ValueTask<OneOf<Success, ForgejoError>>(branch));
        OneOf<Success<ForgejoPullRequest>, AlreadyExists, ForgejoError> created =
            new Success<ForgejoPullRequest>(new ForgejoPullRequest(
                42, "html/pulls/42", new ForgejoPrBranch("main"), new ForgejoPrBranch("feedback")));
        _forgejo.CreatePullRequestAsync("course-7", "course-a-IF1", "main", "feedback",
                                        Arg.Any<string>(), Arg.Any<string>())
                .Returns(new ValueTask<OneOf<Success<ForgejoPullRequest>, AlreadyExists, ForgejoError>>(created));

        var result = await _sut.OpenFeedbackPrAsync(AcceptanceId);

        result.ShouldBe<Success<FeedbackPr>>().Value.Number.Should().Be(42);
        acceptance.FeedbackPrNumber.Should().Be(42);
    }

    [Fact]
    public async Task OpenFeedbackPr_WhenOneOpen_ReusesWithoutCreating()
    {
        Setup();
        RepoReturns("main");
        OneOf<ForgejoPullRequest, NotFound, ForgejoError> existing = new ForgejoPullRequest(
            7, "html/pulls/7", new ForgejoPrBranch("main"), new ForgejoPrBranch("feedback"));
        _forgejo.FindOpenPullRequestAsync("course-7", "course-a-IF1", "main", "feedback")
                .Returns(new ValueTask<OneOf<ForgejoPullRequest, NotFound, ForgejoError>>(existing));

        var result = await _sut.OpenFeedbackPrAsync(AcceptanceId);

        result.ShouldBe<Success<FeedbackPr>>().Value.Number.Should().Be(7);
        await _forgejo.DidNotReceive().CreatePullRequestAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task OpenFeedbackPr_ByNonTeacher_ReturnsForbidden()
    {
        Setup();
        _currentUser.StudentId.Returns(OtherStudentId);
        _currentUser.Roles.Returns(new HashSet<Role> { Role.Teacher });

        var result = await _sut.OpenFeedbackPrAsync(AcceptanceId);

        result.ShouldBe<Forbidden>();
    }

    [Fact]
    public async Task GetAnalytics_ReturnsRollupWithCommitsPerPush()
    {
        Acceptance acceptance = Setup();
        acceptance.Analytics.PushCount = 4;
        acceptance.Analytics.ActiveDayCount = 3;
        _forgejo.GetAllCommitsAsync("course-7", "course-a-IF1")
                .Returns(new ValueTask<OneOf<IReadOnlyCollection<ForgejoCommit>, NotFound, ForgejoError>>(
                    new ForgejoCommit[]
                    {
                        new("sha-1", new ForgejoCommitDetails(
                            new ForgejoCommitAuthor("2026-06-01T10:00:00Z")), new ForgejoUser(1, "IF000050")),
                        new("sha-2", new ForgejoCommitDetails(
                            new ForgejoCommitAuthor("2026-06-01T11:00:00Z")), new ForgejoUser(1, "IF000050")),
                        new("other", new ForgejoCommitDetails(
                            new ForgejoCommitAuthor("2026-06-01T12:00:00Z")), new ForgejoUser(2, "IF000099"))
                    }));

        var result = await _sut.GetAnalyticsAsync(AcceptanceId);

        CommitAnalyticsView analytics = result.ShouldBe<CommitAnalyticsView>();
        analytics.PushCount.Should().Be(4);
        analytics.CommitCount.Should().Be(3);
        analytics.CommitsPerPush.Should().Be(0.75);
        analytics.ActiveDayCount.Should().Be(3);
        analytics.Commits.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetAnalytics_ByNonTeacher_ReturnsForbidden()
    {
        Setup();
        _currentUser.StudentId.Returns(OtherStudentId);
        _currentUser.Roles.Returns(new HashSet<Role> { Role.Teacher });

        var result = await _sut.GetAnalyticsAsync(AcceptanceId);

        result.ShouldBe<Forbidden>();
    }
}
