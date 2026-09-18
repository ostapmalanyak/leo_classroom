using LeoClassroom.Services.Auth;
using LeoClassroom.Services.Forgejo;
using LeoClassroom.Services.Util;
using LeoClassroom.Persistence.Model;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Shared;
using OneOf;
using OneOf.Types;

namespace LeoClassroom.Services;

public sealed record FeedbackPr(long Number, string HtmlUrl);

public interface ISubmissionReviewService
{
    public ValueTask<OneOf<Success<FeedbackPr>, NotFound, Forbidden, ForgejoError>> OpenFeedbackPrAsync(long acceptanceId);
    public ValueTask<OneOf<CommitAnalyticsView, NotFound, Forbidden>> GetAnalyticsAsync(long acceptanceId);
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

    public async ValueTask<OneOf<CommitAnalyticsView, NotFound, Forbidden>> GetAnalyticsAsync(long acceptanceId)
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

        CommitAnalytics rollup = acceptance.Analytics;
        double commitsPerPush = rollup.PushCount > 0 ? (double)rollup.CommitCount / rollup.PushCount : 0;

        return new CommitAnalyticsView(rollup.PushCount, rollup.CommitCount, commitsPerPush,
                                       rollup.FirstPushAt, rollup.LastPushAt, rollup.ActiveDayCount);
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
