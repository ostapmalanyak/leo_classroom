using LeoClassroom.Persistence.Model;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Shared;

namespace LeoClassroom.Services.Forgejo;

internal sealed class CommitAnalyticsConsumer(IUnitOfWork uow, ILogger<CommitAnalyticsConsumer> logger)
    : IWebhookConsumer
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

        PushDetails push = ForgejoPushPayload.Parse(envelope.Payload);
        CommitAnalytics rollup = acceptance.Analytics;

        if (push.HeadSha is not null && string.Equals(rollup.LastPushHeadSha, push.HeadSha, StringComparison.Ordinal))
        {
            return;
        }

        LocalDate pushDay = envelope.ReceivedAt.InZone(Const.TimeZone).Date;
        if (rollup.LastPushAt is null
            || rollup.LastPushAt.Value.InZone(Const.TimeZone).Date != pushDay)
        {
            rollup.ActiveDayCount++;
        }

        rollup.PushCount++;
        rollup.CommitCount += push.CommitCount;
        rollup.FirstPushAt ??= envelope.ReceivedAt;
        rollup.LastPushAt = envelope.ReceivedAt;
        rollup.LastPushHeadSha = push.HeadSha;

        await uow.SaveChangesAsync();
        logger.LogInformation("Updated commit analytics for acceptance {AcceptanceId}: {Pushes} pushes, {Commits} commits",
                              acceptance.Id, rollup.PushCount, rollup.CommitCount);
    }
}
