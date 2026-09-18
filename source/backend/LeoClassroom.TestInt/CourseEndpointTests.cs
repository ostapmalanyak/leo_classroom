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

    protected override async ValueTask ImportSeedDataAsync(DatabaseContext context)
    {
        var roster = new Roster { Name = "5AHIF" };
        context.Rosters.Add(roster);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        _rosterId = roster.Id;
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
}
