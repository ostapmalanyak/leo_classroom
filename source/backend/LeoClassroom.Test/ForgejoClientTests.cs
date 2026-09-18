using System.Net;
using System.Text;
using LeoClassroom.Services.Forgejo;
using LeoClassroom.Shared;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OneOf;
using OneOf.Types;

namespace LeoClassroom.Test;

/// <summary>
///     The parts of the Forgejo client where the wire protocol itself is the thing that can be wrong
/// </summary>
public sealed class ForgejoClientTests
{
    private readonly List<HttpRequestMessage> _requests = [];
    private readonly Queue<HttpResponseMessage> _responses = new();

    private ForgejoClient Build()
    {
        var handler = new StubHandler(_requests, _responses);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://forgejo.test/") };

        return new ForgejoClient(http, Substitute.For<ILogger<ForgejoClient>>());
    }

    private void Respond(HttpStatusCode status, string? json = null) =>
        _responses.Enqueue(new HttpResponseMessage(status)
        {
            Content = new StringContent(json ?? string.Empty, Encoding.UTF8, "application/json")
        });

    [Fact]
    public async Task GetTeamAsync_UsesTheSearchRoute()
    {
        // /orgs/{org}/teams/{name} does not exist and 404s for every team, which silently turns every
        // "ensure the team exists" into "create it again"
        Respond(HttpStatusCode.OK, """{"ok":true,"data":[{"id":42,"name":"teachers"}]}""");

        var result = await Build().GetTeamAsync("algorithms-10", "teachers");

        result.ShouldBe<ForgejoTeam>().Id.Should().Be(42);
        _requests.Single().RequestUri!.PathAndQuery
                 .Should().Be("/api/v1/orgs/algorithms-10/teams/search?q=teachers");
    }

    [Fact]
    public async Task GetTeamAsync_IgnoresLooseSearchMatches()
    {
        // the search matches on substrings, so an exact name still has to be picked out
        Respond(HttpStatusCode.OK,
                """{"ok":true,"data":[{"id":7,"name":"teachers-archive"},{"id":42,"name":"teachers"}]}""");

        var result = await Build().GetTeamAsync("algorithms-10", "teachers");

        result.ShouldBe<ForgejoTeam>().Id.Should().Be(42);
    }

    [Fact]
    public async Task GetTeamAsync_NoMatch_IsNotFound()
    {
        Respond(HttpStatusCode.OK, """{"ok":true,"data":[]}""");

        (await Build().GetTeamAsync("algorithms-10", "teachers")).ShouldBe<NotFound>();
    }

    [Fact]
    public async Task EnsureTeamAsync_ExistingTeam_ReturnsItWithoutCreating()
    {
        Respond(HttpStatusCode.OK, """{"ok":true,"data":[{"id":42,"name":"teachers"}]}""");

        var result = await Build().EnsureTeamAsync("algorithms-10", "teachers", CollaboratorPermission.Admin);

        result.ShouldBe<Success<long>>().Value.Should().Be(42);
        _requests.Should().ContainSingle().Which.Method.Should().Be(HttpMethod.Get);
    }

    [Fact]
    public async Task EnsureTeamAsync_MissingTeam_CreatesIt()
    {
        Respond(HttpStatusCode.OK, """{"ok":true,"data":[]}""");
        Respond(HttpStatusCode.Created, """{"id":43,"name":"teachers"}""");

        var result = await Build().EnsureTeamAsync("algorithms-10", "teachers", CollaboratorPermission.Admin);

        result.ShouldBe<Success<long>>().Value.Should().Be(43);
        _requests[1].Method.Should().Be(HttpMethod.Post);
        _requests[1].RequestUri!.AbsolutePath.Should().Be("/api/v1/orgs/algorithms-10/teams");
    }

    [Fact]
    public async Task EnsureTeamAsync_CreatedConcurrently_LooksItUpInsteadOfFailing()
    {
        Respond(HttpStatusCode.OK, """{"ok":true,"data":[]}""");
        Respond(HttpStatusCode.UnprocessableEntity, """{"message":"team already exists"}""");
        Respond(HttpStatusCode.OK, """{"ok":true,"data":[{"id":44,"name":"teachers"}]}""");

        var result = await Build().EnsureTeamAsync("algorithms-10", "teachers", CollaboratorPermission.Admin);

        result.ShouldBe<Success<long>>().Value.Should().Be(44);
    }

    [Fact]
    public async Task DeleteOrgAsync_AlreadyGone_IsSuccess()
    {
        // a retried cascade has to converge rather than report a failure for work already done
        Respond(HttpStatusCode.NotFound);

        OneOf<Success, ForgejoError> result = await Build().DeleteOrgAsync("algorithms-10");

        result.ShouldBe<Success>();
        _requests.Single().Method.Should().Be(HttpMethod.Delete);
        _requests.Single().RequestUri!.AbsolutePath.Should().Be("/api/v1/orgs/algorithms-10");
    }

