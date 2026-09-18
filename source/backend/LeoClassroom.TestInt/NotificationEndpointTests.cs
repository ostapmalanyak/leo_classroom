using System.Net;
using System.Net.Http.Json;
using LeoClassroom.Endpoints;
using LeoClassroom.Persistence.Model;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Shared;
using LeoClassroom.TestInt.Util;
using Microsoft.EntityFrameworkCore;

namespace LeoClassroom.TestInt;

public sealed class NotificationEndpointTests(WebApiTestFixture fixture) : WebApiTestBase(fixture)
{
    private const string OwnerIf = "IF000001";
    private const string OptInNewIf = "IF000050";
    private const string OptInDeadlineIf = "IF000051";
    private const string OptOutIf = "IF000052";
    private const string NoEmailIf = "IF000053";

    private long _courseId;
    private long _optInNewUserId;

    protected override async ValueTask ImportSeedDataAsync(DatabaseContext context)
    {
        var owner = new User { StudentId = OwnerIf, FirstName = "Olive", LastName = "Wren", Role = Role.Teacher, State = UserState.Active };
        var a = new User { StudentId = OptInNewIf, FirstName = "A", LastName = "A", Role = Role.Student, State = UserState.Active, Email = "a@s.at" };
        var b = new User { StudentId = OptInDeadlineIf, FirstName = "B", LastName = "B", Role = Role.Student, State = UserState.Active, Email = "b@s.at" };
        var c = new User { StudentId = OptOutIf, FirstName = "C", LastName = "C", Role = Role.Student, State = UserState.Active, Email = "c@s.at" };
        var d = new User { StudentId = NoEmailIf, FirstName = "D", LastName = "D", Role = Role.Student, State = UserState.Active, Email = null };
        var roster = new Roster { Name = "5AHIF", Kind = RosterKind.Auto, ClassKey = "5AHIF", Members = [a, b, c, d] };
        context.Users.AddRange(owner, a, b, c, d);
        context.Rosters.Add(roster);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        context.NotificationPreferences.AddRange(
            new NotificationPreference { UserId = a.Id, NewAssignment = true, DeadlineChanged = false },
            new NotificationPreference { UserId = b.Id, NewAssignment = false, DeadlineChanged = true },
            new NotificationPreference { UserId = d.Id, NewAssignment = true, DeadlineChanged = true });

        var course = new Course
        {
            Title = "Algorithms", RosterId = roster.Id, OwnerId = owner.Id, StudentsRetainAccess = true,
            CreatedAt = TestClock.GetCurrentInstant()
        };
        context.Courses.Add(course);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        _courseId = course.Id;
        _optInNewUserId = a.Id;
    }

    private AssignmentCreateRequest CreateRequest() => new(
        _courseId, "A1", null, null,
        new LocalDateTime(2026, 6, 20, 23, 59).InZoneLeniently(Const.TimeZone).ToInstant(),
        DeadlineKind.Soft, false, StarterSourceKind.DescriptionOnly, null, null, false, null,
        DownloadSnapshotMode.Deadline);

    [Fact]
    public async Task CreateAssignment_QueuesNewAssignmentForOptedInStudentsOnly()
    {
        AuthenticateAsTeacher(OwnerIf);
        var created = await ApiClient.PostAsJsonAsync("api/assignments", CreateRequest(), JsonOptions, TestCancellationToken);
        created.StatusCode.Should().Be(HttpStatusCode.Created);

        List<Notification> queued = [];
        await ModifyDatabaseContentAsync(async ctx =>
            queued = await ctx.Notifications.AsNoTracking()
                              .Where(n => n.Trigger == NotificationTrigger.NewAssignment)
                              .ToListAsync(TestCancellationToken));

        queued.Should().ContainSingle();
        queued[0].RecipientUserId.Should().Be(_optInNewUserId);
        queued[0].RecipientEmail.Should().Be("a@s.at");
    }

    [Fact]
    public async Task Preferences_RoundTripForStudent()
    {
        AuthenticateAsStudent(OptOutIf);

        var initial = await ApiClient.GetFromJsonAsync<NotificationPreferenceDto>(
            "api/notifications/preferences", JsonOptions, TestCancellationToken);
        initial!.NewAssignment.Should().BeFalse();
        initial.DeadlineChanged.Should().BeFalse();

        var put = await ApiClient.PutAsJsonAsync("api/notifications/preferences",
            new NotificationPreferenceDto(true, false), JsonOptions, TestCancellationToken);
        put.StatusCode.Should().Be(HttpStatusCode.OK);

        var after = await ApiClient.GetFromJsonAsync<NotificationPreferenceDto>(
            "api/notifications/preferences", JsonOptions, TestCancellationToken);
        after!.NewAssignment.Should().BeTrue();
        after.DeadlineChanged.Should().BeFalse();
    }

    [Fact]
    public async Task Preferences_AsTeacher_IsForbidden()
    {
        AuthenticateAsTeacher(OwnerIf);

        var response = await ApiClient.GetAsync("api/notifications/preferences", TestCancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
