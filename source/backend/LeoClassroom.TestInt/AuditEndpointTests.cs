using System.Net;
using System.Net.Http.Json;
using LeoClassroom.Endpoints;
using LeoClassroom.Persistence.Model;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Shared;
using LeoClassroom.TestInt.Util;

namespace LeoClassroom.TestInt;

public sealed class AuditEndpointTests(WebApiTestFixture fixture) : WebApiTestBase(fixture)
{
    private const string OwnerIf = "IF000001";
    private const string CoTeacherIf = "IF000002";
    private const string AdminIf = "IF000003";
    private const string StudentIf = "IF000050";

    private long _courseId;
    private long _coTeacherId;

    protected override async ValueTask ImportSeedDataAsync(DatabaseContext context)
    {
        var owner = new User { StudentId = OwnerIf, FirstName = "Olive", LastName = "Wren", Role = Role.Teacher };
        var coTeacher = new User { StudentId = CoTeacherIf, FirstName = "Carl", LastName = "Tan", Role = Role.Teacher };
        var roster = new Roster { Name = "5AHIF", Kind = RosterKind.Auto, ClassKey = "5AHIF" };
        context.Users.AddRange(owner, coTeacher);
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
        _coTeacherId = coTeacher.Id;
    }

    [Fact]
    public async Task NonAdmin_Returns403()
    {
        AuthenticateAsTeacher(OwnerIf);

        var response = await ApiClient.GetAsync("api/audit", TestCancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DangerousOperation_WritesAuditRow_AdminCanQuery()
    {
        AuthenticateAsTeacher(OwnerIf);
        var added = await ApiClient.PostAsJsonAsync($"api/courses/{_courseId}/teachers",
            new CourseTeacherRequest(_coTeacherId), JsonOptions, TestCancellationToken);
        added.StatusCode.Should().Be(HttpStatusCode.NoContent);

        AuthenticateAsAdmin(AdminIf);
        var events = await ApiClient.GetFromJsonAsync<AuditEventView[]>(
            "api/audit?action=CourseTeacherAdded", JsonOptions, TestCancellationToken);

        events.Should().ContainSingle(e => e.Action == AuditAction.CourseTeacherAdded
                                           && e.TargetId == _courseId.ToString()
                                           && e.ActorStudentId == OwnerIf);
    }

    [Fact]
    public async Task FilterByActor_ReturnsOnlyMatching()
    {
        AuthenticateAsTeacher(OwnerIf);
        await ApiClient.PostAsJsonAsync($"api/courses/{_courseId}/teachers",
            new CourseTeacherRequest(_coTeacherId), JsonOptions, TestCancellationToken);

        AuthenticateAsAdmin(AdminIf);
        var none = await ApiClient.GetFromJsonAsync<AuditEventView[]>(
            "api/audit?actor=IF999999", JsonOptions, TestCancellationToken);

        none.Should().BeEmpty();
    }
}
