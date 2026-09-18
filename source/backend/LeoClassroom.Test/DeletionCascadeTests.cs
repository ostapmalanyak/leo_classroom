using LeoClassroom.Services;
using LeoClassroom.Services.Forgejo;
using LeoClassroom.Persistence.Repositories;
using LeoClassroom.Persistence.Util;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OneOf;
using OneOf.Types;

namespace LeoClassroom.Test;

/// <summary>
///     The cascade is the one place that keeps the database and Forgejo in step, so the ordering it guarantees is
///     what these tests are about
/// </summary>
public sealed class DeletionCascadeTests
{
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IAssignmentRepository _assignments = Substitute.For<IAssignmentRepository>();
    private readonly IAcceptanceRepository _acceptances = Substitute.For<IAcceptanceRepository>();
    private readonly ICourseRepository _courses = Substitute.For<ICourseRepository>();
    private readonly IForgejoClient _forgejo = Substitute.For<IForgejoClient>();
    private readonly DeletionCascade _sut;

    public DeletionCascadeTests()
    {
        _uow.AssignmentRepository.Returns(_assignments);
        _uow.AcceptanceRepository.Returns(_acceptances);
        _uow.CourseRepository.Returns(_courses);

        _forgejo.DeleteRepoAsync(Arg.Any<string>(), Arg.Any<string>())
                .Returns(new ValueTask<OneOf<Success, ForgejoError>>(new Success()));
        _forgejo.DeleteOrgAsync(Arg.Any<string>())
                .Returns(new ValueTask<OneOf<Success, ForgejoError>>(new Success()));

        _assignments.GetIdsByCoursesAsync(Arg.Any<IReadOnlyCollection<long>>())
                    .Returns(new ValueTask<IReadOnlyCollection<long>>([]));
        _acceptances.GetByAssignmentsAsync(Arg.Any<IReadOnlyCollection<long>>())
                    .Returns(new ValueTask<IReadOnlyCollection<AcceptanceRepoRef>>([]));
        _courses.GetForgejoOrgsAsync(Arg.Any<IReadOnlyCollection<long>>())
                .Returns(new ValueTask<IReadOnlyCollection<string>>([]));
        _acceptances.DeleteByIdsAsync(Arg.Any<IReadOnlyCollection<long>>()).Returns(new ValueTask<int>(0));
        _assignments.DeleteByIdsAsync(Arg.Any<IReadOnlyCollection<long>>()).Returns(new ValueTask<int>(0));
        _courses.DeleteByIdsAsync(Arg.Any<IReadOnlyCollection<long>>()).Returns(new ValueTask<int>(0));

        _sut = new DeletionCascade(_uow, _forgejo, Substitute.For<ILogger<DeletionCascade>>());
    }

    private static AcceptanceRepoRef Acceptance(long id, long assignmentId = 1) =>
        new(id, 99, assignmentId, "org", $"repo-{id}");

    [Fact]
    public async Task DeleteAcceptancesAsync_RemovesTheRepositoryBeforeTheRowThatNamesIt()
    {
        _acceptances.DeleteByIdsAsync(Arg.Any<IReadOnlyCollection<long>>()).Returns(new ValueTask<int>(1));

        CascadeResult result = await _sut.DeleteAcceptancesAsync([Acceptance(1)]);

        Received.InOrder(() =>
        {
            _forgejo.DeleteRepoAsync("org", "repo-1");
            _acceptances.DeleteByIdsAsync(Arg.Any<IReadOnlyCollection<long>>());
        });
        result.Repositories.Should().Be(1);
        result.Acceptances.Should().Be(1);
        result.ForgejoFailures.Should().Be(0);
    }

    [Fact]
    public async Task DeleteAcceptancesAsync_FailingRepository_StillDeletesTheRowAndIsReported()
    {
        _forgejo.DeleteRepoAsync("org", "repo-1")
                .Returns(new ValueTask<OneOf<Success, ForgejoError>>(new ForgejoError(500, "boom")));
        _acceptances.DeleteByIdsAsync(Arg.Any<IReadOnlyCollection<long>>()).Returns(new ValueTask<int>(1));

        CascadeResult result = await _sut.DeleteAcceptancesAsync([Acceptance(1)]);

        // an orphaned repository is recoverable, a half-deleted assignment is not
        result.Acceptances.Should().Be(1);
        result.ForgejoFailures.Should().Be(1);
        result.Repositories.Should().Be(0);
    }

