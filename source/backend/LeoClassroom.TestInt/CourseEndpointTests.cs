using System.Net;
using System.Net.Http.Json;
using LeoClassroom.Endpoints;
using LeoClassroom.Persistence.Model;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Shared;
using LeoClassroom.TestInt.Util;

namespace LeoClassroom.TestInt;

public sealed class CourseEndpointTests(WebApiTestFixture fixture) : WebApiTestBase(fixture)
{
    private const string OwnerIf = "IF000001";
    private const string OtherTeacherIf = "IF000002";
    private const string AdminIf = "IF000003";
    private const string StudentIf = "IF000050";

    private long _rosterId;
    private long _secondRosterId;

    protected override async ValueTask ImportSeedDataAsync(DatabaseContext context)
    {
        var roster = new Roster { Name = "5AHIF" };
        var secondRoster = new Roster { Name = "6AHIF" };
        var owner = new User
        {
            StudentId = OwnerIf,
            FirstName = "Course",
            LastName = "Owner",
            Role = Role.Teacher
        };
        var student = new User
        {
            StudentId = StudentIf,
            FirstName = "Roster",
            LastName = "Student",
            Role = Role.Student,
            Class = "5AHIF"
        };
        roster.Members.Add(student);
        context.Rosters.AddRange(roster, secondRoster);
        context.Users.AddRange(owner, student);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        _rosterId = roster.Id;
        _secondRosterId = secondRoster.Id;
    }

    private CourseCreateRequest ValidCreateRequest(string title = "Algorithms") => new(title, _rosterId);

