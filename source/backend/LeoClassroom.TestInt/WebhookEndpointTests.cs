using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using LeoClassroom.Persistence.Model;
using LeoClassroom.TestInt.Util;
using Microsoft.EntityFrameworkCore;

namespace LeoClassroom.TestInt;

public sealed class WebhookEndpointTests(WebApiTestFixture fixture) : WebApiTestBase(fixture)
{
    private const string Path = "/api/webhooks/forgejo";

    private const string PushPayload =
        """{"sender":{"login":"if_s"},"repository":{"name":"algo-a1-if_s","owner":{"login":"algo-5"}}}""";

    private static string Sign(string body)
    {
        byte[] hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(WebhookSecret), Encoding.UTF8.GetBytes(body));

        return Convert.ToHexStringLower(hash);
    }

    private static HttpRequestMessage Request(string body, string? signature, string eventType = "push")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Path)
        {
            Content = new StringContent(body, Encoding.UTF8)
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.Add("X-Forgejo-Event", eventType);
        if (signature is not null)
        {
            request.Headers.Add("X-Forgejo-Signature", signature);
        }

        return request;
    }

    [Fact]
    public async Task ValidSignature_AcceptsPersistsAndStampsReceiveTime()
    {
        HttpResponseMessage response =
            await ApiClient.SendAsync(Request(PushPayload, Sign(PushPayload)), TestCancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        WebhookEvent? stored = null;
        await ModifyDatabaseContentAsync(async ctx =>
            stored = await ctx.WebhookEvents.AsNoTracking().SingleOrDefaultAsync(TestCancellationToken));

        stored.Should().NotBeNull();
        stored!.EventType.Should().Be("push");
        stored.Actor.Should().Be("if_s");
        stored.RepoOwner.Should().Be("algo-5");
        stored.RepoName.Should().Be("algo-a1-if_s");
        stored.ReceivedAt.Should().Be(TestClock.GetCurrentInstant());
    }

    [Fact]
    public async Task InvalidSignature_ReturnsUnauthorizedWithNoSideEffects()
    {
        HttpResponseMessage response =
            await ApiClient.SendAsync(Request(PushPayload, "deadbeef"), TestCancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        int count = 0;
        await ModifyDatabaseContentAsync(async ctx =>
            count = await ctx.WebhookEvents.AsNoTracking().CountAsync(TestCancellationToken));
        count.Should().Be(0);
    }

    [Fact]
    public async Task MissingSignature_ReturnsUnauthorized()
    {
        HttpResponseMessage response =
            await ApiClient.SendAsync(Request(PushPayload, signature: null), TestCancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
