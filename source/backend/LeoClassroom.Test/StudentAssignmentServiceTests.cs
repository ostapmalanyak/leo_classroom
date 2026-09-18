using LeoClassroom.Services.Auth;
using LeoClassroom.Services;
using LeoClassroom.Persistence.Model;
using LeoClassroom.Persistence.Repositories;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Shared;
using Microsoft.Extensions.Logging;
using NSubstitute;
using LeoClassroom.Services.Forgejo;
using OneOf.Types;

namespace LeoClassroom.Test;

public sealed class StudentAssignmentServiceTests
{
    private const long AssignmentId = 11L;
    private const long StudentLocalId = 50L;
    private const string StudentNumber = "IF000050";

    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IAssignmentRepository _assignmentRepo = Substitute.For<IAssignmentRepository>();
    private readonly IAcceptanceRepository _acceptanceRepo = Substitute.For<IAcceptanceRepository>();
    private readonly IUserRepository _userRepo = Substitute.For<IUserRepository>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly StudentAssignmentService _sut;

    public StudentAssignmentServiceTests()
    {
        _uow.AssignmentRepository.Returns(_assignmentRepo);
        _uow.AcceptanceRepository.Returns(_acceptanceRepo);
        _uow.UserRepository.Returns(_userRepo);
        _currentUser.StudentId.Returns(StudentNumber);
        _userRepo.GetIdByStudentIdAsync(StudentNumber).Returns(new ValueTask<long?>(StudentLocalId));
        _clock.GetCurrentInstant().Returns(Instant.FromUtc(2026, 6, 13, 12, 0));

        _sut = new StudentAssignmentService(_uow, _currentUser, _clock,
                                            Substitute.For<ILogger<StudentAssignmentService>>());
    }

    private static Assignment AssignmentInCourse() => new()
    {
        Id = AssignmentId, CourseId = 7L, Title = "Algorithms", Slug = "algorithms",
        Course = new Course { Id = 7L, Title = "Course", RosterId = 1, ForgejoOrg = "course-7" }
    };

    [Fact]
    public async Task Accept_WhenAssignedAndNew_CreatesProvisioningForTheEndpointToQueueAfterCommit()
    {
        _assignmentRepo.GetByIdAsync(AssignmentId).Returns(new ValueTask<Assignment?>(AssignmentInCourse()));
        _assignmentRepo.IsStudentAssignedAsync(AssignmentId, StudentLocalId).Returns(new ValueTask<bool>(true));
        _acceptanceRepo.ExistsAsync(AssignmentId, StudentLocalId).Returns(new ValueTask<bool>(false));

        var result = await _sut.AcceptAsync(AssignmentId);

        result.ShouldBe<Success<Acceptance>>().Value.Status.Should().Be(SubmissionStatus.Provisioning);
        _acceptanceRepo.Received(1).Add(Arg.Is<Acceptance>(a => a.Status == SubmissionStatus.Provisioning
                                                                && a.RepoOwner == "course-7"));
        await _uow.Received(1).SaveChangesAsync();
    }

    [Fact]
    public async Task Accept_NotInRoster_ReturnsForbidden()
    {
        _assignmentRepo.GetByIdAsync(AssignmentId).Returns(new ValueTask<Assignment?>(AssignmentInCourse()));
        _assignmentRepo.IsStudentAssignedAsync(AssignmentId, StudentLocalId).Returns(new ValueTask<bool>(false));

        var result = await _sut.AcceptAsync(AssignmentId);

        result.ShouldBe<Forbidden>();
        await _uow.DidNotReceive().SaveChangesAsync();
    }

    [Fact]
    public async Task Accept_AlreadyAccepted_ReturnsAlreadyExists()
    {
        _assignmentRepo.GetByIdAsync(AssignmentId).Returns(new ValueTask<Assignment?>(AssignmentInCourse()));
        _assignmentRepo.IsStudentAssignedAsync(AssignmentId, StudentLocalId).Returns(new ValueTask<bool>(true));
        _acceptanceRepo.ExistsAsync(AssignmentId, StudentLocalId).Returns(new ValueTask<bool>(true));

        var result = await _sut.AcceptAsync(AssignmentId);

        result.ShouldBe<AlreadyExists>();
        _acceptanceRepo.DidNotReceive().Add(Arg.Any<Acceptance>());
    }

