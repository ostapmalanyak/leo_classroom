using System.Net;
using System.Net.Http.Json;
using LeoClassroom.Endpoints;
using LeoClassroom.Services.Download;
using LeoClassroom.Persistence.Model;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Shared;
using LeoClassroom.TestInt.Util;
using Microsoft.EntityFrameworkCore;

namespace LeoClassroom.TestInt;

public sealed class DownloadEndpointTests(WebApiTestFixture fixture) : WebApiTestBase(fixture)
{
    private const string OwnerIf = "IF000001";
    private const string OtherTeacherIf = "IF000002";

    private long _assignmentId;

    protected override async ValueTask ImportSeedDataAsync(DatabaseContext context)
    {
        var owner = new User { StudentId = OwnerIf, FirstName = "Olive", LastName = "Wren", Role = Role.Teacher, State = UserState.Active };
        var other = new User { StudentId = OtherTeacherIf, FirstName = "Carl", LastName = "Tan", Role = Role.Teacher, State = UserState.Active };
        var roster = new Roster { Name = "5AHIF", Kind = RosterKind.Auto, ClassKey = "5AHIF" };
        context.Users.AddRange(owner, other);
        context.Rosters.Add(roster);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var course = new Course
        {
            Title = "Algorithms", RosterId = roster.Id, OwnerId = owner.Id, StudentsRetainAccess = true,
            CreatedAt = TestClock.GetCurrentInstant()
        };
        context.Courses.Add(course);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var assignment = new Assignment
        {
            CourseId = course.Id, Title = "A1", Slug = "a1", OwnerId = owner.Id,
            DownloadSnapshotMode = DownloadSnapshotMode.Deadline
        };
        context.Assignments.Add(assignment);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        _assignmentId = assignment.Id;
    }

    [Fact]
    public async Task Trigger_AsOwner_CreatesPendingJobAndReturns202()
    {
        AuthenticateAsTeacher(OwnerIf);

        var response = await ApiClient.PostAsJsonAsync(
            $"api/assignments/{_assignmentId}/download", new DownloadTriggerRequest(DownloadSnapshotMode.Head),
            JsonOptions, TestCancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var accepted = await response.Content.ReadFromJsonAsync<DownloadJobAcceptedDto>(JsonOptions, TestCancellationToken);
        accepted!.Id.Should().BeGreaterThan(0);

        DownloadJob? job = null;
        await ModifyDatabaseContentAsync(async ctx =>
            job = await ctx.DownloadJobs.AsNoTracking()
                           .FirstOrDefaultAsync(j => j.Id == accepted.Id, TestCancellationToken));

        job.Should().NotBeNull();
        job!.Status.Should().Be(DownloadJobStatus.Pending);
        job.Mode.Should().Be(DownloadSnapshotMode.Head);
        job.RequestedByStudentId.Should().Be(OwnerIf);
    }

    [Fact]
    public async Task Trigger_AsNonOwnerTeacher_IsForbidden()
    {
        AuthenticateAsTeacher(OtherTeacherIf);

        var response = await ApiClient.PostAsJsonAsync(
            $"api/assignments/{_assignmentId}/download", new DownloadTriggerRequest(null),
            JsonOptions, TestCancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Status_AfterTrigger_ReflectsPendingJob()
    {
        AuthenticateAsTeacher(OwnerIf);
        var trigger = await ApiClient.PostAsJsonAsync(
            $"api/assignments/{_assignmentId}/download", new DownloadTriggerRequest(null),
            JsonOptions, TestCancellationToken);
        var accepted = await trigger.Content.ReadFromJsonAsync<DownloadJobAcceptedDto>(JsonOptions, TestCancellationToken);

        var status = await ApiClient.GetFromJsonAsync<DownloadJobView>(
            $"api/assignments/{_assignmentId}/download/{accepted!.Id}", JsonOptions, TestCancellationToken);

        status!.Id.Should().Be(accepted.Id);
        status.AssignmentId.Should().Be(_assignmentId);
        status.Mode.Should().Be(DownloadSnapshotMode.Deadline);
    }

    [Fact]
    public async Task Artifact_WhilePending_ReturnsNotFound()
    {
        AuthenticateAsTeacher(OwnerIf);
        var trigger = await ApiClient.PostAsJsonAsync(
            $"api/assignments/{_assignmentId}/download", new DownloadTriggerRequest(null),
            JsonOptions, TestCancellationToken);
        var accepted = await trigger.Content.ReadFromJsonAsync<DownloadJobAcceptedDto>(JsonOptions, TestCancellationToken);

        var response = await ApiClient.GetAsync(
            $"api/assignments/{_assignmentId}/download/{accepted!.Id}/artifact", TestCancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
