using System.Net;
using System.Net.Http.Json;
using LeoClassroom.Endpoints;
using LeoClassroom.Persistence.Model;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Shared;
using LeoClassroom.TestInt.Util;

namespace LeoClassroom.TestInt;

public sealed class AssignmentEndpointTests(WebApiTestFixture fixture) : WebApiTestBase(fixture)
{
    private const string OwnerIf = "IF000001";
    private const string OtherTeacherIf = "IF000002";
    private const string StudentIf = "IF000050";

    private long _courseId;

    protected override async ValueTask ImportSeedDataAsync(DatabaseContext context)
    {
        var owner = new User { StudentId = OwnerIf, FirstName = "Olive", LastName = "Wren", Role = Role.Teacher };
        var roster = new Roster { Name = "5AHIF", Kind = RosterKind.Auto, ClassKey = "5AHIF" };
        context.Users.Add(owner);
        context.Rosters.Add(roster);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var course = new Course
        {
            Title = "Algorithms", RosterId = roster.Id, OwnerId = owner.Id, StudentsRetainAccess = true,
            CreatedAt = TestClock.GetCurrentInstant()
        };
        context.Courses.Add(course);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        _courseId = course.Id;
    }

    private AssignmentCreateRequest ValidCreate(string title = "Sorting") => new(
        _courseId, title, "desc", "hints", null, DeadlineKind.None, false,
        StarterSourceKind.DescriptionOnly, null, null, false, null, DownloadSnapshotMode.Deadline);

    private async Task<AssignmentDto> CreateAsOwnerAsync(string title = "Sorting")
    {
        AuthenticateAsTeacher(OwnerIf);
        var response = await ApiClient.PostAsJsonAsync("api/assignments", ValidCreate(title), JsonOptions,
                                                       TestCancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return (await response.Content.ReadFromJsonAsync<AssignmentDto>(JsonOptions, TestCancellationToken))!;
    }

    [Fact]
    public async Task PostAssignment_Student_Returns403()
    {
        AuthenticateAsStudent(StudentIf);

        var response = await ApiClient.PostAsJsonAsync("api/assignments", ValidCreate(), JsonOptions,
                                                       TestCancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PostAssignment_CourseOwner_CreatesWithOwner()
    {
        AssignmentDto created = await CreateAsOwnerAsync();

        created.Title.Should().Be("Sorting");
        created.Slug.Should().Be("sorting");
    }

    [Fact]
    public async Task PostAssignment_NonCourseTeacher_Returns403()
    {
        AuthenticateAsTeacher(OtherTeacherIf);

        var response = await ApiClient.PostAsJsonAsync("api/assignments", ValidCreate(), JsonOptions,
                                                       TestCancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PutAssignment_ByNonOwnerTeacher_Returns403()
    {
        AssignmentDto created = await CreateAsOwnerAsync();
        AuthenticateAsTeacher(OtherTeacherIf);
        var update = new AssignmentUpdateRequest("Renamed", null, null, null, DeadlineKind.None, false,
            StarterSourceKind.DescriptionOnly, null, null, false, null, DownloadSnapshotMode.Deadline);

        var response = await ApiClient.PutAsJsonAsync($"api/assignments/{created.Id}", update, JsonOptions,
                                                      TestCancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetStudents_ByOwner_ReturnsEmptyList()
    {
        AssignmentDto created = await CreateAsOwnerAsync();

        var students = await ApiClient.GetFromJsonAsync<StudentSubmission[]>(
            $"api/assignments/{created.Id}/students", JsonOptions, TestCancellationToken);

        students.Should().BeEmpty();
    }

    [Fact]
    public async Task PostAssignment_InvalidDeadline_Returns400()
    {
        AuthenticateAsTeacher(OwnerIf);
        var bad = new AssignmentCreateRequest(_courseId, "X", null, null, null, DeadlineKind.Hard, false,
            StarterSourceKind.DescriptionOnly, null, null, false, null, DownloadSnapshotMode.Deadline);

        var response = await ApiClient.PostAsJsonAsync("api/assignments", bad, JsonOptions, TestCancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var problemDetails = await response.Content
                                           .ReadFromJsonAsync<HttpValidationProblemDetails>(
                                               JsonOptions, TestCancellationToken);

        problemDetails.Should().NotBeNull();
        problemDetails.Errors.Should().ContainKey("deadline");
    }

    [Theory]
    [InlineData(StarterSourceKind.ForkOwnRepo)]
    [InlineData(StarterSourceKind.CopyRepo)]
    public async Task PostAssignment_NonHttpStarterUrl_Returns400(StarterSourceKind starterSourceKind)
    {
        AuthenticateAsTeacher(OwnerIf);

        // the starter URL is cloned server-side, so a file:// source must never reach the provisioning worker
        var bad = new AssignmentCreateRequest(_courseId, "X", null, null, null, DeadlineKind.None, false,
            starterSourceKind, "file:///etc/passwd", null, false, null, DownloadSnapshotMode.Deadline);

        var response = await ApiClient.PostAsJsonAsync("api/assignments", bad, JsonOptions, TestCancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problemDetails = await response.Content
                                           .ReadFromJsonAsync<HttpValidationProblemDetails>(
                                               JsonOptions, TestCancellationToken);

        problemDetails.Should().NotBeNull();
        problemDetails.Errors.Should().ContainKey("starterRepoUrl");
    }

    [Fact]
    public async Task AcceptLink_AsOwner_ReturnsStudentLink()
    {
        AssignmentDto created = await CreateAsOwnerAsync("Trees");

        var link = await ApiClient.GetFromJsonAsync<AcceptLinkDto>(
            $"api/assignments/{created.Id}/accept-link", JsonOptions, TestCancellationToken);

        link!.AssignmentId.Should().Be(created.Id);
        link.AcceptLink.Should().EndWith($"/my-assignments/{created.Id}");
    }

    [Fact]
    public async Task AcceptLink_AsNonCourseTeacher_Returns403()
    {
        AssignmentDto created = await CreateAsOwnerAsync("Graphs");
        AuthenticateAsTeacher(OtherTeacherIf);

        var response = await ApiClient.GetAsync(
            $"api/assignments/{created.Id}/accept-link", TestCancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
