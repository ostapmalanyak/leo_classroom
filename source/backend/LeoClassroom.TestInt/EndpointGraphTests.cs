using System.Net;
using LeoClassroom.Persistence.Model;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Services;
using LeoClassroom.Services.Auth;
using LeoClassroom.Services.Forgejo;
using LeoClassroom.Services.Provisioning;
using LeoClassroom.Shared;
using LeoClassroom.TestInt.Util;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using OneOf;
using OneOf.Types;

namespace LeoClassroom.TestInt;

/// <summary>
///     Checks the shape of the endpoint graph itself: that every route group maps without conflicting, that the
///     public endpoints are reachable and that everything else is closed by the fallback authorization policy.
/// </summary>
/// <remarks>
///     Deliberately the only integration test that does not derive from <see cref="WebApiTestBase" />: it must run
///     without Postgres, so that a mapping mistake is caught even where Docker is unavailable.
/// </remarks>
public sealed class EndpointGraphTests
{
    private const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=none;Username=x;Password=y";

    [Theory]
    [InlineData("accept", false, true)]
    [InlineData("retry", false, true)]
    [InlineData("retry", false, false)]
    [InlineData("accept", true, true)]
    [InlineData("retry", true, true)]
    public async Task SubmissionQueue_IsWrittenOnlyAfterSuccessfulCommit(
        string operation, bool commitFails, bool needsProvisioning)
    {
        var transaction = Substitute.For<ITransactionProvider>();
        var queue = Substitute.For<IProvisioningQueue>();
        var service = Substitute.For<IStudentAssignmentService>();
        var cache = Substitute.For<IUserProvisioningCache>();
        cache.Lookup(Arg.Any<ClaimUserData>()).Returns(ProvisioningState.Active);
        var acceptance = new Acceptance
        {
            Id = 5, AssignmentId = 1, RepoOwner = "course", RepoName = "submission",
            Status = SubmissionStatus.Provisioning
        };
        OneOf<Success<Acceptance>, NotFound, Forbidden, AlreadyExists> accepted = new Success<Acceptance>(acceptance);
        service.AcceptAsync(1).Returns(ValueTask.FromResult(accepted));
        OneOf<AcceptanceRetry, NotFound> retry = new AcceptanceRetry(acceptance, needsProvisioning);
        service.RetryAsync(1).Returns(ValueTask.FromResult(retry));

        bool committed = false;
        transaction.CommitAsync().Returns(_ =>
        {
            if (commitFails)
            {
                throw new InvalidOperationException("Simulated commit failure");
            }
            committed = true;

            return ValueTask.CompletedTask;
        });
        queue.EnqueueAsync(5).Returns(_ =>
        {
            committed.Should().BeTrue("the worker must see a committed acceptance");

            return ValueTask.CompletedTask;
        });

        await using var baseFactory = new WebAppFactory(UnreachableDatabase);
        baseFactory.AuthState.SetUser("IF000050", ["student"]);
        await using var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            // This endpoint-graph check substitutes the write boundary and never contacts a database or worker.
            services.RemoveAll<IHostedService>();
            services.AddSingleton(cache);
            services.AddSingleton(service);
            services.AddSingleton(queue);
            services.AddScoped(_ => transaction);
        }));
        using var client = factory.CreateClient();

        var response = await client.PostAsync($"/api/assignments/1/{operation}", null,
                                              TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(commitFails ? HttpStatusCode.InternalServerError : HttpStatusCode.Accepted);
        await queue.Received(!commitFails && (operation == "accept" || needsProvisioning) ? 1 : 0).EnqueueAsync(5);
    }

    [Fact]
    public async Task PublicEndpoints_AreReachableAnonymously()
    {
        await using var factory = new WebAppFactory(UnreachableDatabase);
        var client = factory.CreateClient();
        var cancellationToken = TestContext.Current.CancellationToken;

        (await client.GetAsync("/healthz", cancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/api/version", cancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("/api/courses")]
    [InlineData("/api/rosters")]
    [InlineData("/api/users/search")]
    [InlineData("/api/assignments/mine")]
    [InlineData("/api/notifications/preferences")]
    [InlineData("/api/audit")]
    [InlineData("/api/admin/users/purge-preview")]
    [InlineData("/api/admin/users/1/deletion-impact")]
    public async Task ProtectedEndpoints_AreRefusedAnonymously(string path)
    {
        await using var factory = new WebAppFactory(UnreachableDatabase);
        var client = factory.CreateClient();

        var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        // refused before the handler runs, so the unreachable database is never touched
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UnknownPath_IsRefusedAnonymously()
    {
        await using var factory = new WebAppFactory(UnreachableDatabase);
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/does-not-exist", TestContext.Current.CancellationToken);

        // the fallback policy also covers requests that match no endpoint, so an anonymous caller cannot probe
        // which routes exist
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("/api/courses/0")]
    [InlineData("/api/courses/-1")]
    [InlineData("/api/rosters/0/members")]
    public async Task NonPositiveId_DoesNotAddressAResource(string path)
    {
        await using var factory = new WebAppFactory(UnreachableDatabase);
        var client = factory.CreateClient();
        factory.AuthState.SetUser("teacher-1", ["teacher"]);

        var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        // the route constraint rejects it, so no handler and therefore no database access happens
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("teacher")]
    [InlineData("student")]
    public async Task DestructiveAdminEndpoints_AreRefusedToNonAdmins(string role)
    {
        await using var factory = new WebAppFactory(UnreachableDatabase);
        var client = factory.CreateClient();
        factory.AuthState.SetUser("IF000001", [role]);

        var response = await client.DeleteAsync("/api/admin/users/1", TestContext.Current.CancellationToken);

        // refused by policy before the handler runs, so the unreachable database is never touched
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