    [Fact]
    public async Task EnsureUserAsync_LocalAccount_SendsAPasswordSoForgejoAcceptsIt()
    {
        Respond(HttpStatusCode.Created, """{"id":1,"login":"IF000001"}""");

        await Build().EnsureUserAsync("IF000001", "a@b.at", authSourceId: 0);

        string body = await _requests.Single().Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.Should().Contain("\"password\"").And.Contain("\"source_id\":0");
    }

    [Fact]
    public async Task EnsureUserAsync_AlreadyExists_IsSuccessWithoutCreatingAgain()
    {
        Respond(HttpStatusCode.UnprocessableEntity, """{"message":"user already exists [name: IF000001]"}""");
        Respond(HttpStatusCode.OK, """{"id":1,"login":"IF000001"}""");

        OneOf<Success, ForgejoError> result = await Build().EnsureUserAsync("IF000001", "a@b.at", authSourceId: 0);

        result.ShouldBe<Success>();
        _requests[1].RequestUri!.AbsolutePath.Should().Be("/api/v1/users/IF000001");
    }

    [Fact]
    public async Task EnsureUserAsync_RejectedForADuplicateEmail_IsAnErrorAndNotSuccess()
    {
        // 422 also means "the request was refused" - a duplicate e-mail is the one that happens, because
        // addresses are unique across Forgejo. Reporting it as success sends the caller on to PATCH a user
        // that does not exist, and the 404 it gets there says nothing about the cause.
        Respond(HttpStatusCode.UnprocessableEntity, """{"message":"e-mail already in use [email: a@b.at]"}""");
        Respond(HttpStatusCode.NotFound);

        OneOf<Success, ForgejoError> result = await Build().EnsureUserAsync("IF000001", "a@b.at", authSourceId: 0);

        ForgejoError error = result.ShouldBe<ForgejoError>();
        error.StatusCode.Should().Be(422);
        error.Reason.Should().Contain("e-mail already in use");
    }

    [Fact]
    public async Task EnsureOrgAsync_CreateRefused_IsAnErrorWhenTheOrgStillDoesNotExist()
    {
        Respond(HttpStatusCode.NotFound);                                              // lookup
        Respond(HttpStatusCode.UnprocessableEntity, """{"message":"name is reserved"}"""); // create
        Respond(HttpStatusCode.NotFound);                                              // re-check

        OneOf<Success, ForgejoError> result = await Build().EnsureOrgAsync("algorithms-10");

        result.ShouldBe<ForgejoError>().Reason.Should().Contain("name is reserved");
    }

    [Fact]
    public async Task EnsureOrgAsync_LostTheRace_IsSuccess()
    {
        Respond(HttpStatusCode.NotFound);                                     // lookup
        Respond(HttpStatusCode.UnprocessableEntity);                          // create, someone was faster
        Respond(HttpStatusCode.OK, """{"id":3,"username":"algorithms-10"}"""); // re-check finds it

        (await Build().EnsureOrgAsync("algorithms-10")).ShouldBe<Success>();
    }

    [Fact]
    public async Task CreateBranchAsync_BranchAlreadyThere_IsSuccess()
    {
        Respond(HttpStatusCode.Conflict);
        Respond(HttpStatusCode.OK, """{"name":"feedback"}""");

        (await Build().CreateBranchAsync("algorithms-10", "repo", "feedback", "main")).ShouldBe<Success>();
    }

    [Fact]
    public async Task CreateBranchAsync_SourceRefMissing_IsAnErrorRatherThanSuccess()
    {
        // a 422 here can mean the branch exists or that `oldRefName` does not; only the first is success,
        // and reporting the second as success leaves a feedback branch that was never created
        Respond(HttpStatusCode.UnprocessableEntity, """{"message":"invalid ref name"}""");
        Respond(HttpStatusCode.NotFound);

        OneOf<Success, ForgejoError> result =
            await Build().CreateBranchAsync("algorithms-10", "repo", "feedback", "nope");

        result.ShouldBe<ForgejoError>().Reason.Should().Contain("invalid ref name");
    }

    [Fact]
    public async Task GetAuthenticatedUserAsync_ReadsTheSnakeCaseAdminFlag()
    {
        // Forgejo sends is_admin; without an explicit mapping the property stays false and the health check
        // reports a site administrator as not being one
        Respond(HttpStatusCode.OK,
                """{"id":2,"login":"leo-classroom-bot","email":"bot@htl-leonding.ac.at","is_admin":true}""");

        OneOf<ForgejoCurrentUser, ForgejoError> result = await Build().GetAuthenticatedUserAsync();

        ForgejoCurrentUser identity = result.ShouldBe<ForgejoCurrentUser>();
        identity.IsAdmin.Should().BeTrue();
        identity.Login.Should().Be("leo-classroom-bot");
    }

    private sealed class StubHandler(List<HttpRequestMessage> requests, Queue<HttpResponseMessage> responses)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
                                                               CancellationToken cancellationToken)
        {
            requests.Add(request);

            return Task.FromResult(responses.Count > 0
                                       ? responses.Dequeue()
                                       : new HttpResponseMessage(HttpStatusCode.InternalServerError));
        }
    }
}
