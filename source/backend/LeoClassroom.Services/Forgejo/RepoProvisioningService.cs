using System.Net;
using LeoClassroom.Services.Util;
using LeoClassroom.Persistence.Model;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Shared;
using Microsoft.Extensions.Options;
using OneOf;
using OneOf.Types;

namespace LeoClassroom.Services.Forgejo;

public readonly record struct ProvisioningError(string Reason);

public interface IRepoProvisioningService
{
    public ValueTask<OneOf<Success<string>, NotFound, ForgejoError>> ProvisionCourseOrgAsync(long courseId);

    public ValueTask<OneOf<Success, NotFound, ProvisioningError>> ProvisionAcceptanceAsync(long acceptanceId);

    public ValueTask<OneOf<Success, NotFound, ForgejoError>> DeleteAcceptanceAsync(long acceptanceId);
}

internal sealed class RepoProvisioningService(
    IUnitOfWork uow,
    IForgejoClient forgejo,
    IGitService git,
    IArchiveExtractor archiveExtractor,
    IReconciliationService reconciliation,
    IOptions<ForgejoSettings> settings,
    ILogger<RepoProvisioningService> logger) : IRepoProvisioningService
{
    public async ValueTask<OneOf<Success<string>, NotFound, ForgejoError>> ProvisionCourseOrgAsync(long courseId)
    {
        Course? course = await uow.CourseRepository.GetTrackedByIdAsync(courseId);
        if (course is null)
        {
            return new NotFound();
        }

        bool existed = course.ForgejoOrg is not null;
        string org = course.ForgejoOrg ?? ForgejoNaming.OrgName(course.Title, course.Id);

        OneOf<Success, ForgejoError> ensureOrg = await forgejo.EnsureOrgAsync(org);
        if (ensureOrg.Failure is { } orgFailed)
        {
            return orgFailed;
        }

        OneOf<Success<long>, ForgejoError> team =
            await forgejo.EnsureTeamAsync(org, ForgejoNaming.TeachersTeamName, CollaboratorPermission.Admin);

        return await team.Match<ValueTask<OneOf<Success<string>, NotFound, ForgejoError>>>(
            teamId => CompleteCourseOrgAsync(course, org, teamId.Value, existed),
            error => ValueTask.FromResult<OneOf<Success<string>, NotFound, ForgejoError>>(error));
    }

    /// <summary>
    ///     Puts the owner in the teachers team and, for an organisation created just now, installs the push webhook
    ///     and records the organisation on the course
    /// </summary>
    private async ValueTask<OneOf<Success<string>, NotFound, ForgejoError>> CompleteCourseOrgAsync(
        Course course, string org, long teamId, bool orgExisted)
    {
        OneOf<Success, ForgejoError> member = await EnsureMemberAsync(teamId, course.Owner.StudentId);
        if (member.Failure is { } memberFailed)
        {
            return memberFailed;
        }

        if (!orgExisted)
        {
            OneOf<Success, ForgejoError> hook = await forgejo.EnsureOrgWebhookAsync(
                org, settings.Value.WebhookTargetUrl, settings.Value.WebhookSecret);
            if (hook.Failure is { } hookFailed)
            {
                return hookFailed;
            }

            course.ForgejoOrg = org;
            await uow.SaveChangesAsync();
        }

        return new Success<string>(org);
    }

    public async ValueTask<OneOf<Success, NotFound, ProvisioningError>> ProvisionAcceptanceAsync(long acceptanceId)
    {
        Acceptance? acceptance = await uow.AcceptanceRepository.GetTrackedByIdAsync(acceptanceId);
        if (acceptance is null)
        {
            return new NotFound();
        }

        Assignment? assignment = await uow.AssignmentRepository.GetByIdAsync(acceptance.AssignmentId);
        if (assignment is null)
        {
            return new NotFound();
        }

        if (assignment.StarterSourceKind == StarterSourceKind.ForkOwnRepo)
        {
            User? student = await uow.UserRepository.GetByIdAsync(acceptance.StudentId);
            if (student is null)
            {
                return await FailAsync(acceptance, "student not found");
            }

            acceptance.RepoOwner = student.StudentId;
        }

        OneOf<Success<string>, NotFound, ForgejoError> orgResult = await ProvisionCourseOrgAsync(assignment.CourseId);

        return await orgResult.Match<ValueTask<OneOf<Success, NotFound, ProvisioningError>>>(
            org => ProvisionRepositoryAsync(acceptance, assignment),
            async notFound => await FailAsync(acceptance, "course not found"),
            async error => await FailAsync(acceptance, $"could not provision course org: {error.Reason}"));
    }

    /// <summary>
    ///     Uses the student's repository when it is already there, and otherwise creates and seeds it
    /// </summary>
    private async ValueTask<OneOf<Success, NotFound, ProvisioningError>> ProvisionRepositoryAsync(
        Acceptance acceptance, Assignment assignment)
    {
        OneOf<ForgejoRepo, NotFound, ForgejoError> existing =
            await forgejo.GetRepoAsync(acceptance.RepoOwner, acceptance.RepoName);

        return await existing.Match<ValueTask<OneOf<Success, NotFound, ProvisioningError>>>(
            repo => StoreReadyAsync(acceptance, repo.CloneUrl),
            notFound => SeedRepositoryAsync(acceptance, assignment, acceptance.RepoOwner),
            async error => await FailAsync(acceptance, error.Reason));
    }

    private async ValueTask<OneOf<Success, NotFound, ProvisioningError>> SeedRepositoryAsync(
        Acceptance acceptance, Assignment assignment, string repoOwner)
    {
        OneOf<ForgejoRepo, ProvisioningError> seeded =
            await CreateAndSeedAsync(assignment, repoOwner, acceptance.RepoName);

        return await seeded.Match<ValueTask<OneOf<Success, NotFound, ProvisioningError>>>(
            async repo =>
            {
                string baseline = repo.DefaultBranch ?? ForgejoNaming.DefaultBranchName;
                OneOf<Success, ForgejoError> branch = await forgejo.CreateBranchAsync(
                    repoOwner, acceptance.RepoName, ForgejoNaming.FeedbackBranchName, baseline);
                if (branch.Failure is { } branchFailed)
                {
                    logger.LogError("Could not pin feedback branch for acceptance {AcceptanceId} (HTTP {Status}): {Reason}",
                                    acceptance.Id, branchFailed.StatusCode, branchFailed.Reason);
                }

                return await StoreReadyAsync(acceptance, repo.CloneUrl);
            },
            async error => await FailAsync(acceptance, error.Reason));
    }

    private async ValueTask<OneOf<Success, NotFound, ProvisioningError>> StoreReadyAsync(
        Acceptance acceptance, string cloneUrl)
    {
        acceptance.RepoUrl = cloneUrl;
        acceptance.Status = SubmissionStatus.Ready;
        acceptance.FailureReason = null;
        await uow.SaveChangesAsync();

        OneOf<Success, NotFound, ForgejoError> reconcile = await reconciliation.ReconcileAsync(acceptance.AssignmentId);
        reconcile.Switch(
            success => { },
            notFound => { },
            error => logger.LogError(
                "Acceptance {AcceptanceId} provisioned but access reconcile failed (HTTP {Status}): {Reason}",
                acceptance.Id, error.StatusCode, error.Reason));

        return new Success();
    }

    private async ValueTask<ProvisioningError> FailAsync(Acceptance acceptance, string reason)
    {
        acceptance.Status = SubmissionStatus.Failed;
        acceptance.FailureReason = reason;
        await uow.SaveChangesAsync();
        logger.LogError("Provisioning failed for acceptance {AcceptanceId}: {Reason}", acceptance.Id, reason);

        return new ProvisioningError(reason);
    }

    public async ValueTask<OneOf<Success, NotFound, ForgejoError>> DeleteAcceptanceAsync(long acceptanceId)
    {
        Acceptance? acceptance = await uow.AcceptanceRepository.GetByIdAsync(acceptanceId);
        if (acceptance is null)
        {
            return new NotFound();
        }

        OneOf<Success, ForgejoError> deleted = await forgejo.DeleteRepoAsync(acceptance.RepoOwner, acceptance.RepoName);
        if (deleted.Failure is { } deleteFailed)
        {
            return deleteFailed;
        }

        await uow.AcceptanceRepository.DeleteByIdAsync(acceptanceId);
        await uow.SaveChangesAsync();

        return new Success();
    }

    private async ValueTask<OneOf<ForgejoRepo, ProvisioningError>> CreateAndSeedAsync(
        Assignment assignment, string repoOwner, string repoName)
    {
        switch (assignment.StarterSourceKind)
        {
            case StarterSourceKind.DescriptionOnly:
                return await SeedDescriptionOnlyAsync(assignment, repoOwner, repoName);
            case StarterSourceKind.Archive:
                return await SeedFromArchiveAsync(assignment, repoOwner, repoName);
            case StarterSourceKind.ForkOwnRepo:
                return await SeedByForkAsync(assignment, repoOwner, repoName);
            case StarterSourceKind.CopyRepo:
                return await SeedByMigrateAsync(assignment, repoOwner, repoName);
            default:
                return new ProvisioningError("unknown starter source");
        }
    }

    private async ValueTask<OneOf<ForgejoRepo, ProvisioningError>> SeedDescriptionOnlyAsync(
        Assignment assignment, string repoOwner, string repoName)
    {
        OneOf<ForgejoRepo, ProvisioningError> created = await CreateEmptyRepoAsync(repoOwner, repoName);

        return await created.Match<ValueTask<OneOf<ForgejoRepo, ProvisioningError>>>(
            async repo =>
            {
                string readme = string.IsNullOrWhiteSpace(assignment.ReadmeMarkdown)
                    ? $"# {assignment.Title}"
                    : assignment.ReadmeMarkdown;
                OneOf<Success, ForgejoError> file = await forgejo.CreateFileAsync(
                    repoOwner, repoName, "README.md", readme, "Initial commit");
                if (file.Failure is { } fileFailed)
                {
                    if (fileFailed.StatusCode == (int) HttpStatusCode.Conflict)
                    {
                        return await OverwriteReadmeAsync(repo, readme);
                    }

                    return new ProvisioningError(fileFailed.Reason);
                }

                return repo;
            },
            error => ValueTask.FromResult<OneOf<ForgejoRepo, ProvisioningError>>(error));
    }

    private async ValueTask<OneOf<ForgejoRepo, ProvisioningError>> SeedFromArchiveAsync(
        Assignment assignment, string repoOwner, string repoName)
    {
        if (string.IsNullOrEmpty(assignment.ArchivePath) || !File.Exists(assignment.ArchivePath))
        {
            return new ProvisioningError("starter archive is missing");
        }

        string archivePath = assignment.ArchivePath;
        OneOf<ForgejoRepo, ProvisioningError> created = await CreateEmptyRepoAsync(repoOwner, repoName);

        return await created.Match<ValueTask<OneOf<ForgejoRepo, ProvisioningError>>>(
            repo => ExtractIntoRepositoryAsync(assignment, archivePath, repo),
            error => ValueTask.FromResult<OneOf<ForgejoRepo, ProvisioningError>>(error));
    }

    private async ValueTask<OneOf<ForgejoRepo, ProvisioningError>> ExtractIntoRepositoryAsync(
        Assignment assignment, string archivePath, ForgejoRepo repo)
    {
        string extractDir = Path.Combine(Path.GetTempPath(), $"leo-archive-{Guid.NewGuid():N}");
        Directory.CreateDirectory(extractDir);
        try
        {
            await using (FileStream archive = File.OpenRead(archivePath))
            {
                OneOf<Success, ExtractionError> extracted = archiveExtractor.ExtractToDirectory(archive, extractDir);
                if (extracted.Failure is { } extractFailed)
                {
                    return new ProvisioningError(extractFailed.Reason);
                }
            }

            OneOf<Success, GitError> seed = await git.SeedRepositoryFromDirectoryAsync(repo.CloneUrl, extractDir);
            if (seed.Failure is { } seedFailed)
            {
                return new ProvisioningError(seedFailed.Reason);
            }
        }
        finally
        {
            TryDelete(extractDir);
        }

        return await ApplyReadmeOverrideAsync(assignment, repo);
    }

    private async ValueTask<OneOf<ForgejoRepo, ProvisioningError>> SeedByForkAsync(
        Assignment assignment, string repoOwner, string repoName)
    {
        if (!TryParseOwnerRepo(assignment.StarterRepoUrl, out string sourceOwner, out string sourceRepo))
        {
            return new ProvisioningError("invalid fork source repository URL");
        }

        OneOf<Success<ForgejoRepo>, ForgejoError> fork =
            await forgejo.ForkRepoAsync(sourceOwner, sourceRepo, repoOwner, repoName);

        return await fork.Match<ValueTask<OneOf<ForgejoRepo, ProvisioningError>>>(
            forked => ApplyForkReadmeOverrideAsync(assignment, repoOwner, forked.Value),
            async error =>
            {
                if (error.StatusCode != (int) HttpStatusCode.Conflict)
                {
                    return new ProvisioningError(error.Reason);
                }

                OneOf<ForgejoRepo, ProvisioningError> existing =
                    await GetExistingRepoAsync(repoOwner, repoName, error.Reason);

                return await existing
                    .Match<ValueTask<OneOf<ForgejoRepo, ProvisioningError>>>(
                        repo => ApplyForkReadmeOverrideAsync(assignment, repoOwner, repo),
                        existingError => ValueTask.FromResult<OneOf<ForgejoRepo, ProvisioningError>>(
                            existingError));
            });
    }

    private async ValueTask<OneOf<ForgejoRepo, ProvisioningError>> ApplyForkReadmeOverrideAsync(
        Assignment assignment, string repoOwner, ForgejoRepo repo)
    {
        if (string.IsNullOrWhiteSpace(assignment.ReadmeMarkdown))
        {
            return repo;
        }

        OneOf<Success, ForgejoError> botAccess = await forgejo.SetCollaboratorAsync(
            repoOwner, repo.Name, settings.Value.BotName, CollaboratorPermission.Write);
        if (botAccess.Failure is { } accessFailed)
        {
            return new ProvisioningError(accessFailed.Reason);
        }

        return await ApplyReadmeOverrideAsync(assignment, repo);
    }

    internal async ValueTask<OneOf<ForgejoRepo, ProvisioningError>> SeedByMigrateAsync(
        Assignment assignment, string org, string repoName)
    {
        if (string.IsNullOrWhiteSpace(assignment.StarterRepoUrl))
        {
            return new ProvisioningError("missing copy source repository URL");
        }

        string sourceUrl = assignment.StarterRepoUrl;
        string? authUsername = null;
        string? authPassword = null;
        if (TryParseOwnerRepo(sourceUrl, out string sourceOwner, out string sourceRepo)
            && Uri.TryCreate(sourceUrl, UriKind.Absolute, out Uri? requestedUri))
        {
            OneOf<ForgejoRepo, NotFound, ForgejoError> ownRepo =
                await forgejo.GetRepoAsync(sourceOwner, sourceRepo);
            ownRepo.Switch(
                repo =>
                {
                    if (Uri.TryCreate(repo.CloneUrl, UriKind.Absolute, out Uri? canonicalUri)
                        && HasSameAuthority(requestedUri, canonicalUri))
                    {
                        sourceUrl = InternalCloneUrl(canonicalUri);
                        authUsername = settings.Value.BotName;
                        authPassword = settings.Value.AdminToken;
                    }
                },
                notFound => { },
                error => logger.LogDebug("Could not check whether starter repository is local: {Reason}",
                                         error.Reason));
        }

        OneOf<Success<ForgejoRepo>, ForgejoError> migrated =
            await forgejo.MigrateRepoAsync(sourceUrl, org, repoName, isPrivate: true,
                                           authUsername: authUsername, authPassword: authPassword);

        return await migrated.Match<ValueTask<OneOf<ForgejoRepo, ProvisioningError>>>(
            copied => ApplyReadmeOverrideAsync(assignment, copied.Value),
            async error =>
            {
                if (error.StatusCode != (int) HttpStatusCode.Conflict)
                {
                    return new ProvisioningError(error.Reason);
                }

                OneOf<ForgejoRepo, ProvisioningError> existing =
                    await GetExistingRepoAsync(org, repoName, error.Reason);

                return await existing
                    .Match<ValueTask<OneOf<ForgejoRepo, ProvisioningError>>>(
                        repo => ApplyReadmeOverrideAsync(assignment, repo),
                        existingError => ValueTask.FromResult<OneOf<ForgejoRepo, ProvisioningError>>(
                            existingError));
            });
    }

    private string InternalCloneUrl(Uri cloneUri)
    {
        Uri forgejoUri = new(settings.Value.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        var internalUri = new UriBuilder(forgejoUri) { Path = cloneUri.AbsolutePath };

        return internalUri.Uri.AbsoluteUri;
    }

    private static bool HasSameAuthority(Uri left, Uri right) =>
        string.Equals(left.Host, right.Host, StringComparison.OrdinalIgnoreCase) && left.Port == right.Port;

    private async ValueTask<OneOf<ForgejoRepo, ProvisioningError>> CreateEmptyRepoAsync(string org, string repoName)
    {
        OneOf<Success<ForgejoRepo>, AlreadyExists, ForgejoError> created =
            await forgejo.CreateOrgRepoAsync(org, repoName, autoInit: false, isPrivate: true);

        return await created.Match<ValueTask<OneOf<ForgejoRepo, ProvisioningError>>>(
            success => ValueTask.FromResult<OneOf<ForgejoRepo, ProvisioningError>>(success.Value),
            async _ => await GetExistingRepoAsync(
                org, repoName, $"repository {org}/{repoName} already exists"),
            error => ValueTask.FromResult<OneOf<ForgejoRepo, ProvisioningError>>(
                new ProvisioningError(error.Reason)));
    }

    private async ValueTask<OneOf<ForgejoRepo, ProvisioningError>> ApplyReadmeOverrideAsync(
        Assignment assignment, ForgejoRepo repo)
    {
        if (string.IsNullOrWhiteSpace(assignment.ReadmeMarkdown))
        {
            return repo;
        }

        return await OverwriteReadmeAsync(repo, assignment.ReadmeMarkdown);
    }

    private async ValueTask<OneOf<ForgejoRepo, ProvisioningError>> OverwriteReadmeAsync(
        ForgejoRepo repo, string content)
    {
        OneOf<Success, GitError> overwrite = await git.OverwriteFileAsync(
            repo.CloneUrl, "README.md", content, "Override README");
        if (overwrite.Failure is { } overwriteFailed)
        {
            return new ProvisioningError(overwriteFailed.Reason);
        }

        return repo;
    }

    private async ValueTask<OneOf<ForgejoRepo, ProvisioningError>> GetExistingRepoAsync(
        string org, string repoName, string notFoundReason)
    {
        OneOf<ForgejoRepo, NotFound, ForgejoError> existing = await forgejo.GetRepoAsync(org, repoName);

        return existing.Match<OneOf<ForgejoRepo, ProvisioningError>>(
            repo => repo,
            _ => new ProvisioningError(notFoundReason),
            error => new ProvisioningError(error.Reason));
    }

    private async ValueTask<OneOf<Success, ForgejoError>> EnsureMemberAsync(long teamId, string username)
    {
        OneOf<bool, ForgejoError> isMember = await forgejo.IsTeamMemberAsync(teamId, username);

        return await isMember.Match<ValueTask<OneOf<Success, ForgejoError>>>(
            async member =>
            {
                if (member)
                {
                    return new Success();
                }

                return await forgejo.AddTeamMemberAsync(teamId, username);
            },
            error => ValueTask.FromResult<OneOf<Success, ForgejoError>>(error));
    }

    private static bool TryParseOwnerRepo(string? url, out string owner, out string repo)
    {
        owner = string.Empty;
        repo = string.Empty;
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return false;
        }

        string[] segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2)
        {
            return false;
        }

        owner = segments[^2];
        repo = segments[^1].EndsWith(".git", StringComparison.OrdinalIgnoreCase)
            ? segments[^1][..^4]
            : segments[^1];

        return true;
    }

    private void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Could not remove temporary extraction directory {Path}", path);
        }
    }
}
