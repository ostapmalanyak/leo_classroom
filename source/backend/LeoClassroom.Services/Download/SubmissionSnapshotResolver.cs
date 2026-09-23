using LeoClassroom.Services.Forgejo;
using LeoClassroom.Persistence.Model;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Shared;

namespace LeoClassroom.Services.Download;

public readonly record struct SnapshotResolution(string? Sha, bool SeededFallback);

public interface ISubmissionSnapshotResolver
{
    public ValueTask<SnapshotResolution> ResolveAsync(Acceptance acceptance, DownloadSnapshotMode mode, Instant? deadline);
}

internal sealed class SubmissionSnapshotResolver(IUnitOfWork uow) : ISubmissionSnapshotResolver
{
    public async ValueTask<SnapshotResolution> ResolveAsync(
        Acceptance acceptance, DownloadSnapshotMode mode, Instant? deadline)
    {
        if (mode == DownloadSnapshotMode.Head || deadline is null)
        {
            return new SnapshotResolution(null, false);
        }

        WebhookEvent? lastPush = await uow.WebhookEventRepository.GetLastPushReceivedByAsync(
            acceptance.RepoOwner, acceptance.RepoName, deadline.Value);
        if (lastPush is null)
        {
            return new SnapshotResolution(null, true);
        }

        string? headSha = ForgejoPushPayload.Parse(lastPush.Payload).HeadSha;

        return headSha is null
            ? new SnapshotResolution(null, true)
            : new SnapshotResolution(headSha, false);
    }
}
