using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using LeoClassroom.Persistence.Model;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Shared;
using LeoClassroom.TestInt.Util;
using Microsoft.EntityFrameworkCore;

namespace LeoClassroom.TestInt;

public sealed class EventConsumerEndpointTests(WebApiTestFixture fixture) : WebApiTestBase(fixture)
{
    private const string OwnerIf = "IF000001";
    private const string OtherTeacherIf = "IF000002";
    private const string StudentIf = "IF000050";
    private const string Org = "algo-5";
    private const string Repo = "algo-a1-IF000050";
    private const string WebhookPath = "/api/webhooks/forgejo";

    private long _acceptanceId;
    private long _assignmentId;

    protected override async ValueTask ImportSeedDataAsync(DatabaseContext context)
    {
        var owner = new User { StudentId = OwnerIf, FirstName = "Olive", LastName = "Wren", Role = Role.Teacher };
        var other = new User { StudentId = OtherTeacherIf, FirstName = "Carl", LastName = "Tan", Role = Role.Teacher };
        var student = new User { StudentId = StudentIf, FirstName = "Sam", LastName = "Pupil", Role = Role.Student };
        var roster = new Roster { Name = "5AHIF", Kind = RosterKind.Auto, ClassKey = "5AHIF", Members = [student] };
        context.Users.AddRange(owner, other, student);
        context.Rosters.Add(roster);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var course = new Course
        {
            Title = "Algorithms", RosterId = roster.Id, OwnerId = owner.Id, StudentsRetainAccess = true,
            CreatedAt = TestClock.GetCurrentInstant(), ForgejoOrg = Org
        };
        context.Courses.Add(course);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var assignment = new Assignment
        {
            CourseId = course.Id, Title = "A1", Slug = "a1", OwnerId = owner.Id,
            DeadlineKind = DeadlineKind.Soft,
            Deadline = new LocalDateTime(2025, 12, 31, 23, 59).InZoneLeniently(Const.TimeZone).ToInstant()
        };
        context.Assignments.Add(assignment);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        _assignmentId = assignment.Id;

        var acceptance = new Acceptance
        {
            AssignmentId = assignment.Id, StudentId = student.Id, RepoOwner = Org, RepoName = Repo,
            AcceptedAt = TestClock.GetCurrentInstant(), Status = SubmissionStatus.Ready, FeedbackState = FeedbackState.None
        };
        context.Acceptances.Add(acceptance);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        _acceptanceId = acceptance.Id;
    }

    private static string Sign(string body) =>
        Convert.ToHexStringLower(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(WebhookSecret), Encoding.UTF8.GetBytes(body)));

    private static HttpRequestMessage WebhookRequest(string body, string eventType)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, WebhookPath)
        {
            Content = new StringContent(body, Encoding.UTF8)
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.Add("X-Forgejo-Event", eventType);
        request.Headers.Add("X-Forgejo-Signature", Sign(body));

        return request;
    }

    [Fact]
    public async Task PostDeadlinePush_FlagsAcceptanceLateAndUpdatesAnalytics()
    {
        const string payload =
            """{"sender":{"login":"IF000050"},"after":"abc123","commits":[{},{}],"repository":{"name":"algo-a1-IF000050","owner":{"login":"algo-5"}}}""";

        var response = await ApiClient.SendAsync(WebhookRequest(payload, "push"), TestCancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        Acceptance? stored = null;
        await ModifyDatabaseContentAsync(async ctx =>
            stored = await ctx.Acceptances.AsNoTracking()
                              .SingleAsync(a => a.Id == _acceptanceId, TestCancellationToken));

        stored!.Late.Should().BeTrue();
        stored.LateSince.Should().Be(TestClock.GetCurrentInstant());
        stored.Analytics.PushCount.Should().Be(1);
        stored.Analytics.CommitCount.Should().Be(2);
    }

    [Fact]
    public async Task ReviewEvent_MarksFeedbackUnread()
    {
        const string payload =
            """{"sender":{"login":"IF000001"},"action":"reviewed","repository":{"name":"algo-a1-IF000050","owner":{"login":"algo-5"}}}""";

        var response = await ApiClient.SendAsync(WebhookRequest(payload, "pull_request_review"), TestCancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        Acceptance? stored = null;
        await ModifyDatabaseContentAsync(async ctx =>
            stored = await ctx.Acceptances.AsNoTracking()
                              .SingleAsync(a => a.Id == _acceptanceId, TestCancellationToken));

        stored!.FeedbackState.Should().Be(FeedbackState.Unread);
    }

    [Fact]
    public async Task Analytics_AsStudent_IsForbidden()
    {
        AuthenticateAsStudent(StudentIf);

        var response = await ApiClient.GetAsync(
            $"api/assignments/acceptances/{_acceptanceId}/analytics", TestCancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Analytics_AsNonAssignedTeacher_IsForbidden()
    {
        AuthenticateAsTeacher(OtherTeacherIf);

        var response = await ApiClient.GetAsync(
            $"api/assignments/acceptances/{_acceptanceId}/analytics", TestCancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Analytics_AsOwningTeacher_ReturnsRollup()
    {
        AuthenticateAsTeacher(OwnerIf);

        var response = await ApiClient.GetAsync(
            $"api/assignments/acceptances/{_acceptanceId}/analytics", TestCancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
