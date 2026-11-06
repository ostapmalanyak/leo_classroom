using System.Net;
using LeoClassroom.Persistence.Model;
using LeoClassroom.Persistence.Repositories;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Services.Forgejo;
using LeoClassroom.Services.Util;
using LeoClassroom.Shared;
using Microsoft.Extensions.Options;
using NSubstitute;
using OneOf;
using OneOf.Types;

namespace LeoClassroom.Test;

public sealed class RepoProvisioningServiceTests
{
    private const string BotName = "leo-classroom-bot";
    private const string AdminToken = "admin-token";

    [Fact]
    public async Task SeedByMigrateAsync_OwnPrivateRepo_UsesInternalAuthenticatedCloneUrl()
    {
        var forgejo = Substitute.For<IForgejoClient>();
        ForgejoRepo source = CreateRepo("teacher/starter", "https://git.example.test/teacher/starter.git");
        ForgejoRepo copied = CreateRepo("course/copy", "http://forgejo:3000/course/copy.git");
        forgejo.GetRepoAsync("teacher", "starter")
               .Returns(new ValueTask<OneOf<ForgejoRepo, NotFound, ForgejoError>>(source));
        forgejo.MigrateRepoAsync(
                  Arg.Any<string>(), "course", "copy", true, BotName, AdminToken)
               .Returns(new ValueTask<OneOf<Success<ForgejoRepo>, ForgejoError>>(new Success<ForgejoRepo>(copied)));

        RepoProvisioningService service = CreateService(forgejo);
        Assignment assignment = CreateCopyAssignment("https://git.example.test/teacher/starter.git");

        var result = await service.SeedByMigrateAsync(assignment, "course", "copy");

        result.ShouldBe<ForgejoRepo>().Should().Be(copied);
        await forgejo.Received(1).MigrateRepoAsync(
            "http://forgejo:3000/teacher/starter.git", "course", "copy", true, BotName, AdminToken);
    }

    [Fact]
    public async Task SeedByMigrateAsync_ExternalRepo_DoesNotSendForgejoCredentials()
    {
        const string sourceUrl = "https://example.test/teacher/starter.git";
        var forgejo = Substitute.For<IForgejoClient>();
        ForgejoRepo copied = CreateRepo("course/copy", "http://forgejo:3000/course/copy.git");
        forgejo.GetRepoAsync("teacher", "starter")
               .Returns(new ValueTask<OneOf<ForgejoRepo, NotFound, ForgejoError>>(new NotFound()));
        forgejo.MigrateRepoAsync(Arg.Any<string>(), "course", "copy", true, null, null)
               .Returns(new ValueTask<OneOf<Success<ForgejoRepo>, ForgejoError>>(new Success<ForgejoRepo>(copied)));

        RepoProvisioningService service = CreateService(forgejo);
        Assignment assignment = CreateCopyAssignment(sourceUrl);

        var result = await service.SeedByMigrateAsync(assignment, "course", "copy");

        result.ShouldBe<ForgejoRepo>().Should().Be(copied);
        await forgejo.Received(1).MigrateRepoAsync(sourceUrl, "course", "copy", true, null, null);
    }

    [Fact]
    public async Task ProvisionAcceptanceAsync_FailedCourseSetup_StoresFailureReason()
    {
        const long acceptanceId = 12;
        const long assignmentId = 34;
        const long courseId = 56;
        var uow = Substitute.For<IUnitOfWork>();
        var acceptances = Substitute.For<IAcceptanceRepository>();
        var assignments = Substitute.For<IAssignmentRepository>();
        var courses = Substitute.For<ICourseRepository>();
        var acceptance = new Acceptance
        {
            Id = acceptanceId,
            AssignmentId = assignmentId,
            StudentId = 78,
            RepoOwner = "course",
            RepoName = "copy-IF000078",
            AcceptedAt = SystemClock.Instance.GetCurrentInstant(),
            Status = SubmissionStatus.Provisioning
        };
        Assignment assignment = CreateCopyAssignment("https://example.test/teacher/starter.git");
        assignment.CourseId = courseId;
        uow.AcceptanceRepository.Returns(acceptances);
        uow.AssignmentRepository.Returns(assignments);
        uow.CourseRepository.Returns(courses);
        acceptances.GetTrackedByIdAsync(acceptanceId)
                   .Returns(new ValueTask<Acceptance?>(acceptance));
        assignments.GetByIdAsync(assignmentId).Returns(new ValueTask<Assignment?>(assignment));
        courses.GetTrackedByIdAsync(courseId).Returns(new ValueTask<Course?>(default(Course?)));

        RepoProvisioningService service = CreateService(Substitute.For<IForgejoClient>(), uow);

        var result = await service.ProvisionAcceptanceAsync(acceptanceId);

        result.ShouldBe<ProvisioningError>().Reason.Should().Be("course not found");
        acceptance.Status.Should().Be(SubmissionStatus.Failed);
        acceptance.FailureReason.Should().Be("course not found");
        await uow.Received(1).SaveChangesAsync();
    }

