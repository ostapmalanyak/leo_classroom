using LeoClassroom.Services.Auth;
using LeoClassroom.Services.Forgejo;
using LeoClassroom.Services.Util;
using LeoClassroom.Persistence.Model;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Shared;
using OneOf;
using OneOf.Types;
using System.Globalization;

namespace LeoClassroom.Services;

public sealed record FeedbackPr(long Number, string HtmlUrl);

public interface ISubmissionReviewService
{
    public ValueTask<OneOf<Success<FeedbackPr>, NotFound, Forbidden, ForgejoError>> OpenFeedbackPrAsync(long acceptanceId);
    public ValueTask<OneOf<CommitAnalyticsView, NotFound, Forbidden, ForgejoError>> GetAnalyticsAsync(
        long acceptanceId);
}

internal sealed class SubmissionReviewService(
    IUnitOfWork uow,
    ICurrentUser currentUser,
    IForgejoClient forgejo,
    ILogger<SubmissionReviewService> logger) : ISubmissionReviewService
{
    public async ValueTask<OneOf<Success<FeedbackPr>, NotFound, Forbidden, ForgejoError>> OpenFeedbackPrAsync(
        long acceptanceId)
    {
        Acceptance? acceptance = await uow.AcceptanceRepository.GetTrackedWithAssignmentTeachersAsync(acceptanceId);
        if (acceptance is null)
        {
            return new NotFound();
        }
        if (!IsTeacherOf(acceptance.Assignment))
        {
            return new Forbidden();
        }

        OneOf<ForgejoRepo, NotFound, ForgejoError> repo =
            await forgejo.GetRepoAsync(acceptance.RepoOwner, acceptance.RepoName);

        return await repo.Match(
            found => EnsureFeedbackPrAsync(acceptance, found.DefaultBranch ?? ForgejoNaming.DefaultBranchName),
            notFound => Answer(notFound),
            error => Answer(error));
    }

    /// <summary>
    ///     Returns the open feedback pull request, opening the feedback branch and the pull request itself when they
    ///     are not there yet
    /// </summary>
    private async ValueTask<OneOf<Success<FeedbackPr>, NotFound, Forbidden, ForgejoError>> EnsureFeedbackPrAsync(
        Acceptance acceptance, string defaultBranch)
    {
        string head = defaultBranch;
        string baseBranch = ForgejoNaming.FeedbackBranchName;

        OneOf<ForgejoPullRequest, NotFound, ForgejoError> existing =
            await forgejo.FindOpenPullRequestAsync(acceptance.RepoOwner, acceptance.RepoName, head, baseBranch);

        return await existing.Match(
            open => StoreAndReturnAsync(acceptance, open),
            notFound => OpenFeedbackBranchAndPrAsync(acceptance, head, baseBranch, defaultBranch),
            error => Answer(error));
    }

    private async ValueTask<OneOf<Success<FeedbackPr>, NotFound, Forbidden, ForgejoError>>
        OpenFeedbackBranchAndPrAsync(Acceptance acceptance, string head, string baseBranch, string defaultBranch)
    {
        OneOf<Success, ForgejoError> branch = await forgejo.CreateBranchAsync(
            acceptance.RepoOwner, acceptance.RepoName, baseBranch, defaultBranch);
        if (branch.Failure is { } branchFailed)
        {
            return branchFailed;
        }

        OneOf<Success<ForgejoPullRequest>, AlreadyExists, ForgejoError> created =
            await forgejo.CreatePullRequestAsync(acceptance.RepoOwner, acceptance.RepoName, head, baseBranch,
                                                 "Feedback", "Teacher feedback on your submission.");

        return await created.Match(
            opened => StoreAndReturnAsync(acceptance, opened.Value),
            alreadyExists => ReadBackFeedbackPrAsync(acceptance, head, baseBranch),
            error => Answer(error));
    }

    /// <summary>
    ///     Resolves the pull request another caller opened in the meantime
    /// </summary>
    private async ValueTask<OneOf<Success<FeedbackPr>, NotFound, Forbidden, ForgejoError>> ReadBackFeedbackPrAsync(
        Acceptance acceptance, string head, string baseBranch)
    {
        OneOf<ForgejoPullRequest, NotFound, ForgejoError> reused =
            await forgejo.FindOpenPullRequestAsync(acceptance.RepoOwner, acceptance.RepoName, head, baseBranch);

        return await reused.Match(
            open => StoreAndReturnAsync(acceptance, open),
            notFound => Answer(new ForgejoError(409, "feedback pull request exists but could not be resolved")),
            error => Answer(error));
    }

    /// <summary>
    ///     Wraps an outcome that needs no further work, so that the non-async branches of a <c>Match</c> have the
    ///     same shape as the branch that continues the workflow
    /// </summary>
    private static ValueTask<OneOf<Success<FeedbackPr>, NotFound, Forbidden, ForgejoError>> Answer(
        OneOf<Success<FeedbackPr>, NotFound, Forbidden, ForgejoError> outcome) => ValueTask.FromResult(outcome);

    public async ValueTask<OneOf<CommitAnalyticsView, NotFound, Forbidden, ForgejoError>> GetAnalyticsAsync(
        long acceptanceId)
    {
        Acceptance? acceptance = await uow.AcceptanceRepository.GetTrackedWithAssignmentTeachersAsync(acceptanceId);
        if (acceptance is null)
        {
            return new NotFound();
        }
        if (!IsTeacherOf(acceptance.Assignment))
        {
            return new Forbidden();
        }

        OneOf<IReadOnlyCollection<ForgejoCommit>, NotFound, ForgejoError> commits =
            await forgejo.GetAllCommitsAsync(acceptance.RepoOwner, acceptance.RepoName);

        return await commits.Match<ValueTask<OneOf<CommitAnalyticsView, NotFound, Forbidden, ForgejoError>>>(
            forgejoCommits =>
            {
                Instant? deadline = acceptance.Assignment.Deadline;
                HashSet<string> teacherLogins = new(
                [
                    acceptance.Assignment.Owner.StudentId,
                    .. acceptance.Assignment.CoTeachers.Select(teacher => teacher.StudentId)
                ], StringComparer.OrdinalIgnoreCase);
                List<CommitAnalyticsEntry> entries = [];
                foreach (ForgejoCommit commit in forgejoCommits)
                {
                    if (commit.Author?.Login is { } author
                        && (string.Equals(author, "leo-classroom-bot", StringComparison.OrdinalIgnoreCase)
                            || teacherLogins.Contains(author)))
                    {
                        continue;
                    }

                    if (!DateTimeOffset.TryParse(commit.Details.Author.Date, CultureInfo.InvariantCulture,
                                                 DateTimeStyles.RoundtripKind, out DateTimeOffset parsed))
                    {
                        return ValueTask.FromResult<OneOf<CommitAnalyticsView, NotFound, Forbidden, ForgejoError>>(
                            new ForgejoError(502, $"Forgejo returned an invalid commit date for {commit.Sha}"));
                    }

                    Instant at = Instant.FromDateTimeOffset(parsed);
                    entries.Add(new CommitAnalyticsEntry(
                        commit.Sha, at, deadline is not null && at > deadline.Value));
                }

                CommitAnalytics rollup = acceptance.Analytics;
                double commitsPerPush = rollup.PushCount > 0
                    ? (double)entries.Count / rollup.PushCount
                    : 0;

                return ValueTask.FromResult<OneOf<CommitAnalyticsView, NotFound, Forbidden, ForgejoError>>(
                        new CommitAnalyticsView(rollup.PushCount, entries.Count, commitsPerPush,
                                                rollup.FirstPushAt, rollup.LastPushAt, rollup.ActiveDayCount, entries));
                },
                notFound => ValueTask.FromResult<OneOf<CommitAnalyticsView, NotFound, Forbidden, ForgejoError>>(notFound),
                error => ValueTask.FromResult<OneOf<CommitAnalyticsView, NotFound, Forbidden, ForgejoError>>(error));
    }

    private async ValueTask<OneOf<Success<FeedbackPr>, NotFound, Forbidden, ForgejoError>> StoreAndReturnAsync(
        Acceptance acceptance, ForgejoPullRequest pr)
    {
        if (acceptance.FeedbackPrNumber != pr.Number)
        {
            acceptance.FeedbackPrNumber = pr.Number;
            await uow.SaveChangesAsync();
        }
        logger.LogInformation("Feedback PR #{Number} ready for acceptance {AcceptanceId}", pr.Number, acceptance.Id);

        return new Success<FeedbackPr>(new FeedbackPr(pr.Number, pr.HtmlUrl));
    }

    private bool IsTeacherOf(Assignment assignment) =>
        currentUser.Roles.Contains(Role.Admin)
        || string.Equals(assignment.Owner.StudentId, currentUser.StudentId, StringComparison.Ordinal)
        || assignment.CoTeachers.Any(t => string.Equals(t.StudentId, currentUser.StudentId, StringComparison.Ordinal));
}
