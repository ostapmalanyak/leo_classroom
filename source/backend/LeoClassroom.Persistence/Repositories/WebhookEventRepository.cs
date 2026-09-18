using LeoClassroom.Persistence.Model;
using Microsoft.EntityFrameworkCore;

namespace LeoClassroom.Persistence.Repositories;

public interface IWebhookEventRepository
{
    public void Add(WebhookEvent webhookEvent);
    public ValueTask<WebhookEvent?> GetLastPushReceivedByAsync(string repoOwner, string repoName, Instant cutoff);
}

internal sealed class WebhookEventRepository(DbSet<WebhookEvent> webhookEvents) : IWebhookEventRepository
{
    private const string PushEvent = "push";

    public void Add(WebhookEvent webhookEvent) => webhookEvents.Add(webhookEvent);

    public async ValueTask<WebhookEvent?> GetLastPushReceivedByAsync(string repoOwner, string repoName, Instant cutoff) =>
        await webhookEvents.AsNoTracking()
                           .Where(e => e.EventType == PushEvent
                                       && e.RepoOwner == repoOwner
                                       && e.RepoName == repoName
                                       && e.ReceivedAt <= cutoff)
                           .OrderByDescending(e => e.ReceivedAt)
                           .FirstOrDefaultAsync();
}
