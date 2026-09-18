using LeoClassroom.Services.Audit;
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

public sealed class AssignmentAutoDeleteServiceTests
{
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IAssignmentRepository _assignmentRepo = Substitute.For<IAssignmentRepository>();
    private readonly IDeletionCascade _cascade = Substitute.For<IDeletionCascade>();
    private readonly IForgejoClient _forgejo = Substitute.For<IForgejoClient>();
    private readonly IAuditLog _audit = Substitute.For<IAuditLog>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly AssignmentAutoDeleteService _sut;

    public AssignmentAutoDeleteServiceTests()
    {
        _uow.AssignmentRepository.Returns(_assignmentRepo);
        _clock.GetCurrentInstant().Returns(Instant.FromUtc(2026, 6, 13, 2, 30));
        _sut = new AssignmentAutoDeleteService(_uow, _cascade, _audit, _clock,
                                               Substitute.For<ILogger<AssignmentAutoDeleteService>>());
    }

    [Fact]
    public async Task Run_CascadesDueAssignmentsAndAudits()
    {
        IReadOnlyCollection<long> due = new List<long> { 11L }.AsReadOnly();
        _assignmentRepo.GetDueForAutoDeleteAsync(Arg.Any<Instant>())
                       .Returns(new ValueTask<IReadOnlyCollection<long>>(due));
        var assignment = new Assignment
        {
            Id = 11L, CourseId = 7L, Title = "A", Slug = "a",
            Acceptances = [new Acceptance { Id = 1, AssignmentId = 11L, StudentId = 50, RepoOwner = "course-7", RepoName = "r" }]
        };
        _assignmentRepo.GetWithAcceptancesAsync(11L).Returns(new ValueTask<Assignment?>(assignment));
        _cascade.DeleteAssignmentsAsync(Arg.Any<IReadOnlyCollection<long>>())
                .Returns(new ValueTask<CascadeResult>(new CascadeResult(0, 1, 1, 1, 0, 0)));

        int count = await _sut.RunAsync();

        count.Should().Be(1);
        await _cascade.Received(1).DeleteAssignmentsAsync(Arg.Is<IReadOnlyCollection<long>>(ids => ids.Contains(11L)));
        await _audit.Received(1).RecordAsync(AuditAction.AssignmentAutoDeleted, "Assignment", "11",
                                             Arg.Any<IReadOnlyDictionary<string, string>?>());
    }

    [Fact]
    public async Task Run_NothingDue_DeletesNothing()
    {
        _assignmentRepo.GetDueForAutoDeleteAsync(Arg.Any<Instant>())
                       .Returns(new ValueTask<IReadOnlyCollection<long>>(new List<long>().AsReadOnly()));

        int count = await _sut.RunAsync();

        count.Should().Be(0);
        await _assignmentRepo.DidNotReceive().DeleteByIdAsync(Arg.Any<long>());
    }
}
