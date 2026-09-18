using System.Net;
using System.Net.Http.Json;
using LeoClassroom.Endpoints;
using LeoClassroom.Services;
using LeoClassroom.Persistence.Model;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Shared;
using LeoClassroom.TestInt.Util;

namespace LeoClassroom.TestInt;

public sealed class CourseTeacherEndpointTests(WebApiTestFixture fixture) : WebApiTestBase(fixture)
{
    private const string OwnerIf = "IF000001";
    private const string OtherTeacherIf = "IF000002";

    private long _rosterId;
    private long _ownerId;
    private long _coTeacherId;

    protected override async ValueTask ImportSeedDataAsync(DatabaseContext context)
    {
        var roster = new Roster { Name = "5AHIF", Kind = RosterKind.Auto, ClassKey = "5AHIF" };
        var owner = new User { StudentId = OwnerIf, FirstName = "Olive", LastName = "Wren", Role = Role.Teacher };
        var coTeacher = new User { StudentId = OtherTeacherIf, FirstName = "Carl", LastName = "Tan", Role = Role.Teacher };
        context.Rosters.Add(roster);
        context.Users.AddRange(owner, coTeacher);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        _rosterId = roster.Id;
        _ownerId = owner.Id;
        _coTeacherId = coTeacher.Id;
    }

    private async Task<long> CreateCourseAsOwnerAsync()
    {
        AuthenticateAsTeacher(OwnerIf);
        var response = await ApiClient.PostAsJsonAsync("api/courses",
            new CourseCreateRequest("Algorithms", _rosterId), JsonOptions, TestCancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        CourseDto course = (await response.Content.ReadFromJsonAsync<CourseDto>(JsonOptions, TestCancellationToken))!;

        return course.Id;
    }

    [Fact]
    public async Task ListTeachers_ReturnsOwner()
    {
        long courseId = await CreateCourseAsOwnerAsync();

        CourseTeachers teachers = (await ApiClient.GetFromJsonAsync<CourseTeachers>(
            $"api/courses/{courseId}/teachers", JsonOptions, TestCancellationToken))!;

        teachers.Owner.Id.Should().Be(_ownerId);
        teachers.CoTeachers.Should().BeEmpty();
    }

    [Fact]
    public async Task Owner_AddsCoTeacher_Returns204()
    {
        long courseId = await CreateCourseAsOwnerAsync();

        var response = await ApiClient.PostAsJsonAsync($"api/courses/{courseId}/teachers",
            new CourseTeacherRequest(_coTeacherId), JsonOptions, TestCancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        CourseTeachers teachers = (await ApiClient.GetFromJsonAsync<CourseTeachers>(
            $"api/courses/{courseId}/teachers", JsonOptions, TestCancellationToken))!;
        teachers.CoTeachers.Should().ContainSingle(t => t.Id == _coTeacherId);
    }

    [Fact]
    public async Task NonOwner_AddsCoTeacher_Returns403()
    {
        long courseId = await CreateCourseAsOwnerAsync();
        AuthenticateAsTeacher(OtherTeacherIf);

        var response = await ApiClient.PostAsJsonAsync($"api/courses/{courseId}/teachers",
            new CourseTeacherRequest(_coTeacherId), JsonOptions, TestCancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RemovingOwner_Returns403()
    {
        long courseId = await CreateCourseAsOwnerAsync();

        var response = await ApiClient.DeleteAsync($"api/courses/{courseId}/teachers/{_ownerId}",
                                                   TestCancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Owner_RemovesCoTeacher_Returns204()
    {
        long courseId = await CreateCourseAsOwnerAsync();
        var added = await ApiClient.PostAsJsonAsync($"api/courses/{courseId}/teachers",
            new CourseTeacherRequest(_coTeacherId), JsonOptions, TestCancellationToken);
        added.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var response = await ApiClient.DeleteAsync($"api/courses/{courseId}/teachers/{_coTeacherId}",
                                                   TestCancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
