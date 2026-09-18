using LeoClassroom.Services.Forgejo;
using LeoClassroom.Persistence.Model;
using LeoClassroom.Persistence.Repositories;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Shared;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OneOf;
using OneOf.Types;

namespace LeoClassroom.Test;

public sealed class ReconciliationServiceTests
{
    private const long AssignmentId = 1L;
    private const string Org = "algo-5";
    private const string RepoName = "algo-a1-if_s";
    private const string StudentIf = "IF_S";
    private const string TeacherIf = "IF_T";
    private const long TeamId = 7L;

    private readonly IForgejoClient _forgejo = Substitute.For<IForgejoClient>();
    private readonly IAssignmentRepository _assignmentRepo = Substitute.For<IAssignmentRepository>();
    private readonly ReconciliationService _sut;

    public ReconciliationServiceTests()
    {
        var uow = Substitute.For<IUnitOfWork>();
        uow.AssignmentRepository.Returns(_assignmentRepo);
        var clock = Substitute.For<IClock>();
        clock.GetCurrentInstant().Returns(Instant.FromUtc(2026, 3, 1, 12, 0, 0));

        _forgejo.EnsureOrgAsync(Arg.Any<string>())
                .Returns(new ValueTask<OneOf<Success, ForgejoError>>(new Success()));
        _forgejo.EnsureTeamAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CollaboratorPermission>())
                .Returns(new ValueTask<OneOf<Success<long>, ForgejoError>>(new Success<long>(TeamId)));
        _forgejo.AddTeamMemberAsync(Arg.Any<long>(), Arg.Any<string>())
                .Returns(new ValueTask<OneOf<Success, ForgejoError>>(new Success()));
        _forgejo.RemoveTeamMemberAsync(Arg.Any<long>(), Arg.Any<string>())
                .Returns(new ValueTask<OneOf<Success, ForgejoError>>(new Success()));
        _forgejo.SetCollaboratorAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                                      Arg.Any<CollaboratorPermission>())
                .Returns(new ValueTask<OneOf<Success, ForgejoError>>(new Success()));

        _assignmentRepo.GetReconciliationDataAsync(AssignmentId).Returns(_ => new ValueTask<Assignment?>(BuildAssignment()));

        _sut = new ReconciliationService(uow, _forgejo, clock, Substitute.For<ILogger<ReconciliationService>>());
    }

    private static Assignment BuildAssignment() => new()
    {
        Id = AssignmentId,
        CourseId = 5,
        Slug = "a1",
        Title = "A1",
        DeadlineKind = DeadlineKind.None,
        Course = new Course
        {
            Id = 5,
            Title = "Algo",
            ForgejoOrg = Org,
            Owner = new User { StudentId = TeacherIf, FirstName = "T", LastName = "T", Role = Role.Teacher }
        },
        Acceptances =
        [
            new Acceptance
            {
                RepoOwner = Org,
                RepoName = RepoName,
                Student = new User { StudentId = StudentIf, FirstName = "S", LastName = "S", Role = Role.Student }
            }
        ]
    };

    private void ArrangeActual(bool teacherIsMember, CollaboratorPermission studentActual)
    {
        OneOf<IReadOnlyCollection<string>, ForgejoError> members =
            (teacherIsMember ? new List<string> { TeacherIf } : []).AsReadOnly();
        _forgejo.ListTeamMembersAsync(TeamId)
                .Returns(new ValueTask<OneOf<IReadOnlyCollection<string>, ForgejoError>>(members));
        _forgejo.GetCollaboratorPermissionAsync(Org, RepoName, StudentIf)
                .Returns(new ValueTask<OneOf<CollaboratorPermission, NotFound, ForgejoError>>(studentActual));
    }

    [Fact]
    public async Task AlreadyConverged_PerformsNoWrites()
    {
        ArrangeActual(teacherIsMember: true, studentActual: CollaboratorPermission.Write);

        OneOf<Success, NotFound, ForgejoError> result = await _sut.ReconcileAsync(AssignmentId);

        result.ShouldBe<Success>();
        await _forgejo.DidNotReceive().AddTeamMemberAsync(Arg.Any<long>(), Arg.Any<string>());
        await _forgejo.DidNotReceive().SetCollaboratorAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                                                            Arg.Any<CollaboratorPermission>());
    }

    [Fact]
    public async Task DriftedCollaborator_IsRestoredToDesired()
    {
        ArrangeActual(teacherIsMember: true, studentActual: CollaboratorPermission.Read);

        await _sut.ReconcileAsync(AssignmentId);

        await _forgejo.Received(1).SetCollaboratorAsync(Org, RepoName, StudentIf, CollaboratorPermission.Write);
    }

    [Fact]
    public async Task MissingTeamMember_IsAdded()
    {
        ArrangeActual(teacherIsMember: false, studentActual: CollaboratorPermission.Write);

        await _sut.ReconcileAsync(AssignmentId);

        await _forgejo.Received(1).AddTeamMemberAsync(TeamId, TeacherIf);
    }

    [Fact]
    public async Task MissingAssignment_ReturnsNotFound()
    {
        _assignmentRepo.GetReconciliationDataAsync(99L).Returns(new ValueTask<Assignment?>((Assignment?)null));

        OneOf<Success, NotFound, ForgejoError> result = await _sut.ReconcileAsync(99L);

        result.ShouldBe<NotFound>();
    }
}
