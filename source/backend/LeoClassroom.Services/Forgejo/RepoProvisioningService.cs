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

        OneOf<Success<string>, NotFound, ForgejoError> orgResult = await ProvisionCourseOrgAsync(assignment.CourseId);

        return await orgResult.Match<ValueTask<OneOf<Success, NotFound, ProvisioningError>>>(
            org => ProvisionRepositoryAsync(acceptance, assignment, org.Value),
            async notFound => await FailAsync(acceptance, "course not found"),
            async error => await FailAsync(acceptance, $"could not provision course org: {error.Reason}"));
    }

    /// <summary>
    ///     Uses the student's repository when it is already there, and otherwise creates and seeds it
    /// </summary>
    private async ValueTask<OneOf<Success, NotFound, ProvisioningError>> ProvisionRepositoryAsync(
        Acceptance acceptance, Assignment assignment, string org)
    {
        OneOf<ForgejoRepo, NotFound, ForgejoError> existing = await forgejo.GetRepoAsync(org, acceptance.RepoName);

        return await existing.Match<ValueTask<OneOf<Success, NotFound, ProvisioningError>>>(
            repo => StoreReadyAsync(acceptance, org, repo.CloneUrl),
            notFound => SeedRepositoryAsync(acceptance, assignment, org),
            async error => await FailAsync(acceptance, error.Reason));
    }

    private async ValueTask<OneOf<Success, NotFound, ProvisioningError>> SeedRepositoryAsync(
        Acceptance acceptance, Assignment assignment, string org)
    {
        OneOf<ForgejoRepo, ProvisioningError> seeded = await CreateAndSeedAsync(assignment, org, acceptance.RepoName);

        return await seeded.Match<ValueTask<OneOf<Success, NotFound, ProvisioningError>>>(
            async repo =>
            {
                string baseline = repo.DefaultBranch ?? ForgejoNaming.DefaultBranchName;
                OneOf<Success, ForgejoError> branch = await forgejo.CreateBranchAsync(
                    org, acceptance.RepoName, ForgejoNaming.FeedbackBranchName, baseline);
                if (branch.Failure is { } branchFailed)
                {
                    logger.LogWarning("Could not pin feedback branch for acceptance {AcceptanceId}: {Reason}",
                                      acceptance.Id, branchFailed.Reason);
                }

                return await StoreReadyAsync(acceptance, org, repo.CloneUrl);
            },
            async error => await FailAsync(acceptance, error.Reason));
    }

    private async ValueTask<OneOf<Success, NotFound, ProvisioningError>> StoreReadyAsync(
        Acceptance acceptance, string org, string cloneUrl)
    {
        acceptance.RepoOwner = org;
        acceptance.RepoUrl = cloneUrl;
        acceptance.Status = SubmissionStatus.Ready;
        await uow.SaveChangesAsync();

        OneOf<Success, NotFound, ForgejoError> reconcile = await reconciliation.ReconcileAsync(acceptance.AssignmentId);
        reconcile.Switch(
            success => { },
            notFound => { },
            error => logger.LogWarning("Acceptance {AcceptanceId} provisioned but access reconcile failed: {Reason}",
                                       acceptance.Id, error.Reason));

        return new Success();
    }

    private async ValueTask<ProvisioningError> FailAsync(Acceptance acceptance, string reason)
    {
        acceptance.Status = SubmissionStatus.Failed;
        await uow.SaveChangesAsync();
        logger.LogWarning("Provisioning failed for acceptance {AcceptanceId}: {Reason}", acceptance.Id, reason);

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
        Assignment assignment, string org, string repoName)
    {
        switch (assignment.StarterSourceKind)
        {
            case StarterSourceKind.DescriptionOnly:
                return await SeedDescriptionOnlyAsync(assignment, org, repoName);
            case StarterSourceKind.Archive:
                return await SeedFromArchiveAsync(assignment, org, repoName);
            case StarterSourceKind.ForkOwnRepo:
                return await SeedByForkAsync(assignment, org, repoName);
            case StarterSourceKind.CopyRepo:
                return await SeedByMigrateAsync(assignment, org, repoName);
            default:
                return new ProvisioningError("unknown starter source");
        }
    }

    private async ValueTask<OneOf<ForgejoRepo, ProvisioningError>> SeedDescriptionOnlyAsync(
        Assignment assignment, string org, string repoName)
    {
        OneOf<ForgejoRepo, ProvisioningError> created = await CreateEmptyRepoAsync(org, repoName);

        return await created.Match<ValueTask<OneOf<ForgejoRepo, ProvisioningError>>>(
            async repo =>
            {
                string readme = string.IsNullOrWhiteSpace(assignment.ReadmeMarkdown)
                    ? $"# {assignment.Title}"
                    : assignment.ReadmeMarkdown;
                OneOf<Success, ForgejoError> file = await forgejo.CreateFileAsync(
                    org, repoName, "README.md", readme, "Initial commit");
                if (file.Failure is { } fileFailed)
                {
                    return new ProvisioningError(fileFailed.Reason);
                }

                return repo;
            },
            error => ValueTask.FromResult<OneOf<ForgejoRepo, ProvisioningError>>(error));
    }

    private async ValueTask<OneOf<ForgejoRepo, ProvisioningError>> SeedFromArchiveAsync(
        Assignment assignment, string org, string repoName)
    {
        if (string.IsNullOrEmpty(assignment.ArchivePath) || !File.Exists(assignment.ArchivePath))
        {
            return new ProvisioningError("starter archive is missing");
        }

        string archivePath = assignment.ArchivePath;
        OneOf<ForgejoRepo, ProvisioningError> created = await CreateEmptyRepoAsync(org, repoName);

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
        Assignment assignment, string org, string repoName)
    {
        if (!TryParseOwnerRepo(assignment.StarterRepoUrl, out string sourceOwner, out string sourceRepo))
        {
            return new ProvisioningError("invalid fork source repository URL");
        }

        OneOf<Success<ForgejoRepo>, ForgejoError> fork =
            await forgejo.ForkRepoAsync(sourceOwner, sourceRepo, org, repoName);

        return await fork.Match(
            forked => ApplyReadmeOverrideAsync(assignment, forked.Value),
            error => ValueTask.FromResult<OneOf<ForgejoRepo, ProvisioningError>>(
                new ProvisioningError(error.Reason)));
    }

    private async ValueTask<OneOf<ForgejoRepo, ProvisioningError>> SeedByMigrateAsync(
        Assignment assignment, string org, string repoName)
    {
        if (string.IsNullOrWhiteSpace(assignment.StarterRepoUrl))
        {
            return new ProvisioningError("missing copy source repository URL");
        }

        OneOf<Success<ForgejoRepo>, ForgejoError> migrated =
            await forgejo.MigrateRepoAsync(assignment.StarterRepoUrl, org, repoName, isPrivate: true);

        return await migrated.Match(
            copied => ApplyReadmeOverrideAsync(assignment, copied.Value),
            error => ValueTask.FromResult<OneOf<ForgejoRepo, ProvisioningError>>(
                new ProvisioningError(error.Reason)));
    }

    private async ValueTask<OneOf<ForgejoRepo, ProvisioningError>> CreateEmptyRepoAsync(string org, string repoName)
    {
        OneOf<Success<ForgejoRepo>, AlreadyExists, ForgejoError> created =
            await forgejo.CreateOrgRepoAsync(org, repoName, autoInit: false, isPrivate: true);

        return created.Match<OneOf<ForgejoRepo, ProvisioningError>>(
            success => success.Value,
            _ => new ProvisioningError($"repository {org}/{repoName} already exists"),
            error => new ProvisioningError(error.Reason));
    }

    private async ValueTask<OneOf<ForgejoRepo, ProvisioningError>> ApplyReadmeOverrideAsync(
        Assignment assignment, ForgejoRepo repo)
    {
        if (string.IsNullOrWhiteSpace(assignment.ReadmeMarkdown))
        {
            return repo;
        }

        OneOf<Success, GitError> overwrite = await git.OverwriteFileAsync(
            repo.CloneUrl, "README.md", assignment.ReadmeMarkdown, "Override README");
        if (overwrite.Failure is { } overwriteFailed)
        {
            return new ProvisioningError(overwriteFailed.Reason);
        }

        return repo;
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