    [Fact]
    public async Task ProvisionAcceptanceAsync_ForkConflict_UsesExistingForkAndCompletesProvisioning()
    {
        const long acceptanceId = 12;
        const long assignmentId = 34;
        const long courseId = 56;
        const string org = "course-org";
        const string repoName = "student-assignment";
        var uow = Substitute.For<IUnitOfWork>();
        var acceptances = Substitute.For<IAcceptanceRepository>();
        var assignments = Substitute.For<IAssignmentRepository>();
        var courses = Substitute.For<ICourseRepository>();
        var acceptance = new Acceptance
        {
            Id = acceptanceId,
            AssignmentId = assignmentId,
            StudentId = 78,
            RepoOwner = org,
            RepoName = repoName,
            AcceptedAt = SystemClock.Instance.GetCurrentInstant(),
            Status = SubmissionStatus.Provisioning
        };
        Assignment assignment = new()
        {
            Title = "Fork exercise",
            Slug = "fork-exercise",
            CourseId = courseId,
            StarterSourceKind = StarterSourceKind.ForkOwnRepo,
            StarterRepoUrl = "https://git.example.test/teacher/starter"
        };
        Course course = new()
        {
            Id = courseId,
            Title = "Course",
            Owner = new User { StudentId = "teacher", FirstName = "Teach", LastName = "Er" },
            ForgejoOrg = org
        };
        ForgejoRepo existingFork = CreateRepo(
            $"{org}/{repoName}", "http://forgejo:3000/course-org/student-assignment.git");
        var forgejo = Substitute.For<IForgejoClient>();
        var reconciliation = Substitute.For<IReconciliationService>();
        uow.AcceptanceRepository.Returns(acceptances);
        uow.AssignmentRepository.Returns(assignments);
        uow.CourseRepository.Returns(courses);
        acceptances.GetTrackedByIdAsync(acceptanceId)
                   .Returns(new ValueTask<Acceptance?>(acceptance));
        assignments.GetByIdAsync(assignmentId).Returns(new ValueTask<Assignment?>(assignment));
        courses.GetTrackedByIdAsync(courseId).Returns(new ValueTask<Course?>(course));
        forgejo.EnsureOrgAsync(org).Returns(new ValueTask<OneOf<Success, ForgejoError>>(new Success()));
        forgejo.EnsureTeamAsync(org, ForgejoNaming.TeachersTeamName, CollaboratorPermission.Admin)
               .Returns(new ValueTask<OneOf<Success<long>, ForgejoError>>(new Success<long>(42)));
        forgejo.IsTeamMemberAsync(42, "teacher").Returns(new ValueTask<OneOf<bool, ForgejoError>>(true));
        forgejo.GetRepoAsync(org, repoName)
               .Returns(
                   new ValueTask<OneOf<ForgejoRepo, NotFound, ForgejoError>>(new NotFound()),
                   new ValueTask<OneOf<ForgejoRepo, NotFound, ForgejoError>>(existingFork));
        forgejo.ForkRepoAsync("teacher", "starter", org, repoName)
               .Returns(new ValueTask<OneOf<Success<ForgejoRepo>, ForgejoError>>(
                   new ForgejoError((int) HttpStatusCode.Conflict, "already forked")));
        forgejo.CreateBranchAsync(org, repoName, ForgejoNaming.FeedbackBranchName, "main")
               .Returns(new ValueTask<OneOf<Success, ForgejoError>>(new Success()));
        reconciliation.ReconcileAsync(assignmentId)
                      .Returns(new ValueTask<OneOf<Success, NotFound, ForgejoError>>(new Success()));

        RepoProvisioningService service = CreateService(forgejo, uow, reconciliation);

        var result = await service.ProvisionAcceptanceAsync(acceptanceId);

        result.ShouldBe<Success>();
        acceptance.Status.Should().Be(SubmissionStatus.Ready);
        acceptance.RepoUrl.Should().Be(existingFork.CloneUrl);
        await forgejo.Received(2).GetRepoAsync(org, repoName);
        await forgejo.Received(1).CreateBranchAsync(
            org, repoName, ForgejoNaming.FeedbackBranchName, "main");
        await reconciliation.Received(1).ReconcileAsync(assignmentId);
    }

    private static RepoProvisioningService CreateService(
        IForgejoClient forgejo, IUnitOfWork? uow = null, IReconciliationService? reconciliation = null) =>
        new(
            uow ?? Substitute.For<IUnitOfWork>(),
            forgejo,
            Substitute.For<IGitService>(),
            Substitute.For<IArchiveExtractor>(),
            reconciliation ?? Substitute.For<IReconciliationService>(),
            Options.Create(new ForgejoSettings
            {
                BaseUrl = "http://forgejo:3000",
                AdminToken = AdminToken,
                WebhookSecret = "secret",
                WebhookTargetUrl = "http://backend/webhook"
            }),
            Substitute.For<ILogger<RepoProvisioningService>>());

    private static Assignment CreateCopyAssignment(string sourceUrl) => new()
    {
        Title = "Copy exercise",
        Slug = "copy-exercise",
        StarterSourceKind = StarterSourceKind.CopyRepo,
        StarterRepoUrl = sourceUrl
    };

    private static ForgejoRepo CreateRepo(string fullName, string cloneUrl) =>
        new(1, fullName.Split('/')[1], fullName, cloneUrl, cloneUrl, "main");
}
