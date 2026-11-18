using LeoClassroom.Services.Auth;
using LeoClassroom.Services.Forgejo;
using LeoClassroom.Services;
using LeoClassroom.Persistence.Model;
using LeoClassroom.Persistence.Repositories;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LeoClassroom.Services.Util;
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
    private readonly IAssignmentRepository _assignmentRepo = Substitute.For<IAssignmentRepository>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IForgejoClient _forgejo = Substitute.For<IForgejoClient>();
    private readonly SubmissionReviewService _sut;

    public SubmissionReviewServiceTests()
    {
        _uow.AcceptanceRepository.Returns(_acceptanceRepo);
        _currentUser.StudentId.Returns(OwnerStudentId);
        _currentUser.Roles.Returns(new HashSet<Role> { Role.Teacher });
        _uow.AssignmentRepository.Returns(_assignmentRepo);
        _sut = new SubmissionReviewService(_uow, _currentUser, _forgejo, Options.Create(new ForgejoSettings
                                           {
                                               BaseUrl = "http://forgejo.test", AdminToken = "t",
                                               WebhookSecret = "s", WebhookTargetUrl = "http://backend/hook"
                                           }),
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

    private static readonly Instant AcceptedAt = Instant.FromUtc(2026, 6, 1, 8, 0);

    private static ForgejoCommit Commit(string sha, string date, string? login) =>
        new(sha, new ForgejoCommitDetails(new ForgejoCommitAuthor(date), new ForgejoCommitAuthor(date)),
            login is null ? null : new ForgejoUser(1, login));

    private void CommitsReturn(params ForgejoCommit[] commits) =>
        _forgejo.GetAllCommitsAsync("course-7", "course-a-IF1")
                .Returns(new ValueTask<OneOf<IReadOnlyCollection<ForgejoCommit>, NotFound, ForgejoError>>(commits));

    [Fact]
    public async Task GetAnalytics_CountsOnlyTheStudentsOwnCommitsAfterAccepting()
    {
        Acceptance acceptance = Setup();
        acceptance.AcceptedAt = AcceptedAt;
        CommitsReturn(
            Commit("starter", "2026-05-20T09:00:00Z", OwnerStudentId),      // copied with the starter repository
            Commit("seed", "2026-06-01T08:01:00Z", "leo-classroom-bot"),     // the bot seeding the repository
            Commit("sha-1", "2026-06-01T10:00:00Z", "IF000050"),
            Commit("teacher-fix", "2026-06-01T11:30:00Z", OwnerStudentId),  // a teacher pushing into it later
            Commit("private-mail", "2026-06-02T12:00:00Z", null));           // the student, unlinked e-mail

        var result = await _sut.GetAnalyticsAsync(AcceptanceId);

        CommitAnalyticsView analytics = result.ShouldBe<CommitAnalyticsView>();
        analytics.Commits.Select(commit => commit.Sha).Should().Equal("sha-1", "private-mail");
        analytics.CommitCount.Should().Be(2);
    }

    [Fact]
    public async Task GetAnalytics_ExcludesCoTeacherCommits()
    {
        Acceptance acceptance = Setup();
        acceptance.Assignment.CoTeachers =
        [
            new User { Id = 21, StudentId = "IF000021", FirstName = "C", LastName = "T", Role = Role.Teacher }
        ];
        CommitsReturn(Commit("co-teacher", "2026-06-01T10:00:00Z", "if000021"),
                      Commit("student", "2026-06-01T11:00:00Z", "IF000050"));

        var result = await _sut.GetAnalyticsAsync(AcceptanceId);

        result.ShouldBe<CommitAnalyticsView>().Commits.Select(commit => commit.Sha).Should().Equal("student");
    }

    [Fact]
    public async Task GetAnalytics_DerivesActiveDaysFromCommitsAndRatioFromPushes()
    {
        Acceptance acceptance = Setup();
        acceptance.Analytics.PushCount = 2;
        acceptance.Analytics.CommitCount = 3;
        // two commits on one Vienna day (late evening UTC is already the next day there), one on another
        CommitsReturn(Commit("a", "2026-06-01T10:00:00Z", "IF000050"),
                      Commit("b", "2026-06-01T23:30:00Z", "IF000050"),
                      Commit("c", "2026-06-02T09:00:00Z", "IF000050"));

        var result = await _sut.GetAnalyticsAsync(AcceptanceId);

        CommitAnalyticsView analytics = result.ShouldBe<CommitAnalyticsView>();
        analytics.ActiveDayCount.Should().Be(2);
        analytics.PushCount.Should().Be(2);
        analytics.CommitsPerPush.Should().Be(1.5);
    }

    [Fact]
    public async Task GetAnalytics_InvalidCommitDate_IsAnUpstreamError()
    {
        Setup();
        CommitsReturn(Commit("broken", "not a date", "IF000050"));

        var result = await _sut.GetAnalyticsAsync(AcceptanceId);

        result.ShouldBe<ForgejoError>().StatusCode.Should().Be(502);
    }

    private Assignment SetupSummary(params Acceptance[] acceptances)
    {
        Acceptance template = Setup();
        Assignment assignment = template.Assignment;
        assignment.Deadline = Instant.FromUtc(2026, 6, 10, 21, 59);
        _assignmentRepo.GetWithTeachersAsync(1).Returns(new ValueTask<Assignment?>(assignment));
        _acceptanceRepo.GetByAssignmentWithStudentAsync(1)
                       .Returns(new ValueTask<IReadOnlyCollection<Acceptance>>(acceptances));

        return assignment;
    }

    private static Acceptance Submission(long id, string repoName, SubmissionStatus status = SubmissionStatus.Ready) =>
        new() { Id = id, AssignmentId = 1, StudentId = id, RepoOwner = "course-7", RepoName = repoName, Status = status };

    [Fact]
    public async Task GetCommitSummary_CountsEachSubmissionsOwnAndLateCommits()
    {
        SetupSummary(Submission(1, "course-a-IF1"), Submission(2, "course-a-IF2"));
        CommitsReturn(Commit("on-time", "2026-06-05T10:00:00Z", "IF1"),
                      Commit("late", "2026-06-11T10:00:00Z", "IF1"),
                      Commit("teacher", "2026-06-12T10:00:00Z", OwnerStudentId));
        _forgejo.GetAllCommitsAsync("course-7", "course-a-IF2")
                .Returns(new ValueTask<OneOf<IReadOnlyCollection<ForgejoCommit>, NotFound, ForgejoError>>(
                    Array.Empty<ForgejoCommit>()));

        var result = await _sut.GetCommitSummaryAsync(1);

        result.ShouldBe<IReadOnlyCollection<CommitSummaryView>>().Should().BeEquivalentTo(
        [
            new CommitSummaryView(1, 2, 1),
            new CommitSummaryView(2, 0, 0)
        ]);
    }

    [Fact]
    public async Task GetCommitSummary_UnanswerableRepository_LeavesOnlyThatRowWithoutCounts()
    {
        SetupSummary(Submission(1, "course-a-IF1"), Submission(2, "course-a-IF2"),
                     Submission(3, "course-a-IF3", SubmissionStatus.Provisioning));
        CommitsReturn(Commit("work", "2026-06-05T10:00:00Z", "IF1"));
        _forgejo.GetAllCommitsAsync("course-7", "course-a-IF2")
                .Returns(new ValueTask<OneOf<IReadOnlyCollection<ForgejoCommit>, NotFound, ForgejoError>>(
                    new ForgejoError(502, "Forgejo is unreachable")));

        var result = await _sut.GetCommitSummaryAsync(1);

        result.ShouldBe<IReadOnlyCollection<CommitSummaryView>>().Should().BeEquivalentTo(
        [
            new CommitSummaryView(1, 1, 0),
            new CommitSummaryView(2, null, null),
            new CommitSummaryView(3, null, null)
        ]);
        // a repository that is still being set up is not asked about at all
        await _forgejo.DidNotReceive().GetAllCommitsAsync("course-7", "course-a-IF3");
    }

    [Fact]
    public async Task GetCommitSummary_ByNonTeacher_ReturnsForbidden()
    {
        SetupSummary(Submission(1, "course-a-IF1"));
        _currentUser.StudentId.Returns(OtherStudentId);

        var result = await _sut.GetCommitSummaryAsync(1);

        result.ShouldBe<Forbidden>();
        await _forgejo.DidNotReceive().GetAllCommitsAsync(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task GetCommitSummary_UnknownAssignment_ReturnsNotFound()
    {
        _assignmentRepo.GetWithTeachersAsync(99).Returns(new ValueTask<Assignment?>((Assignment?)null));

        var result = await _sut.GetCommitSummaryAsync(99);

        result.ShouldBe<NotFound>();
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
