using System.Net;
using System.Net.Http.Json;
using LeoClassroom.Services;
using LeoClassroom.Persistence.Model;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Shared;
using LeoClassroom.TestInt.Util;

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
            CreatedAt = TestClock.GetCurrentInstant()
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
    public async Task Info_ForNonMember_Returns403()
    {
        AuthenticateAsStudent(OutsiderIf);

        var response = await ApiClient.GetAsync($"api/assignments/{_assignmentId}/info", TestCancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
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
}
