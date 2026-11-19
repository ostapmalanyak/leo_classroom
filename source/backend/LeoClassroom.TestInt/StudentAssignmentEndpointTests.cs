using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LeoClassroom.Endpoints;
using LeoClassroom.Services;
using LeoClassroom.Persistence.Model;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Shared;
using LeoClassroom.TestInt.Util;
using Microsoft.EntityFrameworkCore;

namespace LeoClassroom.TestInt;

public sealed class StudentAssignmentEndpointTests(WebApiTestFixture fixture) : WebApiTestBase(fixture)
{
    private const string OwnerIf = "IF000001";
    private const string MemberIf = "IF000050";
    private const string OutsiderIf = "IF000077";

    private long _assignmentId;

    protected override async ValueTask ImportSeedDataAsync(DatabaseContext context)
    {
        var owner = new User { StudentId = OwnerIf, FirstName = "Olive", LastName = "Wren", Role = Role.Teacher };
        var member = new User { StudentId = MemberIf, FirstName = "Mia", LastName = "Lee", Role = Role.Student };
        var outsider = new User { StudentId = OutsiderIf, FirstName = "Otto", LastName = "Kemp", Role = Role.Student };
        var roster = new Roster { Name = "5AHIF", Kind = RosterKind.Auto, ClassKey = "5AHIF", Members = [member] };
        context.Users.AddRange(owner, member, outsider);
        context.Rosters.Add(roster);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var course = new Course
        {
            Title = "Algorithms", RosterId = roster.Id, OwnerId = owner.Id, StudentsRetainAccess = true,
            CreatedAt = TestClock.GetCurrentInstant(), Members = [member]
        };
        context.Courses.Add(course);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var assignment = new Assignment
        {
            CourseId = course.Id, Title = "Sorting", Slug = "sorting", OwnerId = owner.Id,
            DeadlineKind = DeadlineKind.None, StarterSourceKind = StarterSourceKind.DescriptionOnly,
            DownloadSnapshotMode = DownloadSnapshotMode.Deadline, HintsInstructions = "Read this first"
        };
        context.Assignments.Add(assignment);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        _assignmentId = assignment.Id;
    }

    [Fact]
    public async Task Mine_ForMember_ListsUnacceptedAssignment()
    {
        AuthenticateAsStudent(MemberIf);

        var list = await ApiClient.GetFromJsonAsync<StudentAssignmentList>("api/assignments/mine", JsonOptions,
                                                                          TestCancellationToken);

        list!.Unaccepted.Should().ContainSingle(a => a.Id == _assignmentId);
    }