    private async Task<CourseDto> CreateCourseAsOwnerAsync(string title = "Algorithms")
    {
        AuthenticateAsTeacher(OwnerIf);
        var response = await ApiClient.PostAsJsonAsync("api/courses", ValidCreateRequest(title), JsonOptions,
                                                       TestCancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return (await response.Content.ReadFromJsonAsync<CourseDto>(JsonOptions, TestCancellationToken))!;
    }

    [Fact]
    public async Task PostCourse_NoToken_Returns401()
    {
        Anonymous();

        var response = await ApiClient.PostAsJsonAsync("api/courses", ValidCreateRequest(), JsonOptions,
                                                       TestCancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostCourse_Student_Returns403()
    {
        AuthenticateAsStudent(StudentIf);

        var response = await ApiClient.PostAsJsonAsync("api/courses", ValidCreateRequest(), JsonOptions,
                                                       TestCancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PostCourse_Teacher_Returns201WithActingOwner()
    {
        var created = await CreateCourseAsOwnerAsync();

        created.Title.Should().Be("Algorithms");
        created.RosterId.Should().Be(_rosterId);
        created.IconSeed.Should().NotBeNull();
        created.IconSeed!.Should().MatchRegex("^[A-Za-z0-9-]{8,64}$");
    }

    [Fact]
    public async Task PostCourse_ClassDialogPayload_CreatesCourse()
    {
        AuthenticateAsTeacher(OwnerIf);

        var response = await ApiClient.PostAsJsonAsync("api/courses",
            new { name = "Chemistry", rosterIds = new[] { _rosterId.ToString() }, iconSeed = "chemistry-seed-01" },
            JsonOptions, TestCancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<CourseDto>(JsonOptions, TestCancellationToken);
        created!.Title.Should().Be("Chemistry");
        created.RosterId.Should().Be(_rosterId);
        created.IconSeed.Should().Be("chemistry-seed-01");
    }

    [Fact]
    public async Task PostCourse_ClassDialogPayload_RejectsBlankName()
    {
        AuthenticateAsTeacher(OwnerIf);

        var response = await ApiClient.PostAsJsonAsync("api/courses",
            new { name = "   ", rosterIds = new[] { _rosterId.ToString() } },
            JsonOptions, TestCancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problemDetails = await response.Content
                                           .ReadFromJsonAsync<HttpValidationProblemDetails>(
                                               JsonOptions, TestCancellationToken);
        problemDetails!.Errors.Should().ContainKey("name");
    }

    [Fact]
    public async Task GetCourseAvatar_IsAnonymousCachedAndDeterministicForSeed()
    {
        Anonymous();

        const string seed = "seed-chemistry-01";
        var response = await ApiClient.GetAsync($"api/courses/avatar?seed={seed}", TestCancellationToken);
        var sameSeedResponse = await ApiClient.GetAsync($"api/courses/avatar?seed={seed}", TestCancellationToken);
        var otherSeedResponse = await ApiClient.GetAsync("api/courses/avatar?seed=seed-physics-01", TestCancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("image/svg+xml");
        response.Headers.CacheControl?.Public.Should().BeTrue();
        response.Headers.CacheControl?.MaxAge.Should().Be(TimeSpan.FromDays(365));
        string svg = await response.Content.ReadAsStringAsync(TestCancellationToken);
        string sameSeedSvg = await sameSeedResponse.Content.ReadAsStringAsync(TestCancellationToken);
        string otherSeedSvg = await otherSeedResponse.Content.ReadAsStringAsync(TestCancellationToken);
        svg.Should().Be(sameSeedSvg);
        svg.Should().NotBe(otherSeedSvg);
        svg.Should().Contain("<rect x=");
        svg.Should().NotContain("<text");
    }

    [Fact]
    public async Task GetCourseAvatar_SameCourseNameWithDifferentSeedsReturnsDifferentImages()
    {
        AuthenticateAsTeacher(OwnerIf);
        var firstResponse = await ApiClient.PostAsJsonAsync("api/courses",
            new { name = "Same class", rosterIds = new[] { _rosterId.ToString() }, iconSeed = "course-seed-a-01" },
            JsonOptions, TestCancellationToken);
        var secondResponse = await ApiClient.PostAsJsonAsync("api/courses",
            new { name = "Same class", rosterIds = new[] { _secondRosterId.ToString() }, iconSeed = "course-seed-b-02" },
            JsonOptions, TestCancellationToken);

        firstResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var firstCourse = await firstResponse.Content.ReadFromJsonAsync<CourseDto>(JsonOptions, TestCancellationToken);
        var secondCourse = await secondResponse.Content.ReadFromJsonAsync<CourseDto>(JsonOptions, TestCancellationToken);
        firstCourse!.Title.Should().Be(secondCourse!.Title);

        Anonymous();
        var firstIcon = await ApiClient.GetStringAsync($"api/courses/avatar?seed={firstCourse.IconSeed}",
                                                       TestCancellationToken);
        var secondIcon = await ApiClient.GetStringAsync($"api/courses/avatar?seed={secondCourse.IconSeed}",
                                                        TestCancellationToken);
        firstIcon.Should().NotBe(secondIcon);
    }

    [Fact]
    public async Task GetCourseAvatar_RejectsInvalidSeeds()
    {
        Anonymous();

        var shortSeedResponse = await ApiClient.GetAsync("api/courses/avatar?seed=short", TestCancellationToken);
        var invalidSeedResponse = await ApiClient.GetAsync("api/courses/avatar?seed=invalid_seed", TestCancellationToken);

        shortSeedResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        invalidSeedResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostCourse_EmptyTitle_Returns400()
    {
        AuthenticateAsTeacher(OwnerIf);

        var response = await ApiClient.PostAsJsonAsync("api/courses", new CourseCreateRequest("", _rosterId),
                                                       JsonOptions, TestCancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // failed validation is reported as RFC 9457 problem details
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var problemDetails = await response.Content
                                           .ReadFromJsonAsync<HttpValidationProblemDetails>(
                                               JsonOptions, TestCancellationToken);

        problemDetails.Should().NotBeNull();
        problemDetails.Status.Should().Be((int) HttpStatusCode.BadRequest);

        // the errors are keyed by the camelCase property names of the JSON contract
        problemDetails.Errors.Should().ContainKey("title");

        // the endpoint validates before acting, so nothing should have been persisted
        var courses = await ApiClient.GetFromJsonAsync<CourseOverview[]>("api/courses", JsonOptions,
                                                                        TestCancellationToken);
        courses.Should().BeEmpty();
    }

    [Fact]
    public async Task PostCourse_MissingRoster_Returns404()
    {
        AuthenticateAsTeacher(OwnerIf);

        var response = await ApiClient.PostAsJsonAsync("api/courses", new CourseCreateRequest("Algorithms", 9999L),
                                                       JsonOptions, TestCancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PostCourse_DuplicateTitleInRoster_Returns409()
    {
        await CreateCourseAsOwnerAsync();

        var response = await ApiClient.PostAsJsonAsync("api/courses", ValidCreateRequest(), JsonOptions,
                                                       TestCancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task GetCourses_Teacher_ReturnsList()
    {
        await CreateCourseAsOwnerAsync();

        var overviews = await ApiClient.GetFromJsonAsync<List<CourseOverview>>("api/courses", JsonOptions,
                                                                               TestCancellationToken);

        overviews.Should().NotBeNull();
        overviews!.Should().ContainSingle(o => o.Title == "Algorithms");
    }

    [Fact]
    public async Task GetCourseById_Missing_Returns404()
    {
        AuthenticateAsTeacher(OwnerIf);

        var response = await ApiClient.GetAsync("api/courses/9999", TestCancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetCourseParticipants_StudentMember_ReturnsNamesWithoutStudentNumbers()
    {
        CourseDto course = await CreateCourseAsOwnerAsync();
        AuthenticateAsStudent(StudentIf);

        var response = await ApiClient.GetAsync($"api/courses/{course.Id}/participants", TestCancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        CourseParticipantsDto participants =
            (await response.Content.ReadFromJsonAsync<CourseParticipantsDto>(JsonOptions, TestCancellationToken))!;
        participants.Title.Should().Be("Algorithms");
        participants.Participants.Should().ContainSingle()
                   .Which.Should().Be(new CourseParticipantDto("Test", StudentIf, "5AHIF", Role.Student));
        string body = await response.Content.ReadAsStringAsync(TestCancellationToken);
        body.Should().NotContain("\"studentId\"");
    }

    [Fact]
    public async Task GetCourseParticipants_NonMember_Returns403()
    {
        CourseDto course = await CreateCourseAsOwnerAsync();
        AuthenticateAsStudent("IF000051");

        var response = await ApiClient.GetAsync($"api/courses/{course.Id}/participants", TestCancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PutCourse_Owner_Returns200AndUpdates()
    {
        var created = await CreateCourseAsOwnerAsync();

        var update = new CourseUpdateRequest("Algorithms II", IsReadOnly: true, StudentsRetainAccess: false);
        var response = await ApiClient.PutAsJsonAsync($"api/courses/{created.Id}", update, JsonOptions,
                                                      TestCancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<CourseDto>(JsonOptions, TestCancellationToken);
        updated!.Title.Should().Be("Algorithms II");
        updated.IsReadOnly.Should().BeTrue();
    }

    [Fact]
    public async Task PutCourse_NonOwnerTeacher_Returns403()
    {
        var created = await CreateCourseAsOwnerAsync();

        AuthenticateAsTeacher(OtherTeacherIf);
        var update = new CourseUpdateRequest("Hijacked", IsReadOnly: false, StudentsRetainAccess: true);
        var response = await ApiClient.PutAsJsonAsync($"api/courses/{created.Id}", update, JsonOptions,
                                                      TestCancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PutCourse_Admin_Returns200()
    {
        var created = await CreateCourseAsOwnerAsync();

        AuthenticateAsAdmin(AdminIf);
        var update = new CourseUpdateRequest("Admin Edit", IsReadOnly: false, StudentsRetainAccess: true);
        var response = await ApiClient.PutAsJsonAsync($"api/courses/{created.Id}", update, JsonOptions,
                                                      TestCancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeleteCourse_Owner_Returns204()
    {
        var created = await CreateCourseAsOwnerAsync();

        var response = await ApiClient.DeleteAsync($"api/courses/{created.Id}", TestCancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task DeleteCourse_NonOwnerTeacher_Returns403()
    {
        var created = await CreateCourseAsOwnerAsync();

        AuthenticateAsTeacher(OtherTeacherIf);
        var response = await ApiClient.DeleteAsync($"api/courses/{created.Id}", TestCancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeleteCourse_Admin_Returns204()
    {
        var created = await CreateCourseAsOwnerAsync();

        AuthenticateAsAdmin(AdminIf);
        var response = await ApiClient.DeleteAsync($"api/courses/{created.Id}", TestCancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task DeleteCourse_UnknownId_Returns404()
    {
        AuthenticateAsTeacher(OwnerIf);

        var response = await ApiClient.DeleteAsync("api/courses/9999", TestCancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteCourse_RemovesCourseFromList()
    {
        var created = await CreateCourseAsOwnerAsync();

        var response = await ApiClient.DeleteAsync($"api/courses/{created.Id}", TestCancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var courses = await ApiClient.GetFromJsonAsync<CourseOverview[]>("api/courses", JsonOptions,
                                                                         TestCancellationToken);
        courses.Should().NotContain(course => course.Id == created.Id);
    }
}
