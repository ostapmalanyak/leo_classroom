using LeoClassroom.Services.Auth;
using LeoClassroom.Services.Forgejo;
using LeoClassroom.Services.Util;
using LeoClassroom.Persistence.Model;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Shared;
using OneOf;
using Microsoft.Extensions.Options;
using OneOf.Types;
using System.Globalization;

namespace LeoClassroom.Services;

public sealed record FeedbackPr(long Number, string HtmlUrl);

public interface ISubmissionReviewService
{
    public ValueTask<OneOf<Success<FeedbackPr>, NotFound, Forbidden, ForgejoError>> OpenFeedbackPrAsync(long acceptanceId);
    public ValueTask<OneOf<CommitAnalyticsView, NotFound, Forbidden, ForgejoError>> GetAnalyticsAsync(
        long acceptanceId);

    /// <summary>
    ///     Counts every submission's own commits, for the overview of a whole assignment
    /// </summary>
    public ValueTask<OneOf<IReadOnlyCollection<CommitSummaryView>, NotFound, Forbidden>> GetCommitSummaryAsync(
        long assignmentId);
}

internal sealed class SubmissionReviewService(
    IUnitOfWork uow,
    ICurrentUser currentUser,
    IForgejoClient forgejo,
    IOptions<ForgejoSettings> forgejoSettings,
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
        if (acceptance.Status == SubmissionStatus.DownloadOnly)
        {
            return new NotFound();
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
        if (acceptance.Status == SubmissionStatus.DownloadOnly)
        {
            return new NotFound();
        }

        OneOf<IReadOnlyList<CommitAnalyticsEntry>, NotFound, ForgejoError> commits =
            await GetStudentCommitsAsync(acceptance, acceptance.Assignment);

        return commits.Match<OneOf<CommitAnalyticsView, NotFound, Forbidden, ForgejoError>>(
            entries => ToAnalyticsView(acceptance.Analytics, entries),
            notFound => notFound,
            error => error);
    }

    public async ValueTask<OneOf<IReadOnlyCollection<CommitSummaryView>, NotFound, Forbidden>> GetCommitSummaryAsync(
        long assignmentId)
    {
        Assignment? assignment = await uow.AssignmentRepository.GetWithTeachersAsync(assignmentId);
        if (assignment is null)
        {
            return new NotFound();
        }
        if (!IsTeacherOf(assignment))
        {
            return new Forbidden();
        }

        IReadOnlyCollection<Acceptance> acceptances =
            await uow.AcceptanceRepository.GetByAssignmentWithStudentAsync(assignmentId);

        // one listing per repository; a few at a time, so a whole class neither runs serially nor floods Forgejo
        using var gate = new SemaphoreSlim(SummaryConcurrency);
        CommitSummaryView[] summaries = await Task.WhenAll(acceptances.Select(async acceptance =>
        {
            await gate.WaitAsync();
            try
            {
                return await SummarizeAsync(acceptance, assignment);
            }
            finally
            {
                gate.Release();
            }
        }));

        return summaries;
    }

    private const int SummaryConcurrency = 4;

    private async Task<CommitSummaryView> SummarizeAsync(Acceptance acceptance, Assignment assignment)
    {
        if (acceptance.Status != SubmissionStatus.Ready)
        {
            return new CommitSummaryView(acceptance.Id, null, null);
        }

        OneOf<IReadOnlyList<CommitAnalyticsEntry>, NotFound, ForgejoError> commits =
            await GetStudentCommitsAsync(acceptance, assignment);

        // one repository Forgejo cannot answer for leaves its row without counts instead of failing the class
        return commits.Match(
            entries => new CommitSummaryView(acceptance.Id, entries.Count, entries.Count(entry => entry.Late)),
            notFound => new CommitSummaryView(acceptance.Id, null, null),
            error => new CommitSummaryView(acceptance.Id, null, null));
    }

    private static CommitAnalyticsView ToAnalyticsView(CommitAnalytics rollup,
                                                       IReadOnlyList<CommitAnalyticsEntry> entries)
    {
        // pushes only exist as webhook events, so the ratio uses the commits those same pushes carried
        double commitsPerPush = rollup.PushCount > 0
            ? (double) rollup.CommitCount / rollup.PushCount
            : 0;
        // derived from the commits rather than the push rollup, so days worked before pushes were tracked count too
        int activeDays = entries.Select(entry => entry.At.InZone(Const.TimeZone).Date).Distinct().Count();

        return new CommitAnalyticsView(rollup.PushCount, entries.Count, commitsPerPush, rollup.FirstPushAt,
                                       rollup.LastPushAt, activeDays, entries);
    }

    /// <summary>
    ///     Lists the commits that are the student's own work
    /// </summary>
    /// <remarks>
    ///     A copied or forked starter brings the teacher's whole history along, so only commits made after the
    ///     student accepted count. Of those, the bot's seeding and anything a teacher of the assignment authored
    ///     are left out too. A commit whose e-mail Forgejo cannot link to an account counts as the student's: in a
    ///     student repository that is almost always the student committing with a private address.
    /// </remarks>
    private async ValueTask<OneOf<IReadOnlyList<CommitAnalyticsEntry>, NotFound, ForgejoError>>
        GetStudentCommitsAsync(Acceptance acceptance, Assignment assignment)
    {
        OneOf<IReadOnlyCollection<ForgejoCommit>, NotFound, ForgejoError> commits =
            await forgejo.GetAllCommitsAsync(acceptance.RepoOwner, acceptance.RepoName);

        return commits.Match<OneOf<IReadOnlyList<CommitAnalyticsEntry>, NotFound, ForgejoError>>(
            found => FilterStudentCommits(found, acceptance, assignment),
            notFound => notFound,
            error => error);
    }

    private OneOf<IReadOnlyList<CommitAnalyticsEntry>, NotFound, ForgejoError> FilterStudentCommits(
        IReadOnlyCollection<ForgejoCommit> commits, Acceptance acceptance, Assignment assignment)
    {
        var excludedAuthors = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { forgejoSettings.Value.BotName };
        excludedAuthors.Add(assignment.Owner.StudentId);
        excludedAuthors.UnionWith(assignment.CoTeachers.Select(teacher => teacher.StudentId));

        Instant? deadline = assignment.Deadline;
        List<CommitAnalyticsEntry> entries = [];
        foreach (ForgejoCommit commit in commits)
        {
            if (commit.Author?.Login is { } login && excludedAuthors.Contains(login))
            {
                continue;
            }
            if (ParseInstant(commit.Details.Author.Date) is not { } authoredAt)
            {
                return new ForgejoError(502, $"Forgejo returned an invalid commit date for {commit.Sha}");
            }

            // the committer date moves when a commit is recreated (rebase, amend), the author date does not; a
            // starter commit keeps both, so either one predating the acceptance would do - committer is stricter
            Instant committedAt = ParseInstant(commit.Details.Committer?.Date) ?? authoredAt;
            if (committedAt <= acceptance.AcceptedAt)
            {
                continue;
            }

            entries.Add(new CommitAnalyticsEntry(
                commit.Sha, authoredAt, deadline is not null && authoredAt > deadline.Value,
                commit.Details.Message?.Trim() ?? string.Empty,
                [.. (commit.Files ?? []).Select(file => new CommitFile(file.Filename, file.Status))]));
        }

        return entries;
    }

    private static Instant? ParseInstant(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind,
                                out DateTimeOffset parsed)
            ? Instant.FromDateTimeOffset(parsed)
            : null;

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
