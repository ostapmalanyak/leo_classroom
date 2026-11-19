using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text;
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
    public async Task PostArchivePreview_AsOwner_ReturnsPromotedFileList()
    {
        AuthenticateAsTeacher(OwnerIf);
        using var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
        {
            using Stream entry = archive.CreateEntry("starter/src/main.cs").Open();
            await entry.WriteAsync(Encoding.UTF8.GetBytes("class Main {}"), TestCancellationToken);
        }

        using var request = new MultipartFormDataContent();
        using var file = new ByteArrayContent(zip.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        request.Add(file, "archive", "starter.zip");

        var response = await ApiClient.PostAsync("api/assignments/archive-preview", request, TestCancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        StarterArchivePreviewDto preview =
            (await response.Content.ReadFromJsonAsync<StarterArchivePreviewDto>(JsonOptions, TestCancellationToken))!;
        preview.UploadId.Should().MatchRegex("^[0-9a-f]{32}$");
        preview.Entries.Should().Contain("src/main.cs");
    }

    [Fact]
    public async Task PostArchiveAssignment_WithoutConfirmedUpload_Returns400()
    {
        AuthenticateAsTeacher(OwnerIf);
        var request = new AssignmentCreateRequest(_courseId, "Archive task", null, null, null, DeadlineKind.None,
            false, StarterSourceKind.Archive, null, null, false, null, DownloadSnapshotMode.Deadline);

        var response = await ApiClient.PostAsJsonAsync("api/assignments", request, JsonOptions,
                                                       TestCancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problemDetails =
            await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(JsonOptions, TestCancellationToken);
        problemDetails!.Errors.Should().ContainKey("starterArchiveUploadId");
    }

    [Fact]
    public async Task PostArchiveAssignment_CreatesDownloadOnlyAssignmentWithoutStarterRepository()
    {
        AuthenticateAsTeacher(OwnerIf);
        using var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
        {
            using Stream entry = archive.CreateEntry("starter/readme.md").Open();
            await entry.WriteAsync(Encoding.UTF8.GetBytes("# Starter"), TestCancellationToken);
        }

        using var previewRequest = new MultipartFormDataContent();
        using var file = new ByteArrayContent(zip.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        previewRequest.Add(file, "archive", "starter.zip");
        var previewResponse = await ApiClient.PostAsync(
            "api/assignments/archive-preview", previewRequest, TestCancellationToken);
        previewResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        StarterArchivePreviewDto preview =
            (await previewResponse.Content.ReadFromJsonAsync<StarterArchivePreviewDto>(
                JsonOptions, TestCancellationToken))!;

        var request = new AssignmentCreateRequest(
            _courseId, "Download-only task", null, null, null, DeadlineKind.None, false,
            StarterSourceKind.Archive, null, null, false, null, DownloadSnapshotMode.Deadline,
            StarterArchiveUploadId: preview.UploadId);
        var response = await ApiClient.PostAsJsonAsync(
            "api/assignments", request, JsonOptions, TestCancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        AssignmentDto created =
            (await response.Content.ReadFromJsonAsync<AssignmentDto>(JsonOptions, TestCancellationToken))!;
        created.HasStarterArchive.Should().BeTrue();
        created.StarterTemplateRepoUrl.Should().BeNull();
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

    [Theory]
    [InlineData(StarterSourceKind.ForkOwnRepo)]
    [InlineData(StarterSourceKind.CopyRepo)]
    public void PostAssignment_RepoSourcesAcceptZipInsteadOfRepositoryUrl(StarterSourceKind starterSourceKind)
    {
        var request = new AssignmentCreateRequest(
            _courseId, "ZIP starter", null, null, null, DeadlineKind.None, false,
            starterSourceKind, null, null, false, null, DownloadSnapshotMode.Deadline,
            StarterArchiveUploadId: Guid.NewGuid().ToString("N"));

        new AssignmentCreateRequest.Validator(SystemClock.Instance).Validate(request).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task PostAssignment_ForkWithHardDeadline_Returns400()
    {
        AuthenticateAsTeacher(OwnerIf);

        // the student owns a fork, so revoking their write access at a hard deadline is impossible
        var bad = new AssignmentCreateRequest(_courseId, "X", null, null, Instant.FromUtc(2030, 1, 1, 0, 0),
            DeadlineKind.Hard, false, StarterSourceKind.ForkOwnRepo, "https://git.example/teacher/starter", null,
            false, null, DownloadSnapshotMode.Deadline);

        var response = await ApiClient.PostAsJsonAsync("api/assignments", bad, JsonOptions, TestCancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problemDetails = await response.Content
                                           .ReadFromJsonAsync<HttpValidationProblemDetails>(
                                               JsonOptions, TestCancellationToken);

        problemDetails.Should().NotBeNull();
        problemDetails.Errors.Should().ContainKey("deadlineKind");
    }

    [Fact]
    public async Task PutAssignment_SwitchToForkWithHardDeadline_Returns400()
    {
        AssignmentDto created = await CreateAsOwnerAsync();
        var update = new AssignmentUpdateRequest("X", null, null, Instant.FromUtc(2030, 1, 1, 0, 0),
            DeadlineKind.Hard, false, StarterSourceKind.ForkOwnRepo, "https://git.example/teacher/starter", null,
            false, null, DownloadSnapshotMode.Deadline);

        var response = await ApiClient.PutAsJsonAsync($"api/assignments/{created.Id}", update, JsonOptions,
                                                      TestCancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
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
