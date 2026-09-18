using LeoClassroom.Services.Auth;
using LeoClassroom.Services.Forgejo;
using LeoClassroom.Services.Security;
using LeoClassroom.Services.Util;
using LeoClassroom.Persistence.Model;
using LeoClassroom.Persistence.Util;
using Microsoft.Extensions.Options;
using OneOf;
using OneOf.Types;

namespace LeoClassroom.Services;

/// <param name="Username">The Forgejo username, which is the caller's IF number</param>
/// <param name="IssuedAt">When a password was last issued, or null if never</param>
/// <param name="Managed">
///     Whether this application manages the password at all. False when Forgejo accounts are bound to an
///     external login source, in which case the credential comes from there.
/// </param>
public sealed record GitCredentialStatus(string Username, Instant? IssuedAt, bool Managed);

/// <param name="Password">Shown to the caller once and then unrecoverable</param>
public sealed record IssuedGitCredential(string Username, string Password, Instant IssuedAt);

/// <summary>
///     The git password a user authenticates to Forgejo with
/// </summary>
/// <remarks>
///     <para>
///         Student repositories are private, so every user needs a credential for git over HTTPS. Rather than
///         delegating that to an external login source, the backend - which is already Forgejo administrator -
///         creates the account and sets a random password on demand.
///     </para>
///     <para>
///         The password is returned once and never stored: only the issue timestamp is kept. A user who
///         forgets theirs gets a new one, and one who changes it inside Forgejo simply keeps using theirs -
///         nothing here tracks it.
///     </para>
/// </remarks>
public interface IGitCredentialService
{
    public ValueTask<OneOf<GitCredentialStatus, NotFound>> GetStatusAsync();

    /// <summary>
    ///     Issues a new random password, replacing any existing one
    /// </summary>
    public ValueTask<OneOf<IssuedGitCredential, NotFound, ForgejoError>> ResetAsync();
}

internal sealed class GitCredentialService(
    IUnitOfWork uow,
    ICurrentUser currentUser,
    IForgejoClient forgejo,
    IOptions<ForgejoSettings> settings,
    IClock clock,
    ILogger<GitCredentialService> logger) : IGitCredentialService
{
    public async ValueTask<OneOf<GitCredentialStatus, NotFound>> GetStatusAsync()
    {
        User? user = await uow.UserRepository.GetTrackedByStudentIdAsync(currentUser.StudentId);

        return user is null
            ? new NotFound()
            : new GitCredentialStatus(user.StudentId, user.GitCredentialIssuedAt,
                                      Managed: settings.Value.AuthSourceId == 0);
    }

    public async ValueTask<OneOf<IssuedGitCredential, NotFound, ForgejoError>> ResetAsync()
    {
        User? user = await uow.UserRepository.GetTrackedByStudentIdAsync(currentUser.StudentId);
        if (user is null)
        {
            return new NotFound();
        }

        if (settings.Value.AuthSourceId != 0)
        {
            // the account authenticates against an external source; a local password would never be consulted
            logger.LogWarning("Refused to issue a git password while Forgejo accounts are bound to source {SourceId}",
                              settings.Value.AuthSourceId);

            return new ForgejoError(409, "Forgejo accounts use an external login source, so this application "
                                         + "does not manage their passwords.");
        }

        // the account may not exist yet: without the LDAP sync nothing else creates it
        OneOf<Success, ForgejoError> ensured =
            await forgejo.EnsureUserAsync(user.StudentId, user.Email, settings.Value.AuthSourceId);
        if (ensured.Failure is { } ensureFailed)
        {
            return ensureFailed;
        }

        string password = GitCredentialGenerator.Generate();
        OneOf<Success, NotFound, ForgejoError> applied =
            await forgejo.SetUserPasswordAsync(user.StudentId, password);

        return await applied.Match<ValueTask<OneOf<IssuedGitCredential, NotFound, ForgejoError>>>(
            success => RecordIssueAsync(user, password),
            notFound => ValueTask.FromResult<OneOf<IssuedGitCredential, NotFound, ForgejoError>>(notFound),
            error => ValueTask.FromResult<OneOf<IssuedGitCredential, NotFound, ForgejoError>>(error));
    }

    private async ValueTask<OneOf<IssuedGitCredential, NotFound, ForgejoError>> RecordIssueAsync(
        User user, string password)
    {
        Instant issuedAt = clock.GetCurrentInstant();
        user.GitCredentialIssuedAt = issuedAt;
        await uow.SaveChangesAsync();

        logger.LogInformation("Issued a new git password for {StudentId}", user.StudentId);

        return new IssuedGitCredential(user.StudentId, password, issuedAt);
    }
}
