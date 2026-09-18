using System.Net;
using System.Net.Http.Json;
using LeoClassroom.Endpoints;
using LeoClassroom.Persistence.Model;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Shared;
using LeoClassroom.TestInt.Util;
using Microsoft.EntityFrameworkCore;

namespace LeoClassroom.TestInt;

public sealed class MoodleEndpointTests(WebApiTestFixture fixture) : WebApiTestBase(fixture)
{
    private const string OwnerIf = "IF000001";
    private const string OtherTeacherIf = "IF000002";

    private long _linkedCourseId;
    private long _plainCourseId;

    protected override async ValueTask ImportSeedDataAsync(DatabaseContext context)
    {
        var owner = new User { StudentId = OwnerIf, FirstName = "Olive", LastName = "Wren", Role = Role.Teacher, State = UserState.Active };
        var other = new User { StudentId = OtherTeacherIf, FirstName = "Carl", LastName = "Tan", Role = Role.Teacher, State = UserState.Active };
        var roster = new Roster { Name = "5AHIF", Kind = RosterKind.Auto, ClassKey = "5AHIF" };
        context.Users.AddRange(owner, other);
        context.Rosters.Add(roster);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var linked = new Course
        {
            Title = "Algorithms", RosterId = roster.Id, OwnerId = owner.Id, StudentsRetainAccess = true,
            CreatedAt = TestClock.GetCurrentInstant()
        };
        var plain = new Course
        {
            Title = "Databases", RosterId = roster.Id, OwnerId = owner.Id, StudentsRetainAccess = true,
            CreatedAt = TestClock.GetCurrentInstant()
        };
        context.Courses.AddRange(linked, plain);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        _linkedCourseId = linked.Id;
        _plainCourseId = plain.Id;

        context.MoodleLinks.Add(new MoodleLink
        {
            CourseId = linked.Id, Enabled = true, MoodleBaseUrl = "https://moodle.test", MoodleCourseId = 99,
            TokenCipher = "seed-cipher"
        });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static AssignmentCreateRequest CreateRequest(long courseId) => new(
        courseId, "A1", null, null, null, DeadlineKind.None, false,
        StarterSourceKind.DescriptionOnly, null, null, false, null, DownloadSnapshotMode.Deadline);

    [Fact]
    public async Task CreateAssignment_InLinkedCourse_QueuesMoodleCreateOp()
    {
        AuthenticateAsTeacher(OwnerIf);
        var created = await ApiClient.PostAsJsonAsync(
            "api/assignments", CreateRequest(_linkedCourseId), JsonOptions, TestCancellationToken);
        created.StatusCode.Should().Be(HttpStatusCode.Created);

        List<MoodleSyncOp> ops = [];
        await ModifyDatabaseContentAsync(async ctx =>
            ops = await ctx.MoodleSyncOps.AsNoTracking()
                           .Where(o => o.CourseId == _linkedCourseId)
                           .ToListAsync(TestCancellationToken));

        ops.Should().ContainSingle(o => o.OpType == MoodleSyncOpType.Create);
    }

    [Fact]
    public async Task CreateAssignment_InUnlinkedCourse_QueuesNothing()
    {
        AuthenticateAsTeacher(OwnerIf);
        var created = await ApiClient.PostAsJsonAsync(
            "api/assignments", CreateRequest(_plainCourseId), JsonOptions, TestCancellationToken);
        created.StatusCode.Should().Be(HttpStatusCode.Created);

        int count = 0;
        await ModifyDatabaseContentAsync(async ctx =>
            count = await ctx.MoodleSyncOps.AsNoTracking()
                             .CountAsync(o => o.CourseId == _plainCourseId, TestCancellationToken));

        count.Should().Be(0);
    }

    [Fact]
    public async Task SetLink_StoresEncryptedToken_NeverReturnsIt()
    {
        AuthenticateAsTeacher(OwnerIf);
        var request = new MoodleLinkRequest(true, "https://moodle.test", 1234, "plaintext-ws-token");
        var put = await ApiClient.PutAsJsonAsync(
            $"api/courses/{_plainCourseId}/moodle-link", request, JsonOptions, TestCancellationToken);
        put.StatusCode.Should().Be(HttpStatusCode.OK);

        var dto = await ApiClient.GetFromJsonAsync<MoodleLinkDto>(
            $"api/courses/{_plainCourseId}/moodle-link", JsonOptions, TestCancellationToken);
        dto!.Enabled.Should().BeTrue();
        dto.MoodleCourseId.Should().Be(1234);
        dto.HasToken.Should().BeTrue();

        string? cipher = null;
        await ModifyDatabaseContentAsync(async ctx =>
            cipher = await ctx.MoodleLinks.AsNoTracking()
                              .Where(l => l.CourseId == _plainCourseId)
                              .Select(l => l.TokenCipher)
                              .FirstOrDefaultAsync(TestCancellationToken));

        cipher.Should().NotBeNullOrEmpty();
        cipher.Should().NotContain("plaintext-ws-token");
    }

    [Fact]
    public async Task GetLink_AsNonCourseTeacher_IsForbidden()
    {
        AuthenticateAsTeacher(OtherTeacherIf);

        var response = await ApiClient.GetAsync(
            $"api/courses/{_linkedCourseId}/moodle-link", TestCancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
