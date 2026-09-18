using LeoClassroom.Persistence.Model;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Shared;

namespace LeoClassroom.Services.Forgejo;

internal sealed class LateFlaggingConsumer(IUnitOfWork uow, ILogger<LateFlaggingConsumer> logger) : IWebhookConsumer
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
        if (acceptance is null || acceptance.Late)
        {
            return;
        }

        Assignment? assignment = await uow.AssignmentRepository.GetByIdAsync(acceptance.AssignmentId);
        if (assignment is null || assignment.DeadlineKind != DeadlineKind.Soft || assignment.Deadline is null)
        {
            return;
        }

        if (envelope.ReceivedAt < assignment.Deadline.Value)
        {
            return;
        }

        acceptance.Late = true;
        acceptance.LateSince = envelope.ReceivedAt;
        await uow.SaveChangesAsync();
        logger.LogInformation("Flagged acceptance {AcceptanceId} late as of {LateSince}",
                              acceptance.Id, envelope.ReceivedAt);
    }
}
