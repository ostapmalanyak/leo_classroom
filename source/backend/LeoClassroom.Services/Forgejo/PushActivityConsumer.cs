using LeoClassroom.Persistence.Model;
using LeoClassroom.Persistence.Util;

namespace LeoClassroom.Services.Forgejo;

internal sealed class PushActivityConsumer(IUnitOfWork uow, ILogger<PushActivityConsumer> logger) : IWebhookConsumer
{
    private const string PushEvent = "push";

    public async ValueTask ConsumeAsync(WebhookEnvelope envelope)
    {
        if (!string.Equals(envelope.EventType, PushEvent, StringComparison.OrdinalIgnoreCase)
            || envelope.RepoOwner is null || envelope.RepoName is null)
        {
            return;
        }

        Acceptance? acceptance =
            await uow.AcceptanceRepository.GetTrackedByRepoAsync(envelope.RepoOwner, envelope.RepoName);
        if (acceptance is null)
        {
            return;
        }

        acceptance.LastPushAt = envelope.ReceivedAt;
        acceptance.LastCommitAt = envelope.CommitterDate;
        await uow.SaveChangesAsync();
        logger.LogInformation("Updated push activity for acceptance {AcceptanceId}", acceptance.Id);
    }
}
