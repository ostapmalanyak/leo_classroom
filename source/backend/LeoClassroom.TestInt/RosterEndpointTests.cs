using System.Net;
using System.Net.Http.Json;
using LeoClassroom.Endpoints;
using LeoClassroom.Persistence.Model;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Shared;
using LeoClassroom.TestInt.Util;

namespace LeoClassroom.TestInt;

public sealed class RosterEndpointTests(WebApiTestFixture fixture) : WebApiTestBase(fixture)
{
    private const string OwnerIf = "IF000001";
    private const string OtherTeacherIf = "IF000002";
    private const string StudentIf = "IF000050";

    private long _studentId;
    private long _autoRosterId;

    protected override async ValueTask ImportSeedDataAsync(DatabaseContext context)
    {
        var student = new User { StudentId = "IF000099", FirstName = "Pat", LastName = "Lee", Role = Role.Student };
        var autoRoster = new Roster { Name = "5AHIF", Kind = RosterKind.Auto, ClassKey = "5AHIF" };
        context.Users.Add(student);
        context.Rosters.Add(autoRoster);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        _studentId = student.Id;
        _autoRosterId = autoRoster.Id;
    }

    private async Task<RosterDto> CreateRosterAsOwnerAsync(string name = "Project group")
    {
        AuthenticateAsTeacher(OwnerIf);
        var response = await ApiClient.PostAsJsonAsync("api/rosters", new RosterWriteRequest(name), JsonOptions,
                                                       TestCancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return (await response.Content.ReadFromJsonAsync<RosterDto>(JsonOptions, TestCancellationToken))!;
    }

    [Fact]
    public async Task PostRoster_NoToken_Returns401()
    {
        Anonymous();

        var response = await ApiClient.PostAsJsonAsync("api/rosters", new RosterWriteRequest("x"), JsonOptions,
                                                       TestCancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostRoster_Student_Returns403()
    {
        AuthenticateAsStudent(StudentIf);

        var response = await ApiClient.PostAsJsonAsync("api/rosters", new RosterWriteRequest("x"), JsonOptions,
                                                       TestCancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PostRoster_Teacher_CreatesCustomRoster()
    {
        RosterDto roster = await CreateRosterAsOwnerAsync();

        roster.Kind.Should().Be(RosterKind.Custom);
    }

    [Fact]
    public async Task RenameRoster_ByNonOwner_Returns403()
    {
        RosterDto roster = await CreateRosterAsOwnerAsync();
        AuthenticateAsTeacher(OtherTeacherIf);

        var response = await ApiClient.PutAsJsonAsync($"api/rosters/{roster.Id}", new RosterWriteRequest("New"),
                                                      JsonOptions, TestCancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AddMember_ToCustomRoster_Returns204()
    {
        RosterDto roster = await CreateRosterAsOwnerAsync();

        var response = await ApiClient.PostAsync($"api/rosters/{roster.Id}/members/{_studentId}", null,
                                                 TestCancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task GetMembers_AfterAdd_ReturnsMember()
    {
        RosterDto roster = await CreateRosterAsOwnerAsync();
        var added = await ApiClient.PostAsync($"api/rosters/{roster.Id}/members/{_studentId}", null,
                                              TestCancellationToken);
        added.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var members = await ApiClient.GetFromJsonAsync<UserSummary[]>($"api/rosters/{roster.Id}/members",
                                                                      JsonOptions, TestCancellationToken);

        members.Should().ContainSingle(m => m.Id == _studentId);
    }

    [Fact]
    public async Task GetMembers_UnknownRoster_Returns404()
    {
        AuthenticateAsTeacher(OwnerIf);

        var response = await ApiClient.GetAsync("api/rosters/999999/members", TestCancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AddMember_ToAutoRoster_Returns403()
    {
        AuthenticateAsTeacher(OwnerIf);

        var response = await ApiClient.PostAsync($"api/rosters/{_autoRosterId}/members/{_studentId}", null,
                                                 TestCancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeleteRoster_InUseByCourse_Returns409()
    {
        RosterDto roster = await CreateRosterAsOwnerAsync();
        var courseResponse = await ApiClient.PostAsJsonAsync("api/courses",
            new CourseCreateRequest("Algorithms", roster.Id), JsonOptions, TestCancellationToken);
        courseResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var response = await ApiClient.DeleteAsync($"api/rosters/{roster.Id}", TestCancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
