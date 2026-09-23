using LeoClassroom.Services.Forgejo;
using LeoClassroom.Persistence.Model;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Shared;
using OneOf;
using System.Globalization;

namespace LeoClassroom.Services.Download;

public readonly record struct SnapshotResolution(string? Sha, bool SeededFallback);

public interface ISubmissionSnapshotResolver
{
    public ValueTask<SnapshotResolution> ResolveAsync(Acceptance acceptance, DownloadSnapshotMode mode, Instant? deadline);
}

internal sealed class SubmissionSnapshotResolver(IForgejoClient forgejo) : ISubmissionSnapshotResolver
{
    public async ValueTask<SnapshotResolution> ResolveAsync(
        Acceptance acceptance, DownloadSnapshotMode mode, Instant? deadline)
    {
        if (mode == DownloadSnapshotMode.Head || deadline is null)
        {
            return new SnapshotResolution(null, false);
        }

        OneOf<IReadOnlyCollection<ForgejoCommit>, NotFound, ForgejoError> commits =
            await forgejo.GetAllCommitsAsync(acceptance.RepoOwner, acceptance.RepoName);
        IReadOnlyCollection<ForgejoCommit> allCommits = commits.Match(
            found => found,
            _ => Array.Empty<ForgejoCommit>());
        if (allCommits.Count == 0)
        {
            return new SnapshotResolution(null, true);
        }

        foreach ((ForgejoCommit commit, Instant at) in allCommits
                     .Select(commit => (Commit: commit, Parsed: ParseDate(commit)))
                     .Where(item => item.Parsed is not null)
                     .OrderByDescending(item => item.Parsed)
                     .Select(item => (item.Commit, item.Parsed!.Value)))
        {
            if (at <= deadline.Value)
            {
                return new SnapshotResolution(commit.Sha, false);
            }
        }

        return new SnapshotResolution(null, true);
    }

    private static Instant? ParseDate(ForgejoCommit commit) =>
        DateTimeOffset.TryParse(commit.Details.Author.Date, CultureInfo.InvariantCulture,
                                DateTimeStyles.RoundtripKind, out DateTimeOffset parsed)
            ? Instant.FromDateTimeOffset(parsed)
            : null;
}