    [Fact]
    public async Task DeleteAcceptancesAsync_Nothing_TouchesNothing()
    {
        CascadeResult result = await _sut.DeleteAcceptancesAsync([]);

        result.Should().Be(CascadeResult.Empty);
        await _acceptances.DidNotReceive().DeleteByIdsAsync(Arg.Any<IReadOnlyCollection<long>>());
    }

    [Fact]
    public async Task DeleteAssignmentsAsync_DeletesItsAcceptancesFirst()
    {
        _acceptances.GetByAssignmentsAsync(Arg.Any<IReadOnlyCollection<long>>())
                    .Returns(new ValueTask<IReadOnlyCollection<AcceptanceRepoRef>>(
                        new List<AcceptanceRepoRef> { Acceptance(1), Acceptance(2) }.AsReadOnly()));

        await _sut.DeleteAssignmentsAsync([20]);

        Received.InOrder(() =>
        {
            _forgejo.DeleteRepoAsync("org", "repo-1");
            _acceptances.DeleteByIdsAsync(Arg.Any<IReadOnlyCollection<long>>());
            _assignments.DeleteByIdsAsync(Arg.Any<IReadOnlyCollection<long>>());
        });
    }

    [Fact]
    public async Task DeleteCoursesAsync_ReadsTheOrgBeforeDeletingAndRemovesItLast()
    {
        _courses.GetForgejoOrgsAsync(Arg.Any<IReadOnlyCollection<long>>())
                .Returns(new ValueTask<IReadOnlyCollection<string>>(new List<string> { "algorithms-10" }
                             .AsReadOnly()));
        _assignments.GetIdsByCoursesAsync(Arg.Any<IReadOnlyCollection<long>>())
                    .Returns(new ValueTask<IReadOnlyCollection<long>>(new List<long> { 20 }.AsReadOnly()));
        _acceptances.GetByAssignmentsAsync(Arg.Any<IReadOnlyCollection<long>>())
                    .Returns(new ValueTask<IReadOnlyCollection<AcceptanceRepoRef>>(
                        new List<AcceptanceRepoRef> { Acceptance(1, 20) }.AsReadOnly()));

        CascadeResult result = await _sut.DeleteCoursesAsync([10]);

        // Forgejo refuses to drop an organisation that still owns repositories, so the repos have to go first
        Received.InOrder(() =>
        {
            _courses.GetForgejoOrgsAsync(Arg.Any<IReadOnlyCollection<long>>());
            _forgejo.DeleteRepoAsync("org", "repo-1");
            _assignments.DeleteByIdsAsync(Arg.Any<IReadOnlyCollection<long>>());
            _forgejo.DeleteOrgAsync("algorithms-10");
            _courses.DeleteByIdsAsync(Arg.Any<IReadOnlyCollection<long>>());
        });
        result.Organisations.Should().Be(1);
    }

    [Fact]
    public async Task DeleteCoursesAsync_FailingOrgDeletion_IsReportedWithoutStoppingTheRowDeletion()
    {
        _courses.GetForgejoOrgsAsync(Arg.Any<IReadOnlyCollection<long>>())
                .Returns(new ValueTask<IReadOnlyCollection<string>>(new List<string> { "algorithms-10" }
                             .AsReadOnly()));
        _forgejo.DeleteOrgAsync("algorithms-10")
                .Returns(new ValueTask<OneOf<Success, ForgejoError>>(new ForgejoError(500, "not empty")));

        CascadeResult result = await _sut.DeleteCoursesAsync([10]);

        result.ForgejoFailures.Should().Be(1);
        result.Organisations.Should().Be(0);
        await _courses.Received(1).DeleteByIdsAsync(Arg.Any<IReadOnlyCollection<long>>());
    }
}