    [Theory]
    [InlineData(SubmissionStatus.Failed, true)]
    [InlineData(SubmissionStatus.Provisioning, false)]
    [InlineData(SubmissionStatus.Ready, false)]
    public async Task Retry_OnlyFailedAcceptanceNeedsProvisioning(SubmissionStatus status, bool needsProvisioning)
    {
        var acceptance = new Acceptance
        {
            Id = 5, AssignmentId = AssignmentId, StudentId = StudentLocalId, RepoOwner = "course-7",
            RepoName = "r", Status = status
        };
        _acceptanceRepo.GetTrackedByAssignmentAndStudentAsync(AssignmentId, StudentLocalId)
                       .Returns(new ValueTask<Acceptance?>(acceptance));

        var result = await _sut.RetryAsync(AssignmentId);

        AcceptanceRetry retry = result.ShouldBe<AcceptanceRetry>();
        retry.NeedsProvisioning.Should().Be(needsProvisioning);
        retry.Acceptance.Should().BeSameAs(acceptance);
        acceptance.Status.Should().Be(needsProvisioning ? SubmissionStatus.Provisioning : status);
        await _uow.Received(needsProvisioning ? 1 : 0).SaveChangesAsync();
    }

    [Fact]
    public async Task Retry_MissingAcceptance_ReturnsNotFoundWithoutSaving()
    {
        var result = await _sut.RetryAsync(AssignmentId);

        result.ShouldBe<NotFound>();
        await _uow.DidNotReceive().SaveChangesAsync();
    }

    [Fact]
    public async Task List_SplitsAcceptedAndUnaccepted()
    {
        IReadOnlyCollection<AssignedAssignment> all = new List<AssignedAssignment>
        {
            new(1L, 7L, "Course", "A", null, DeadlineKind.None, true, SubmissionStatus.Ready),
            new(2L, 7L, "Course", "B", null, DeadlineKind.None, false, null)
        }.AsReadOnly();
        _assignmentRepo.GetAssignedToStudentAsync(StudentLocalId)
                       .Returns(new ValueTask<IReadOnlyCollection<AssignedAssignment>>(all));

        StudentAssignmentList result = await _sut.ListAsync();

        result.Accepted.Should().ContainSingle(a => a.Id == 1L);
        result.Unaccepted.Should().ContainSingle(a => a.Id == 2L);
    }

    [Fact]
    public async Task ConfirmFeedbackRead_WhenUnread_SetsReadWithTimestamp()
    {
        var acceptance = new Acceptance
        {
            Id = 5, AssignmentId = AssignmentId, StudentId = StudentLocalId, RepoOwner = "course-7",
            RepoName = "r", FeedbackState = FeedbackState.Unread
        };
        _acceptanceRepo.GetTrackedByAssignmentAndStudentAsync(AssignmentId, StudentLocalId)
                       .Returns(new ValueTask<Acceptance?>(acceptance));

        var result = await _sut.ConfirmFeedbackReadAsync(AssignmentId);

        result.ShouldBe<Success>();
        acceptance.FeedbackState.Should().Be(FeedbackState.Read);
        acceptance.FeedbackReadAt.Should().Be(Instant.FromUtc(2026, 6, 13, 12, 0));
        await _uow.Received(1).SaveChangesAsync();
    }

    [Fact]
    public async Task ConfirmFeedbackRead_NoAcceptance_ReturnsNotFound()
    {
        _acceptanceRepo.GetTrackedByAssignmentAndStudentAsync(AssignmentId, StudentLocalId)
                       .Returns(new ValueTask<Acceptance?>((Acceptance?)null));

        var result = await _sut.ConfirmFeedbackReadAsync(AssignmentId);

        result.ShouldBe<NotFound>();
    }
}
