using LeoClassroom.Persistence.Model;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Shared;

namespace LeoClassroom.Services.Forgejo;

internal sealed class FeedbackReviewConsumer(IUnitOfWork uow, ILogger<FeedbackReviewConsumer> logger) : IWebhookConsumer
{
    private const string ReviewEvent = "pull_request_review";

    public async ValueTask ConsumeAsync(WebhookEnvelope envelope)
    {
        if (!string.Equals(envelope.EventType, ReviewEvent, StringComparison.OrdinalIgnoreCase)
            || envelope.RepoOwner is null || envelope.RepoName is null)
        {
            return;
        }

        Acceptance? acceptance =
            await uow.AcceptanceRepository.GetTrackedByRepoAsync(envelope.RepoOwner, envelope.RepoName);
        if (acceptance is null || acceptance.FeedbackState == FeedbackState.Unread)
        {
            return;
        }

        acceptance.FeedbackState = FeedbackState.Unread;
        acceptance.FeedbackReadAt = null;
        await uow.SaveChangesAsync();
        logger.LogInformation("Marked feedback unread for acceptance {AcceptanceId} from review event", acceptance.Id);
    }
}
