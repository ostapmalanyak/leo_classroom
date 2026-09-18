using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LeoClassroom.Services.Util;
using LeoClassroom.Persistence.Model;
using LeoClassroom.Persistence.Util;
using Microsoft.Extensions.Options;

namespace LeoClassroom.Services.Forgejo;

public sealed record WebhookEnvelope(
    string EventType,
    string? Actor,
    string? RepoOwner,
    string? RepoName,
    Instant ReceivedAt,
    Instant? CommitterDate,
    string Payload);

public interface IWebhookConsumer
{
    public ValueTask ConsumeAsync(WebhookEnvelope envelope);
}

public interface IWebhookDispatcher
{
    public ValueTask DispatchAsync(WebhookEnvelope envelope);
}

internal sealed class WebhookDispatcher(
    IEnumerable<IWebhookConsumer> consumers, ILogger<WebhookDispatcher> logger) : IWebhookDispatcher
{
    public async ValueTask DispatchAsync(WebhookEnvelope envelope)
    {
        foreach (IWebhookConsumer consumer in consumers)
        {
            try
            {
                await consumer.ConsumeAsync(envelope);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Webhook consumer {Consumer} failed for event {EventType}",
                                consumer.GetType().Name, envelope.EventType);
            }
        }
    }
}

public interface IWebhookService
{
    public bool VerifySignature(ReadOnlySpan<byte> body, string? signatureHex);
    public ValueTask AcceptAsync(string eventType, byte[] body);
}

internal sealed class WebhookService(
    IUnitOfWork uow, IWebhookDispatcher dispatcher, IClock clock, IOptions<ForgejoSettings> settings,
    ILogger<WebhookService> logger) : IWebhookService
{
    public bool VerifySignature(ReadOnlySpan<byte> body, string? signatureHex)
    {
        if (string.IsNullOrWhiteSpace(signatureHex))
        {
            return false;
        }

        string secret = settings.Value.WebhookSecret;
        if (string.IsNullOrEmpty(secret))
        {
            // with no secret every caller could compute a valid signature, so refuse rather than accept
            logger.LogError("Forgejo webhook secret is not configured; rejecting the webhook");

            return false;
        }

        byte[] key = Encoding.UTF8.GetBytes(secret);
        byte[] computed = HMACSHA256.HashData(key, body);
        string computedHex = Convert.ToHexStringLower(computed);

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(computedHex), Encoding.ASCII.GetBytes(signatureHex.Trim()));
    }

    public async ValueTask AcceptAsync(string eventType, byte[] body)
    {
        string payload = Encoding.UTF8.GetString(body);
        Instant receivedAt = clock.GetCurrentInstant();
        (string? actor, string? repoOwner, string? repoName, Instant? committerDate) = ParsePayload(payload);

        uow.WebhookEventRepository.Add(new WebhookEvent
        {
            EventType = eventType,
            Actor = actor,
            RepoOwner = repoOwner,
            RepoName = repoName,
            ReceivedAt = receivedAt,
            CommitterDate = committerDate,
            Payload = payload
        });
        await uow.SaveChangesAsync();

        await dispatcher.DispatchAsync(new WebhookEnvelope(
            eventType, actor, repoOwner, repoName, receivedAt, committerDate, payload));
    }

    private (string? Actor, string? RepoOwner, string? RepoName, Instant? CommitterDate) ParsePayload(string payload)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            JsonElement root = document.RootElement;

            string? actor = root.TryGetProperty("sender", out JsonElement sender)
                            && sender.TryGetProperty("login", out JsonElement login)
                ? login.GetString()
                : null;

            string? repoOwner = null;
            string? repoName = null;
            if (root.TryGetProperty("repository", out JsonElement repository))
            {
                if (repository.TryGetProperty("owner", out JsonElement owner)
                    && owner.TryGetProperty("login", out JsonElement ownerLogin))
                {
                    repoOwner = ownerLogin.GetString();
                }
                if (repository.TryGetProperty("name", out JsonElement name))
                {
                    repoName = name.GetString();
                }
            }

            Instant? committerDate = null;
            if (root.TryGetProperty("head_commit", out JsonElement headCommit)
                && headCommit.TryGetProperty("timestamp", out JsonElement timestamp)
                && DateTimeOffset.TryParse(timestamp.GetString(), out DateTimeOffset parsed))
            {
                committerDate = Instant.FromDateTimeOffset(parsed);
            }

            return (actor, repoOwner, repoName, committerDate);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Could not parse webhook payload for event metadata");

            return (null, null, null, null);
        }
    }
}