    [Fact]
    public async Task Info_ForMember_ReturnsHintsWithoutRepo()
    {
        AuthenticateAsStudent(MemberIf);

        var response = await ApiClient.GetAsync($"api/assignments/{_assignmentId}/info", TestCancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HiddenAssignment_IsInvisibleToStudents_UntilPublished()
    {
        await ModifyDatabaseContentAsync(async context =>
        {
            await context.Assignments.Where(a => a.Id == _assignmentId)
                         .ExecuteUpdateAsync(set => set.SetProperty(a => a.IsPublished, false),
                                             TestCancellationToken);
        });
        AuthenticateAsStudent(MemberIf);

        var list = await ApiClient.GetFromJsonAsync<StudentAssignmentList>("api/assignments/mine", JsonOptions,
                                                                          TestCancellationToken);
        var info = await ApiClient.GetAsync($"api/assignments/{_assignmentId}/info", TestCancellationToken);
        var accept = await ApiClient.PostAsync($"api/assignments/{_assignmentId}/accept", null, TestCancellationToken);

        list!.Unaccepted.Should().NotContain(a => a.Id == _assignmentId);
        info.StatusCode.Should().Be(HttpStatusCode.NotFound);
        accept.StatusCode.Should().Be(HttpStatusCode.NotFound);

        AuthenticateAsTeacher(OwnerIf);
        var publish = await ApiClient.PostAsync($"api/assignments/{_assignmentId}/publish", null, TestCancellationToken);
        publish.StatusCode.Should().Be(HttpStatusCode.NoContent);

        AuthenticateAsStudent(MemberIf);
        var after = await ApiClient.GetAsync($"api/assignments/{_assignmentId}/info", TestCancellationToken);
        after.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Info_ForNonMember_Returns403()
    {
        AuthenticateAsStudent(OutsiderIf);

        var response = await ApiClient.GetAsync($"api/assignments/{_assignmentId}/info", TestCancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task StarterArchive_IsAvailableToMemberAndForbiddenToOutsider()
    {
        string archiveDirectory = Path.Combine(StarterArchiveStoragePath, "assignments");
        Directory.CreateDirectory(archiveDirectory);
        string archivePath = Path.Combine(archiveDirectory, "starter.zip");
        byte[] archive = [0x50, 0x4b, 0x03, 0x04, 0x01];
        await File.WriteAllBytesAsync(archivePath, archive, TestCancellationToken);
        await ModifyDatabaseContentAsync(async context =>
        {
            Assignment assignment = await context.Assignments.SingleAsync(
                row => row.Id == _assignmentId, TestCancellationToken);
            assignment.StarterSourceKind = StarterSourceKind.Archive;
            assignment.ArchivePath = archivePath;
            await context.SaveChangesAsync(TestCancellationToken);
        });

        AuthenticateAsStudent(MemberIf);
        var download = await ApiClient.GetAsync(
            $"api/assignments/{_assignmentId}/starter-archive", TestCancellationToken);

        download.StatusCode.Should().Be(HttpStatusCode.OK);
        download.Content.Headers.ContentType?.MediaType.Should().Be("application/zip");
        (await download.Content.ReadAsByteArrayAsync(TestCancellationToken)).Should().Equal(archive);

        var info = await ApiClient.GetFromJsonAsync<AssignmentInfoDto>(
            $"api/assignments/{_assignmentId}/info", JsonOptions, TestCancellationToken);
        info!.DownloadOnly.Should().BeTrue();
        var accept = await ApiClient.PostAsync(
            $"api/assignments/{_assignmentId}/accept", null, TestCancellationToken);
        accept.StatusCode.Should().Be(HttpStatusCode.Accepted);
        using (JsonDocument body = JsonDocument.Parse(
                   await accept.Content.ReadAsStringAsync(TestCancellationToken)))
        {
            body.RootElement.GetProperty("status").GetString().Should().Be(nameof(SubmissionStatus.DownloadOnly));
            body.RootElement.GetProperty("repoUrl").ValueKind.Should().Be(JsonValueKind.Null);
        }

        var duplicateAccept = await ApiClient.PostAsync(
            $"api/assignments/{_assignmentId}/accept", null, TestCancellationToken);
        duplicateAccept.StatusCode.Should().Be(HttpStatusCode.Conflict);

        await ModifyDatabaseContentAsync(async context =>
        {
            Acceptance acceptance = await context.Acceptances.SingleAsync(
                row => row.AssignmentId == _assignmentId, TestCancellationToken);
            acceptance.Status.Should().Be(SubmissionStatus.DownloadOnly);
            acceptance.RepoUrl.Should().BeNull();
        });

        AuthenticateAsStudent(OutsiderIf);
        var forbidden = await ApiClient.GetAsync(
            $"api/assignments/{_assignmentId}/starter-archive", TestCancellationToken);
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Accept_ByMember_Returns202ThenConflictOnDoubleAccept()
    {
        AuthenticateAsStudent(MemberIf);

        var first = await ApiClient.PostAsync($"api/assignments/{_assignmentId}/accept", null, TestCancellationToken);
        first.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var second = await ApiClient.PostAsync($"api/assignments/{_assignmentId}/accept", null, TestCancellationToken);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Accept_ByNonMember_Returns403()
    {
        AuthenticateAsStudent(OutsiderIf);

        var response = await ApiClient.PostAsync($"api/assignments/{_assignmentId}/accept", null, TestCancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Submission_FailedProvisioning_IncludesFailureReason()
    {
        await ModifyDatabaseContentAsync(async context =>
        {
            User member = await context.Users.SingleAsync(user => user.StudentId == MemberIf);
            context.Acceptances.Add(new Acceptance
            {
                AssignmentId = _assignmentId,
                StudentId = member.Id,
                RepoOwner = "algorithms-1",
                RepoName = "sorting-IF000050",
                AcceptedAt = TestClock.GetCurrentInstant(),
                Status = SubmissionStatus.Failed,
                FailureReason = "migrate repo: token rejected"
            });
            await context.SaveChangesAsync(TestCancellationToken);
        });
        AuthenticateAsStudent(MemberIf);

        var response = await ApiClient.GetAsync(
            $"api/assignments/{_assignmentId}/submission", TestCancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestCancellationToken));
        body.RootElement.GetProperty("failureReason").GetString().Should().Be("migrate repo: token rejected");
    }
}
